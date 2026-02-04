"""Escalate step executor - escalates to human."""
from __future__ import annotations
import logging
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class EscalateExecutor(BaseStepExecutor):
    """
    Executes Escalate steps - escalates ticket to human operator.

    Assigns ticket to escalation group and adds context
    about why automation couldn't complete.
    """

    @property
    def step_type(self) -> str:
        return StepType.ESCALATE.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute escalation step.

        Configuration options:
        - target_group: Assignment group for escalation
        - preserve_work_notes: Add automation work notes
        - reason_template: Template for escalation reason
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}
        target_group = config.get("target_group", "Level 2 Support")

        try:
            # Build escalation reason
            reason = self._build_escalation_reason(config, context)

            # Update ticket for escalation
            await self._escalate_ticket(context, target_group, reason, config)

            # Mark context as escalated
            context.escalate(reason)

            result.complete({
                "escalated": True,
                "target_group": target_group,
                "reason": reason,
            })

            logger.info(f"Ticket {context.ticket_id} escalated to '{target_group}': {reason}")

        except Exception as e:
            logger.error(f"Escalation failed: {e}")
            result.fail(str(e))

        return result

    def _build_escalation_reason(
        self,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> str:
        """Build escalation reason from context."""
        reasons = []

        # Check classification confidence
        confidence = context.get_variable("confidence", 0)
        if confidence < 0.8:
            reasons.append(f"Low classification confidence ({confidence:.2f})")

        # Check validation errors
        validation_errors = context.get_step_output("validate-request", "validation_errors", [])
        if validation_errors:
            reasons.append(f"Validation failed: {', '.join(validation_errors)}")

        # Check execution errors
        for step_name, step_result in context.step_results.items():
            if step_result.status == ExecutionStatus.FAILED:
                reasons.append(f"Step '{step_name}' failed: {step_result.error}")

        # Check for unknown ticket type
        ticket_type = context.get_variable("ticket_type")
        if ticket_type == "unknown":
            reasons.append("Could not classify ticket type")

        if not reasons:
            reasons.append("Escalated by workflow rule")

        return "; ".join(reasons)

    def _build_work_notes(self, context: ExecutionContext, reason: str) -> str:
        """Build detailed work notes for escalation."""
        lines = [
            "=== Lucid IT Agent - Automated Processing Escalation ===",
            "",
            f"Reason: {reason}",
            "",
            "Automation Context:",
            f"  Ticket Type: {context.get_variable('ticket_type', 'Unknown')}",
            f"  Confidence: {context.get_variable('confidence', 'N/A')}",
            f"  Affected User: {context.get_variable('affected_user', 'Not identified')}",
            "",
            "Steps Executed:",
        ]

        for step_name, step_result in context.step_results.items():
            status_icon = "PASS" if step_result.status == ExecutionStatus.COMPLETED else "FAIL"
            line = f"  [{status_icon}] {step_name}: {step_result.status.value}"
            if step_result.error:
                line += f" - {step_result.error}"
            lines.append(line)

        # Add any execution output details
        execute_output = context.get_step_output("execute-reset", "message")
        if execute_output:
            lines.extend(["", f"Execution Detail: {execute_output}"])

        action_result = context.get_step_output("execute-reset", "action_result")
        if action_result:
            lines.extend(["", f"Tool Server Response: {action_result}"])

        return "\n".join(lines)

    async def _escalate_ticket(
        self,
        context: ExecutionContext,
        target_group: str,
        reason: str,
        config: dict[str, Any],
    ) -> None:
        """Update ServiceNow ticket for escalation."""
        # Build detailed work notes with automation context
        work_notes = self._build_work_notes(context, reason)

        sys_id = context.ticket_data.get("sys_id", "")
        if not sys_id:
            logger.warning(f"No sys_id for ticket {context.ticket_id}, cannot update ServiceNow")
            context.set_variable("escalation_work_notes", work_notes)
            return

        client = context.servicenow_client
        if not client:
            logger.warning("No ServiceNow client available, logging work notes only")
            context.set_variable("escalation_work_notes", work_notes)
            return

        try:
            # Add detailed work notes
            await client.add_work_note(sys_id, work_notes)
            logger.info(f"Escalation details written to ticket {context.ticket_id}")
        except Exception as e:
            logger.error(f"Failed to write escalation work notes: {e}")

        context.set_variable("escalation_work_notes", work_notes)
