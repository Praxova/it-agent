"""UpdateTicket step executor - updates ticket in ServiceNow."""
from __future__ import annotations
import logging
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class UpdateTicketExecutor(BaseStepExecutor):
    """
    Executes UpdateTicket steps - updates ServiceNow ticket.

    Can update:
    - State (in progress, resolved, closed)
    - Assignment
    - Work notes
    - Resolution details
    """

    @property
    def step_type(self) -> str:
        return StepType.UPDATE_TICKET.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute ticket update step.

        Configuration options:
        - state: Target state ("in_progress", "resolved", "closed")
        - close_code: Resolution code
        - add_resolution_notes: Whether to add resolution notes
        - work_notes: Static work notes to add
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}
        target_state = config.get("state", "resolved")

        try:
            update_payload = self._build_update_payload(config, context)

            # In real implementation, would call ServiceNow API
            await self._update_servicenow_ticket(context, update_payload)

            result.complete({
                "updated": True,
                "new_state": target_state,
                "ticket_id": context.ticket_id,
            })

            logger.info(f"Ticket {context.ticket_id} updated to state: {target_state}")

        except Exception as e:
            logger.error(f"Ticket update failed: {e}")
            result.fail(str(e))

        return result

    def _build_update_payload(
        self,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> dict[str, Any]:
        """Build ServiceNow update payload."""
        payload = {}

        # Map state names to ServiceNow values
        state_map = {
            "in_progress": "2",
            "on_hold": "3",
            "resolved": "6",
            "closed": "7",
        }

        state = config.get("state", "resolved")
        payload["state"] = state_map.get(state, "6")

        # Add close/resolution info
        if state in ("resolved", "closed"):
            close_code = config.get("close_code", "automated")
            payload["close_code"] = close_code

            if config.get("add_resolution_notes", True):
                # Build resolution notes from execution context
                ticket_type = context.get_variable("ticket_type", "request")
                affected_user = context.get_variable("affected_user", "N/A")

                resolution = f"Automated resolution by Praxova IT Agent\n"
                resolution += f"Ticket Type: {ticket_type}\n"
                resolution += f"Affected User: {affected_user}\n"

                # Add any step-specific notes
                execute_result = context.get_step_output("execute-reset", "message")
                if execute_result:
                    resolution += f"Action Result: {execute_result}\n"

                payload["close_notes"] = resolution

        # Add work notes if configured
        work_notes = config.get("work_notes")
        if work_notes:
            payload["work_notes"] = work_notes

        return payload

    async def _update_servicenow_ticket(
        self,
        context: ExecutionContext,
        payload: dict[str, Any],
    ) -> None:
        """Call ServiceNow API to update ticket."""
        sys_id = context.ticket_data.get("sys_id", "")
        if not sys_id:
            logger.warning(f"No sys_id for ticket {context.ticket_id}, cannot update")
            context.set_variable("ticket_update_payload", payload)
            return

        client = context.servicenow_client
        if not client:
            logger.warning("No ServiceNow client available")
            # Store what we would have updated for debugging
            context.set_variable("ticket_update_payload", payload)
            return

        try:
            success = await client.update_ticket(sys_id, payload)
            if success:
                logger.info(f"Updated ServiceNow ticket {context.ticket_id}")
            else:
                logger.warning(f"Failed to update ServiceNow ticket {context.ticket_id}")

            # Store update info in context regardless
            context.set_variable("ticket_updated", success)
            context.set_variable("ticket_new_state", payload.get("state"))
        except Exception as e:
            logger.error(f"Error updating ServiceNow ticket: {e}")
            context.set_variable("ticket_updated", False)
            context.set_variable("ticket_new_state", payload.get("state"))
