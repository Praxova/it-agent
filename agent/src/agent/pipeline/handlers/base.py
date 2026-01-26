"""Base handler class for ticket processing."""

from abc import ABC, abstractmethod
from dataclasses import dataclass

from connectors import Ticket


@dataclass
class HandlerResult:
    """Result from a ticket handler.

    Attributes:
        success: Whether the handler successfully processed the ticket.
        message: Internal message for logging.
        customer_message: Customer-visible comment (if successful).
        work_notes: Internal work notes.
        should_close: Whether to close the ticket.
        error: Error message if failed.
    """

    success: bool
    message: str
    customer_message: str | None = None
    work_notes: str | None = None
    should_close: bool = False
    error: str | None = None


class BaseHandler(ABC):
    """Base class for ticket type handlers."""

    @property
    @abstractmethod
    def handles_ticket_types(self) -> list[str]:
        """List of TicketType values this handler can process.

        Returns:
            List of ticket type strings.
        """
        ...

    @abstractmethod
    async def handle(
        self,
        ticket: Ticket,
        classification: "ClassificationResult",
    ) -> HandlerResult:
        """Process the ticket.

        Args:
            ticket: The ticket to process.
            classification: The classification result with extracted entities.

        Returns:
            HandlerResult indicating success/failure and messages.
        """
        ...

    @abstractmethod
    async def validate(
        self,
        ticket: Ticket,
        classification: "ClassificationResult",
    ) -> tuple[bool, str | None]:
        """Validate that we can handle this ticket.

        Args:
            ticket: The ticket to validate.
            classification: The classification result.

        Returns:
            Tuple of (is_valid, error_message).
        """
        ...
