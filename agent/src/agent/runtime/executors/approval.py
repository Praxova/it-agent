"""Approval step executor — auto-approve or submit to portal queue."""
from __future__ import annotations
import logging
import re
from typing import Any, TYPE_CHECKING

import httpx

from .base import BaseStepExecutor
from ..execution_context import ExecutionStatus, StepResult

if TYPE_CHECKING:
    from ..execution_context import ExecutionContext
    from ..models import WorkflowStepExportInfo, RulesetExportInfo

logger = logging.getLogger(__name__)


class ApprovalExecutor(BaseStepExecutor):
    """Handles Approval steps — auto-approve or submit to portal queue."""

    @property
    def step_type(self) -> str:
        return "Approval"

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """Execute approval step."""
        config = step.configuration or {}

        description_template = config.get("description_template", "")
        auto_approve_threshold = config.get("auto_approve_threshold")
        timeout_minutes = config.get("timeout_minutes")
        timeout_action = config.get("timeout_action", "escalate")

        # Get confidence from context variables
        confidence = context.variables.get("confidence")

        # Auto-approve check
        if (
            auto_approve_threshold is not None
            and confidence is not None
            and float(confidence) >= float(auto_approve_threshold)
        ):
            logger.info(
                f"Auto-approved: confidence {confidence} >= threshold {auto_approve_threshold}"
            )
            context.variables["outcome"] = "approved"
            context.variables["auto_approved"] = True
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.COMPLETED,
                output={"outcome": "approved", "auto_approved": True},
            )

        # Render proposed action from template
        proposed_action = self._render_template(description_template, context)

        # Build context snapshot (includes _ticket_data for resume)
        snapshot = dict(context.variables)
        snapshot["_ticket_data"] = context.ticket_data
        snapshot["_workflow_stack"] = list(context.workflow_stack)

        # Determine workflow name from stack
        workflow_name = (
            context.workflow_stack[-1] if context.workflow_stack else "unknown"
        )

        # Build request body
        body = {
            "workflowName": workflow_name,
            "stepName": step.name,
            "agentName": context.variables.get("agent_name", "unknown"),
            "ticketId": context.ticket_id,
            "ticketShortDescription": context.ticket_data.get(
                "short_description", ""
            ),
            "proposedAction": proposed_action,
            "contextSnapshot": snapshot,
            "resumeAfterStep": step.name,
            "autoApproveThreshold": (
                float(auto_approve_threshold)
                if auto_approve_threshold is not None
                else None
            ),
            "confidence": (
                float(confidence) if confidence is not None else None
            ),
            "timeoutMinutes": (
                int(timeout_minutes) if timeout_minutes is not None else None
            ),
        }

        # Submit to Admin Portal (use authenticated portal client if available)
        try:
            url = f"{context.admin_portal_url}/api/approvals"
            if context.portal_client:
                response = await context.portal_client.post(url, json=body, timeout=15.0)
            else:
                async with httpx.AsyncClient() as client:
                    response = await client.post(url, json=body, timeout=15.0)
            response.raise_for_status()
            data = response.json()

            approval_id = str(data.get("id", ""))
            approval_status = data.get("status", "Pending")

            # Server-side auto-approve
            if approval_status == "AutoApproved":
                logger.info(
                    f"Server auto-approved approval {approval_id}"
                )
                context.variables["outcome"] = "approved"
                context.variables["auto_approved"] = True
                return StepResult(
                    step_name=step.name,
                    step_type=step.step_type.value,
                    status=ExecutionStatus.COMPLETED,
                    output={"outcome": "approved", "auto_approved": True},
                )

            # Pending — suspend workflow
            logger.info(
                f"Approval {approval_id} pending, suspending workflow"
            )
            context.variables["outcome"] = "pending"
            context.variables["approval_id"] = approval_id
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.SUSPENDED,
                output={"outcome": "pending", "approval_id": approval_id},
            )

        except Exception as e:
            logger.error(f"Failed to submit approval request: {e}")
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.FAILED,
                error=f"Approval submission failed: {e}",
            )

    def _render_template(
        self, template: str, context: ExecutionContext
    ) -> str:
        """Render a mustache-style template with context values.

        Substitutes {{key}} placeholders. Missing variables are left
        as literal {{key}} placeholders.
        """
        if not template:
            return ""

        # Build values dict from variables + ticket_data
        values: dict[str, Any] = {}
        values.update(context.ticket_data)
        values.update(context.variables)

        def replacer(match: re.Match) -> str:
            key = match.group(1)
            if key in values:
                return str(values[key])
            return match.group(0)  # Leave as {{key}}

        return re.sub(r"\{\{(\w+)\}\}", replacer, template)
