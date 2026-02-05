"""Manual trigger provider — polls Admin Portal for manually submitted work items."""
from __future__ import annotations
import logging

import httpx

from .base import TriggerProvider, TriggerType, WorkItem
from ..execution_context import ExecutionContext, ExecutionStatus

logger = logging.getLogger(__name__)


class ManualTriggerProvider(TriggerProvider):
    """
    Trigger provider for manually submitted work items.

    Polls the Admin Portal for pending manual submissions,
    executes them through the workflow, and reports results back.
    """

    def __init__(self, admin_portal_url: str, agent_name: str):
        self._admin_portal_url = admin_portal_url.rstrip("/")
        self._agent_name = agent_name

    @property
    def trigger_type(self) -> TriggerType:
        return TriggerType.MANUAL

    @property
    def display_name(self) -> str:
        return "Manual Trigger"

    async def poll(self) -> list[WorkItem]:
        """Fetch pending manual submissions from Admin Portal."""
        url = f"{self._admin_portal_url}/api/agents/by-name/{self._agent_name}/manual-submissions/pending"
        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(url, timeout=10.0)
                if response.status_code == 404:
                    return []
                response.raise_for_status()
                items_data = response.json()
                return [self._to_work_item(item) for item in items_data]
        except Exception as e:
            logger.debug(f"Manual submission poll failed: {e}")
            return []

    def _to_work_item(self, data: dict) -> WorkItem:
        """Convert portal submission to WorkItem."""
        submission_id = data["id"]
        extra = data.get("extraData") or data.get("extra_data") or {}

        # Build ticket_data dict matching ServiceNow-like shape
        # so existing workflow steps work without modification
        ticket_data = {
            "short_description": data.get("title", ""),
            "description": data.get("description", ""),
            "caller_id": data.get("requester") or extra.get("caller_id", "manual"),
            "number": f"MANUAL-{submission_id[:8].upper()}",
            "sys_id": submission_id,
            **extra,
        }

        return WorkItem(
            id=submission_id,
            source_type=TriggerType.MANUAL,
            data=ticket_data,
            title=data.get("title", ""),
            description=data.get("description", ""),
            requester=data.get("requester"),
            display_id=f"MANUAL-{submission_id[:8].upper()}",
        )

    async def acknowledge(self, item: WorkItem) -> None:
        """Mark submission as picked up in portal."""
        url = f"{self._admin_portal_url}/api/manual-submissions/{item.id}/acknowledge"
        try:
            async with httpx.AsyncClient() as client:
                await client.patch(url, timeout=10.0)
        except Exception as e:
            logger.warning(f"Failed to acknowledge manual item {item.display_id}: {e}")

    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """Report successful completion to portal."""
        await self._report_result(item, "completed", self._build_message(context))

    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """Report escalation to portal."""
        await self._report_result(
            item, "escalated",
            f"Escalated: {context.escalation_reason}"
        )

    async def fail(self, item: WorkItem, error: str) -> None:
        """Report failure to portal."""
        await self._report_result(item, "failed", f"Failed: {error}")

    async def _report_result(self, item: WorkItem, status: str, message: str) -> None:
        url = f"{self._admin_portal_url}/api/manual-submissions/{item.id}/result"
        try:
            async with httpx.AsyncClient() as client:
                await client.patch(url, json={
                    "status": status,
                    "message": message,
                }, timeout=10.0)
        except Exception as e:
            logger.warning(f"Failed to report result for {item.display_id}: {e}")

    def _build_message(self, context: ExecutionContext) -> str:
        """Build a human-readable result message from execution context."""
        completed_steps = [
            name for name, result in context.step_results.items()
            if result.status == ExecutionStatus.COMPLETED
        ]
        return f"Completed successfully. Steps executed: {', '.join(completed_steps)}"
