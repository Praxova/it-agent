"""Notify step executor - sends notifications."""
from __future__ import annotations
import logging
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class NotifyExecutor(BaseStepExecutor):
    """
    Executes Notify steps - sends notifications.

    Supports channels:
    - ticket-comment: Add comment to ServiceNow ticket
    - email: Send email notification
    - teams: Send Teams message
    """

    @property
    def step_type(self) -> str:
        return StepType.NOTIFY.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute notification step.

        Configuration options:
        - channel: "ticket-comment", "email", "teams"
        - template: Template name or inline template
        - include_temp_password: Whether to include temp password in message
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}
        channel = config.get("channel", "ticket-comment")
        template = config.get("template", "default")

        try:
            # Build the notification message
            message = self._build_message(template, config, context)

            # Send via appropriate channel
            if channel == "ticket-comment":
                await self._add_ticket_comment(context, message, config)
            elif channel == "email":
                await self._send_email(context, message, config)
            elif channel == "teams":
                await self._send_teams_message(context, message, config)
            else:
                logger.warning(f"Unknown notification channel: {channel}")

            result.complete({
                "notified": True,
                "channel": channel,
                "message_preview": message[:200] if len(message) > 200 else message,
            })

            logger.info(f"Notification sent via {channel}")

        except Exception as e:
            logger.error(f"Notification failed: {e}")
            result.fail(str(e))

        return result

    def _build_message(
        self,
        template: str,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> str:
        """Build notification message from template."""
        # Get action result from previous execute step
        execute_result = context.get_step_output("execute-reset", "action_result", {})
        affected_user = context.get_variable("affected_user", "the user")
        ticket_type = context.get_variable("ticket_type", "request")

        # Template selection
        if template == "password-reset-success":
            temp_password = execute_result.get("temp_password", "[provided securely]")
            include_password = config.get("include_temp_password", False)

            if include_password:
                return f"""Your password reset request has been completed.

User: {affected_user}
Temporary Password: {temp_password}

Please log in and change your password immediately.

This action was performed automatically by the IT helpdesk system."""
            else:
                return f"""Your password reset request has been completed.

User: {affected_user}

The temporary password has been sent via a secure channel.
Please log in and change your password immediately.

This action was performed automatically by the IT helpdesk system."""

        elif template == "group-access-granted":
            target_group = context.get_variable("target_group", "the requested group")
            return f"""Group access has been granted.

User: {affected_user}
Group: {target_group}

This action was performed automatically by the IT helpdesk system."""

        elif template == "escalation":
            reason = context.escalation_reason or "Manual review required"
            return f"""This ticket has been escalated to a human operator.

Reason: {reason}

A technician will review your request shortly."""

        else:
            # Default template
            return f"""Your {ticket_type} request has been processed.

This action was performed automatically by the IT helpdesk system.
If you have questions, please reply to this ticket."""

    async def _add_ticket_comment(
        self,
        context: ExecutionContext,
        message: str,
        config: dict[str, Any],
    ) -> None:
        """Add comment to ServiceNow ticket."""
        # In real implementation, would call ServiceNow API
        # For now, just log
        logger.info(f"Would add ticket comment to {context.ticket_id}:\n{message}")

        # Store for later use
        context.set_variable("last_notification", message)

    async def _send_email(
        self,
        context: ExecutionContext,
        message: str,
        config: dict[str, Any],
    ) -> None:
        """Send email notification."""
        recipient = config.get("recipient") or context.ticket_data.get("caller_email")
        logger.info(f"Would send email to {recipient}:\n{message}")

    async def _send_teams_message(
        self,
        context: ExecutionContext,
        message: str,
        config: dict[str, Any],
    ) -> None:
        """Send Teams notification."""
        channel = config.get("teams_channel", "IT-Notifications")
        logger.info(f"Would send Teams message to {channel}:\n{message}")
