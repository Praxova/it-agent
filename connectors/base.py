"""Base connector classes and shared models for ticket system integration."""

from abc import ABC, abstractmethod
from datetime import datetime
from enum import IntEnum

from pydantic import BaseModel


class TicketState(IntEnum):
    """ServiceNow incident state values."""

    NEW = 1
    IN_PROGRESS = 2
    ON_HOLD = 3
    RESOLVED = 6
    CLOSED = 7


class Ticket(BaseModel):
    """Internal ticket representation (connector-agnostic).

    Attributes:
        id: Unique identifier (e.g., sys_id in ServiceNow).
        number: Human-readable ticket number (e.g., INC0010001).
        short_description: Brief summary of the issue.
        description: Detailed description of the issue.
        state: Current ticket state.
        priority: Priority level (1=Critical to 5=Planning).
        caller_username: Username of the affected user.
        assignment_group: Name of the group assigned to handle the ticket.
        created_at: Timestamp when the ticket was created.
        updated_at: Timestamp when the ticket was last updated.
    """

    id: str
    number: str
    short_description: str
    description: str | None = None
    state: TicketState
    priority: int
    caller_username: str
    assignment_group: str
    created_at: datetime
    updated_at: datetime


class TicketUpdate(BaseModel):
    """Fields that can be updated on a ticket.

    Attributes:
        state: New state for the ticket.
        assigned_to: Username to assign the ticket to.
        work_notes: Internal notes (not visible to caller).
        comments: Customer-visible comments.
    """

    state: TicketState | None = None
    assigned_to: str | None = None
    work_notes: str | None = None
    comments: str | None = None


class BaseConnector(ABC):
    """Abstract base class for all ticket system connectors.

    All concrete connector implementations must inherit from this class
    and implement all abstract methods.
    """

    @abstractmethod
    async def poll_queue(self, since: datetime | None = None) -> list[Ticket]:
        """Fetch tickets updated since given time.

        Args:
            since: Only fetch tickets updated after this timestamp.
                   If None, fetch all open tickets.

        Returns:
            List of tickets matching the criteria.
        """
        ...

    @abstractmethod
    async def get_ticket(self, ticket_id: str) -> Ticket:
        """Fetch a single ticket by ID.

        Args:
            ticket_id: Unique identifier for the ticket.

        Returns:
            The requested ticket.

        Raises:
            Exception: If ticket not found or request fails.
        """
        ...

    @abstractmethod
    async def update_ticket(self, ticket_id: str, update: TicketUpdate) -> Ticket:
        """Update ticket fields.

        Args:
            ticket_id: Unique identifier for the ticket.
            update: Fields to update.

        Returns:
            The updated ticket.

        Raises:
            Exception: If update fails.
        """
        ...

    @abstractmethod
    async def add_work_note(self, ticket_id: str, note: str) -> None:
        """Add internal work note (not visible to caller).

        Args:
            ticket_id: Unique identifier for the ticket.
            note: Internal note text.

        Raises:
            Exception: If operation fails.
        """
        ...

    @abstractmethod
    async def add_comment(self, ticket_id: str, comment: str) -> None:
        """Add customer-visible comment.

        Args:
            ticket_id: Unique identifier for the ticket.
            comment: Comment text.

        Raises:
            Exception: If operation fails.
        """
        ...

    @abstractmethod
    async def close_ticket(self, ticket_id: str, resolution: str) -> Ticket:
        """Close ticket with resolution notes.

        Args:
            ticket_id: Unique identifier for the ticket.
            resolution: Resolution notes describing how the issue was resolved.

        Returns:
            The closed ticket.

        Raises:
            Exception: If operation fails.
        """
        ...
