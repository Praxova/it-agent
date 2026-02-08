"""Clarify step executor — post question to user, suspend for reply."""
from __future__ import annotations
import json
import logging
from typing import Any, TYPE_CHECKING

import httpx

from .base import BaseStepExecutor
from ..execution_context import ExecutionStatus, StepResult
from ..utils import resolve_template

if TYPE_CHECKING:
    from ..execution_context import ExecutionContext
    from ..models import WorkflowStepExportInfo, RulesetExportInfo

logger = logging.getLogger(__name__)


class ClarifyExecutor(BaseStepExecutor):
    """
    Handles Clarify steps — post question to user, suspend for reply.

    Configuration options:
    - question_template: Mustache-style template for the question
      e.g., "Which device should I install the software on?\n{{computer_list}}"
    - set_ticket_state: ServiceNow state to set (e.g., "-5" for Awaiting User Info)
    """

    @property
    def step_type(self) -> str:
        return "Clarify"

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """Execute clarify step."""
        config = step.configuration or {}

        question_template = config.get("question_template", "")
        set_ticket_state = config.get("set_ticket_state")

        # Render question from template
        rendered_question = resolve_template(question_template, context)

        if not rendered_question:
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.FAILED,
                error="No question_template specified in step configuration",
            )

        # Get ticket sys_id
        sys_id = context.ticket_data.get("sys_id")
        if not sys_id:
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.FAILED,
                error="No sys_id in ticket_data — cannot post comment to ServiceNow",
            )

        # Post customer-visible comment to ServiceNow
        try:
            snow_client = context.servicenow_client
            if not snow_client:
                return StepResult(
                    step_name=step.name,
                    step_type=step.step_type.value,
                    status=ExecutionStatus.FAILED,
                    error="No ServiceNow client available",
                )

            comment_ok = await snow_client.add_comment(sys_id, rendered_question)
            if not comment_ok:
                return StepResult(
                    step_name=step.name,
                    step_type=step.step_type.value,
                    status=ExecutionStatus.FAILED,
                    error="Failed to post clarification comment to ServiceNow",
                )

            logger.info(f"Posted clarification question to ticket {context.ticket_id}")

            # Optionally set ticket state to "Awaiting User Info"
            if set_ticket_state:
                await snow_client.update_ticket(
                    sys_id, {"state": str(set_ticket_state)}
                )

        except Exception as e:
            logger.error(f"Failed to post clarification to ServiceNow: {e}")
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.FAILED,
                error=f"ServiceNow comment failed: {e}",
            )

        # Build context snapshot (same pattern as ApprovalExecutor)
        snapshot = dict(context.variables)
        snapshot["_ticket_data"] = context.ticket_data
        snapshot["_workflow_stack"] = list(context.workflow_stack)

        # Determine workflow name from stack
        workflow_name = (
            context.workflow_stack[-1] if context.workflow_stack else "unknown"
        )

        # Build request body
        body = {
            "agentName": context.variables.get("agent_name", "unknown"),
            "workflowName": workflow_name,
            "stepName": step.name,
            "ticketId": context.ticket_id,
            "ticketSysId": sys_id,
            "question": rendered_question,
            "contextSnapshotJson": json.dumps(snapshot),
            "resumeAfterStep": step.name,
        }

        # Submit to Admin Portal
        try:
            async with httpx.AsyncClient() as client:
                url = f"{context.admin_portal_url}/api/clarifications"
                response = await client.post(url, json=body, timeout=15.0)
                response.raise_for_status()
                data = response.json()

            clarification_id = str(data.get("id", ""))

            logger.info(
                f"Clarification {clarification_id} created, suspending workflow"
            )
            context.variables["clarification_id"] = clarification_id
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.SUSPENDED,
                output={
                    "outcome": "pending",
                    "clarification_id": clarification_id,
                    "question": rendered_question,
                },
            )

        except Exception as e:
            logger.error(f"Failed to submit clarification request: {e}")
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.FAILED,
                error=f"Clarification submission failed: {e}",
            )

