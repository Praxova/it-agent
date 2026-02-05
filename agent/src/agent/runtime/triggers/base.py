"""Base classes for trigger providers."""
from __future__ import annotations
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

from ..execution_context import ExecutionContext


class TriggerType(str, Enum):
    """Supported trigger types."""
    SERVICENOW = "servicenow"
    MANUAL = "manual"
    WEBHOOK = "webhook"  # Future
    EMAIL = "email"      # Future
    JIRA = "jira"        # Future


@dataclass
class WorkItem:
    """
    Universal work item from any trigger source.

    This is the common interface between trigger providers and the workflow engine.
    Each trigger provider maps its source-specific data into this normalized format.
    The `data` dict is what gets passed to engine.execute() as ticket_data.
    """
    id: str                          # Unique ID in source system (sys_id, issue key, etc.)
    source_type: TriggerType         # Where this came from
    data: dict[str, Any]             # Source-specific fields (becomes ticket_data)
    raw: Any = None                  # Original object from source API (for advanced use)

    # Normalized fields (same regardless of source)
    title: str = ""                  # short_description / summary / subject
    description: str = ""            # description / body / content
    requester: str | None = None     # caller / reporter / from
    priority: str | None = None      # priority level
    created_at: datetime | None = None

    # Display identifier (ticket number, issue key, etc.)
    display_id: str = ""             # INC0010001, PROJ-123, etc.

    def __post_init__(self):
        if not self.display_id:
            self.display_id = self.id


class TriggerProvider(ABC):
    """
    Abstract interface for trigger providers.

    A trigger provider is responsible for:
    1. Discovering new work items from a source system
    2. Acknowledging that work has been picked up
    3. Reporting completion/failure/escalation back to the source

    The provider is created by the AgentRunner based on the workflow's
    declared trigger type and the agent's configured service accounts.
    """

    @property
    @abstractmethod
    def trigger_type(self) -> TriggerType:
        """The type of trigger this provider handles."""
        ...

    @property
    @abstractmethod
    def display_name(self) -> str:
        """Human-readable name for logging."""
        ...

    @abstractmethod
    async def poll(self) -> list[WorkItem]:
        """
        Check for new work items.

        For polling sources (ServiceNow, Jira, Email): query for new items.
        For push sources (Webhook, Manual): return items from internal queue.

        Returns:
            List of new work items to process. Empty list if none available.
        """
        ...

    @abstractmethod
    async def acknowledge(self, item: WorkItem) -> None:
        """
        Acknowledge that a work item has been picked up for processing.

        Typically: set status to "In Progress", add a work note, etc.
        Called BEFORE workflow execution begins.
        """
        ...

    @abstractmethod
    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """
        Report successful completion of a work item.

        Typically: set status to "Resolved", add completion notes, etc.
        Called when workflow execution completes successfully.
        """
        ...

    @abstractmethod
    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """
        Report that a work item was escalated to a human.

        Typically: add escalation notes, leave in an appropriate state.
        Called when workflow execution results in escalation.
        """
        ...

    @abstractmethod
    async def fail(self, item: WorkItem, error: str) -> None:
        """
        Report that processing of a work item failed.

        Typically: add error notes, leave accessible for retry/human review.
        Called when workflow execution fails unexpectedly.
        """
        ...

    async def startup(self) -> None:
        """
        Optional: Called when the agent starts up.

        Use for one-time initialization (e.g., starting an HTTP server for webhooks).
        Default implementation does nothing.
        """
        pass

    async def shutdown(self) -> None:
        """
        Optional: Called when the agent shuts down.

        Use for cleanup (e.g., stopping HTTP server, closing connections).
        Default implementation does nothing.
        """
        pass
