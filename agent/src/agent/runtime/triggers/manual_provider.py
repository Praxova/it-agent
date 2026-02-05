"""Manual trigger provider — submit work items via Admin Portal or API."""
from __future__ import annotations
import asyncio
import logging
from typing import Any

from .base import TriggerProvider, TriggerType, WorkItem
from ..execution_context import ExecutionContext

logger = logging.getLogger(__name__)


class ManualTriggerProvider(TriggerProvider):
    """
    Trigger provider for manually submitted work items.

    Work items are pushed into an internal queue via submit_work_item().
    This will be called from an API endpoint or the Admin Portal UI in Phase T2.

    For now, this is a stub that returns no items from poll().
    """

    def __init__(self):
        self._pending_items: list[WorkItem] = []
        self._lock = asyncio.Lock()

    @property
    def trigger_type(self) -> TriggerType:
        return TriggerType.MANUAL

    @property
    def display_name(self) -> str:
        return "Manual Trigger"

    async def poll(self) -> list[WorkItem]:
        """Return any pending manually-submitted items."""
        async with self._lock:
            items = self._pending_items.copy()
            return items

    async def acknowledge(self, item: WorkItem) -> None:
        """Remove item from pending queue."""
        async with self._lock:
            self._pending_items = [i for i in self._pending_items if i.id != item.id]
        logger.info(f"Manual work item {item.display_id} acknowledged")

    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """Log completion. Phase T2 will add result storage/callback."""
        logger.info(f"Manual work item {item.display_id} completed successfully")

    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """Log escalation. Phase T2 will add result storage/callback."""
        logger.info(
            f"Manual work item {item.display_id} escalated: {context.escalation_reason}"
        )

    async def fail(self, item: WorkItem, error: str) -> None:
        """Log failure. Phase T2 will add result storage/callback."""
        logger.warning(f"Manual work item {item.display_id} failed: {error}")

    async def submit_work_item(
        self,
        title: str,
        description: str,
        requester: str | None = None,
        extra_data: dict[str, Any] | None = None,
    ) -> WorkItem:
        """
        Submit a work item manually.

        This will be called by an API endpoint in Phase T2.

        Args:
            title: Short description of the request
            description: Full description
            requester: Who is requesting (optional)
            extra_data: Additional fields to include in ticket_data

        Returns:
            The created WorkItem
        """
        import uuid

        item_id = str(uuid.uuid4())
        display_id = f"MANUAL-{item_id[:8].upper()}"

        data = {
            "short_description": title,
            "description": description,
            "caller_id": requester or "manual",
            "number": display_id,
            "sys_id": item_id,
            **(extra_data or {}),
        }

        item = WorkItem(
            id=item_id,
            source_type=TriggerType.MANUAL,
            data=data,
            title=title,
            description=description,
            requester=requester,
            display_id=display_id,
        )

        async with self._lock:
            self._pending_items.append(item)

        logger.info(f"Manual work item submitted: {display_id}")
        return item
