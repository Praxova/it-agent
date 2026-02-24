"""ServiceNow trigger provider."""
from __future__ import annotations
import logging
from typing import Any

from .base import TriggerProvider, TriggerType, WorkItem
from ..execution_context import ExecutionContext, ExecutionStatus
from ..integrations.servicenow_client import ServiceNowClient, Ticket

logger = logging.getLogger(__name__)


class ServiceNowTriggerProvider(TriggerProvider):
    """
    Trigger provider that polls ServiceNow for new incidents.

    This is the extraction of logic previously embedded in AgentRunner.
    """

    def __init__(
        self,
        client: ServiceNowClient,
        assignment_group: str,
        poll_limit: int = 5,
    ):
        self._client = client
        self._assignment_group = assignment_group
        self._poll_limit = poll_limit

    @property
    def trigger_type(self) -> TriggerType:
        return TriggerType.SERVICENOW

    @property
    def display_name(self) -> str:
        return f"ServiceNow ({self._assignment_group})"

    async def poll(self) -> list[WorkItem]:
        """Poll ServiceNow for new tickets in the assignment group."""
        if not self._assignment_group:
            logger.warning("No assignment group configured on ServiceNow provider")
            return []

        tickets = await self._client.poll_queue(
            assignment_group=self._assignment_group,
            state="1",  # New
            limit=self._poll_limit,
        )

        return [self._ticket_to_work_item(t) for t in tickets]

    async def acknowledge(self, item: WorkItem) -> None:
        """Set ticket to In Progress and add work note."""
        ticket = self._get_ticket(item)
        await self._client.set_state(ticket.sys_id, "2")  # In Progress
        await self._client.add_work_note(
            ticket.sys_id,
            "Ticket picked up by Praxova IT Agent for automated processing."
        )

    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """Post completion notes and resolve the ticket."""
        ticket = self._get_ticket(item)

        try:
            if not context.get_variable("ticket_updated"):
                success_note = self._build_completion_note(context)
                await self._client.add_work_note(ticket.sys_id, success_note)
                await self._client.set_state(
                    ticket.sys_id, "6",  # Resolved
                    close_notes=f"Resolved by Praxova IT Agent - {context.get_variable('ticket_type', 'automated')}"
                )
        except Exception as e:
            logger.error(f"Failed to write completion notes for {item.display_id}: {e}")

    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """Add escalation notes. Leave ticket in In Progress for human pickup."""
        ticket = self._get_ticket(item)

        try:
            escalation_notes = context.get_variable("escalation_work_notes")
            if not escalation_notes:
                escalation_notes = (
                    f"Praxova IT Agent escalation:\n"
                    f"Reason: {context.escalation_reason}\n"
                    f"Ticket Type: {context.get_variable('ticket_type', 'Unknown')}\n"
                    f"Confidence: {context.get_variable('confidence', 'N/A')}"
                )
            await self._client.add_work_note(ticket.sys_id, escalation_notes)
        except Exception as e:
            logger.error(f"Failed to write escalation notes for {item.display_id}: {e}")

    async def fail(self, item: WorkItem, error: str) -> None:
        """Add error notes to the ticket."""
        ticket = self._get_ticket(item)

        try:
            await self._client.add_work_note(
                ticket.sys_id,
                f"Automated processing failed: {error}\nEscalating to human operator."
            )
        except Exception as e:
            logger.error(f"Failed to write error notes for {item.display_id}: {e}")

    def _ticket_to_work_item(self, ticket: Ticket) -> WorkItem:
        """Convert a ServiceNow Ticket to a generic WorkItem."""
        return WorkItem(
            id=ticket.sys_id,
            source_type=TriggerType.SERVICENOW,
            data=ticket.to_ticket_data(),
            raw=ticket,
            title=ticket.short_description,
            description=ticket.description,
            requester=ticket.caller_id,
            display_id=ticket.number,
        )

    def _get_ticket(self, item: WorkItem) -> Ticket:
        """Extract the original Ticket from a WorkItem."""
        if isinstance(item.raw, Ticket):
            return item.raw
        # Fallback: reconstruct minimal ticket from work item data
        return Ticket(
            sys_id=item.id,
            number=item.display_id,
            short_description=item.title,
            description=item.description,
            caller_id=item.requester or "",
            state="",
            assignment_group="",
        )

    def _build_completion_note(self, context: ExecutionContext) -> str:
        """Build work note summarizing successful completion."""
        lines = [
            "=== Praxova IT Agent - Automated Resolution ===",
            "",
            f"Ticket Type: {context.get_variable('ticket_type', 'request')}",
            f"Affected User: {context.get_variable('affected_user', 'N/A')}",
            "",
            "Steps Completed:",
        ]
        for step_name, step_result in context.step_results.items():
            lines.append(f"  [PASS] {step_name}")

        exec_msg = context.get_step_output("execute-reset", "message")
        if exec_msg:
            lines.extend(["", f"Result: {exec_msg}"])

        return "\n".join(lines)
