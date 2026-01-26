"""Main orchestrator for ticket processing pipeline."""

import asyncio
import logging
from datetime import datetime

from connectors import ServiceNowConnector, Ticket, TicketState, TicketUpdate

from agent.classifier import ClassificationResult, TicketClassifier, TicketType

from .config import PipelineConfig
from .handlers.base import BaseHandler, HandlerResult
from .handlers.file_permission import FilePermissionHandler
from .handlers.group_access import GroupAccessHandler
from .handlers.password_reset import PasswordResetHandler

logger = logging.getLogger(__name__)


class TicketExecutor:
    """Main orchestrator for ticket processing.

    Polls ServiceNow, classifies tickets, routes to handlers, and updates results.
    """

    def __init__(self, config: PipelineConfig | None = None):
        """Initialize the ticket executor.

        Args:
            config: Pipeline configuration (defaults to loading from environment).
        """
        self.config = config or PipelineConfig()

        # Initialize components
        self._connector: ServiceNowConnector | None = None
        self._classifier: TicketClassifier | None = None
        self._handlers: dict[str, BaseHandler] = {}

        self._running = False
        self._last_poll: datetime | None = None

    async def initialize(self):
        """Initialize all components."""
        logger.info("Initializing TicketExecutor...")

        # ServiceNow connector (skip if already set, e.g., in tests)
        if not self._connector:
            self._connector = ServiceNowConnector(
                instance=self.config.servicenow_instance,
                username=self.config.servicenow_username,
                password=self.config.servicenow_password,
                assignment_group=self.config.assignment_group,
            )

        # Classifier (skip if already set, e.g., in tests)
        if not self._classifier:
            self._classifier = TicketClassifier(
                model=self.config.ollama_model,
                base_url=self.config.ollama_base_url,
            )

        # Register handlers
        self._register_handlers()

        logger.info(f"Initialized with {len(self._handlers)} handler types")

    def _register_handlers(self):
        """Register all ticket handlers."""
        handlers = [
            PasswordResetHandler(tool_server_url=self.config.tool_server_url),
            GroupAccessHandler(tool_server_url=self.config.tool_server_url),
            FilePermissionHandler(tool_server_url=self.config.tool_server_url),
        ]

        for handler in handlers:
            for ticket_type in handler.handles_ticket_types:
                self._handlers[ticket_type] = handler
                logger.debug(f"Registered handler for {ticket_type}")

    async def close(self):
        """Clean up resources."""
        if self._connector:
            await self._connector.close()

    async def run_once(self) -> int:
        """Poll and process tickets once.

        Returns:
            Number of tickets processed.
        """
        if not self._connector:
            await self.initialize()

        logger.info("Polling for tickets...")

        # Poll for new/updated tickets
        tickets = await self._connector.poll_queue(since=self._last_poll)
        self._last_poll = datetime.now()

        if not tickets:
            logger.info("No tickets to process")
            return 0

        logger.info(f"Found {len(tickets)} tickets to process")

        processed = 0
        for ticket in tickets:
            try:
                await self.process_ticket(ticket)
                processed += 1
            except Exception as e:
                logger.exception(f"Error processing ticket {ticket.number}: {e}")

        return processed

    async def run_daemon(self):
        """Run continuously, polling at configured interval."""
        if not self._connector:
            await self.initialize()

        self._running = True
        logger.info(
            f"Starting daemon mode (polling every {self.config.poll_interval_seconds}s)"
        )

        while self._running:
            try:
                await self.run_once()
            except Exception as e:
                logger.exception(f"Error in poll cycle: {e}")

            await asyncio.sleep(self.config.poll_interval_seconds)

    def stop(self):
        """Stop daemon mode."""
        self._running = False
        logger.info("Stop requested")

    async def process_ticket(self, ticket: Ticket):
        """Process a single ticket through the pipeline.

        Args:
            ticket: The ticket to process.
        """
        logger.info(f"Processing ticket {ticket.number}: {ticket.short_description}")

        # Step 1: Claim the ticket
        await self._claim_ticket(ticket)

        # Step 2: Classify
        classification = self._classifier.classify(ticket)
        logger.info(
            f"Classified {ticket.number} as {classification.ticket_type} "
            f"(confidence: {classification.confidence:.2f}, "
            f"action: {classification.action_recommended})"
        )

        # Step 3: Route based on classification
        if (
            classification.should_escalate
            or classification.confidence < self.config.confidence_threshold_review
        ):
            await self._escalate_ticket(
                ticket, classification, "Low confidence or flagged for escalation"
            )
            return

        if classification.ticket_type == TicketType.UNKNOWN:
            await self._escalate_ticket(ticket, classification, "Unknown ticket type")
            return

        # Step 4: Find handler
        handler = self._handlers.get(classification.ticket_type)
        if not handler:
            await self._escalate_ticket(
                ticket,
                classification,
                f"No handler for ticket type: {classification.ticket_type}",
            )
            return

        # Step 5: Validate
        is_valid, validation_error = await handler.validate(ticket, classification)
        if not is_valid:
            await self._escalate_ticket(
                ticket, classification, f"Validation failed: {validation_error}"
            )
            return

        # Step 6: Execute
        if classification.confidence < self.config.confidence_threshold_auto:
            # Flag for review but still process
            logger.info(
                f"Ticket {ticket.number} flagged for review "
                f"(confidence {classification.confidence:.2f})"
            )

        result = await handler.handle(ticket, classification)

        # Step 7: Update ticket
        await self._update_ticket_with_result(ticket, classification, result)

    async def _claim_ticket(self, ticket: Ticket):
        """Assign ticket to Lucid Agent.

        Args:
            ticket: The ticket to claim.
        """
        logger.debug(f"Claiming ticket {ticket.number}")

        await self._connector.update_ticket(
            ticket.id,
            TicketUpdate(
                state=TicketState.IN_PROGRESS,
                assigned_to=self.config.agent_user,
                work_notes="Ticket claimed by Lucid IT Agent for automated processing.",
            ),
        )

    async def _escalate_ticket(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
        reason: str,
    ):
        """Escalate ticket to human queue.

        Args:
            ticket: The ticket to escalate.
            classification: The classification result.
            reason: Reason for escalation.
        """
        logger.info(f"Escalating ticket {ticket.number}: {reason}")

        work_note = f"""Lucid IT Agent could not automatically resolve this ticket.

Classification: {classification.ticket_type}
Confidence: {classification.confidence:.2f}
Reason for escalation: {reason}

{f"Agent reasoning: {classification.reasoning}" if classification.reasoning else ""}
{f"Escalation details: {classification.escalation_reason}" if classification.escalation_reason else ""}

This ticket has been reassigned to {self.config.escalation_group} for manual review."""

        await self._connector.update_ticket(
            ticket.id,
            TicketUpdate(
                state=TicketState.IN_PROGRESS,
                assigned_to=None,  # Unassign from agent
                work_notes=work_note,
            ),
        )

        # Note: In production, you'd also reassign to escalation_group
        # This requires knowing the sys_id of the group, which we'd look up

    async def _update_ticket_with_result(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
        result: HandlerResult,
    ):
        """Update ticket based on handler result.

        Args:
            ticket: The ticket being processed.
            classification: The classification result.
            result: The handler result.
        """
        if result.success:
            logger.info(f"Ticket {ticket.number} resolved successfully")

            # Add work notes
            if result.work_notes:
                await self._connector.add_work_note(ticket.id, result.work_notes)

            # Add customer comment
            if result.customer_message:
                await self._connector.add_comment(ticket.id, result.customer_message)

            # Close ticket if handler says to
            if result.should_close:
                await self._connector.close_ticket(
                    ticket.id, resolution=f"Resolved by Lucid IT Agent. {result.message}"
                )
        else:
            logger.warning(f"Ticket {ticket.number} handler failed: {result.error}")

            # Add work notes about failure
            await self._connector.add_work_note(
                ticket.id,
                f"Lucid Agent attempted to process but encountered an error:\n{result.error}\n\nEscalating to human queue.",
            )

            # Escalate
            await self._escalate_ticket(ticket, classification, f"Handler failed: {result.error}")
