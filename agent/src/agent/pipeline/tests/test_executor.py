"""Tests for TicketExecutor."""

import asyncio
from unittest.mock import AsyncMock, Mock, patch

import pytest

from connectors import Ticket, TicketState, TicketUpdate


from agent.pipeline.executor import TicketExecutor
from agent.pipeline.handlers.base import HandlerResult


class TestTicketExecutor:
    """Tests for TicketExecutor."""

    @pytest.mark.asyncio
    async def test_initialization(self, mock_config):
        """Test executor initialization."""
        executor = TicketExecutor(mock_config)

        with patch("agent.pipeline.executor.ServiceNowConnector"):
            with patch("agent.pipeline.executor.TicketClassifier"):
                await executor.initialize()

        assert executor._connector is not None
        assert executor._classifier is not None
        assert len(executor._handlers) > 0

    @pytest.mark.asyncio
    async def test_register_handlers(self, mock_config):
        """Test handler registration."""
        executor = TicketExecutor(mock_config)

        with patch("agent.pipeline.executor.ServiceNowConnector"):
            with patch("agent.pipeline.executor.TicketClassifier"):
                await executor.initialize()

        # Should have handlers for password reset and group access
        assert TicketType.PASSWORD_RESET in executor._handlers
        assert TicketType.GROUP_ACCESS_ADD in executor._handlers
        assert TicketType.GROUP_ACCESS_REMOVE in executor._handlers

    @pytest.mark.asyncio
    async def test_run_once_no_tickets(self, mock_config, mock_connector):
        """Test run_once with no tickets."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        with patch("agent.pipeline.executor.TicketClassifier"):
            await executor.initialize()

        processed = await executor.run_once()

        assert processed == 0
        mock_connector.poll_queue.assert_called_once()

    @pytest.mark.asyncio
    async def test_run_once_with_tickets(
        self, mock_config, mock_connector, sample_ticket, sample_classification_password_reset
    ):
        """Test run_once with tickets."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector
        mock_connector.poll_queue.return_value = [sample_ticket]

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_password_reset)

        # Mock handler
        mock_handler = Mock()
        mock_handler.validate = AsyncMock(return_value=(True, None))
        mock_handler.handle = AsyncMock(
            return_value=HandlerResult(
                success=True,
                message="Success",
                customer_message="Reset complete",
                work_notes="Done",
                should_close=True,
            )
        )

        executor._classifier = mock_classifier
        executor._handlers[TicketType.PASSWORD_RESET] = mock_handler

        processed = await executor.run_once()

        assert processed == 1
        mock_connector.update_ticket.assert_called()  # Claim
        mock_connector.close_ticket.assert_called()  # Close after success

    @pytest.mark.asyncio
    async def test_process_ticket_low_confidence(
        self, mock_config, mock_connector, sample_ticket, sample_classification_low_confidence
    ):
        """Test ticket processing with low confidence."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_low_confidence)
        executor._classifier = mock_classifier

        await executor.initialize()
        await executor.process_ticket(sample_ticket)

        # Should escalate
        calls = mock_connector.update_ticket.call_args_list
        assert any("escalat" in str(call).lower() for call in calls)

    @pytest.mark.asyncio
    async def test_process_ticket_unknown_type(
        self, mock_config, mock_connector, sample_ticket, sample_classification_unknown
    ):
        """Test ticket processing with unknown type."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_unknown)
        executor._classifier = mock_classifier

        await executor.initialize()
        await executor.process_ticket(sample_ticket)

        # Should escalate
        calls = mock_connector.update_ticket.call_args_list
        assert any("escalat" in str(call).lower() for call in calls)

    @pytest.mark.asyncio
    async def test_process_ticket_no_handler(
        self, mock_config, mock_connector, sample_ticket, sample_classification_password_reset
    ):
        """Test ticket processing with no handler available."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_password_reset)
        executor._classifier = mock_classifier

        await executor.initialize()

        # Clear handlers after initialization
        executor._handlers = {}

        await executor.process_ticket(sample_ticket)

        # Should escalate
        calls = mock_connector.update_ticket.call_args_list
        assert any("no handler" in str(call).lower() for call in calls)

    @pytest.mark.asyncio
    async def test_process_ticket_validation_failure(
        self, mock_config, mock_connector, sample_ticket, sample_classification_password_reset
    ):
        """Test ticket processing with validation failure."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_password_reset)
        executor._classifier = mock_classifier

        await executor.initialize()

        # Mock handler that fails validation (set after initialization)
        mock_handler = Mock()
        mock_handler.validate = AsyncMock(return_value=(False, "Missing required field"))
        executor._handlers[TicketType.PASSWORD_RESET] = mock_handler

        await executor.process_ticket(sample_ticket)

        # Should escalate
        calls = mock_connector.update_ticket.call_args_list
        assert any("validation failed" in str(call).lower() for call in calls)

    @pytest.mark.asyncio
    async def test_process_ticket_handler_success(
        self, mock_config, mock_connector, sample_ticket, sample_classification_password_reset
    ):
        """Test successful ticket processing."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_password_reset)
        executor._classifier = mock_classifier

        await executor.initialize()

        # Mock handler that succeeds (set after initialization)
        mock_handler = Mock()
        mock_handler.validate = AsyncMock(return_value=(True, None))
        mock_handler.handle = AsyncMock(
            return_value=HandlerResult(
                success=True,
                message="Password reset successful",
                customer_message="Your password has been reset",
                work_notes="Agent reset password",
                should_close=True,
            )
        )
        executor._handlers[TicketType.PASSWORD_RESET] = mock_handler

        await executor.process_ticket(sample_ticket)

        # Should add comment and close
        mock_connector.add_work_note.assert_called()
        mock_connector.add_comment.assert_called()
        mock_connector.close_ticket.assert_called()

    @pytest.mark.asyncio
    async def test_process_ticket_handler_failure(
        self, mock_config, mock_connector, sample_ticket, sample_classification_password_reset
    ):
        """Test ticket processing with handler failure."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        # Mock classifier
        mock_classifier = Mock()
        mock_classifier.classify = Mock(return_value=sample_classification_password_reset)
        executor._classifier = mock_classifier

        # Mock handler that fails
        mock_handler = Mock()
        mock_handler.validate = AsyncMock(return_value=(True, None))
        mock_handler.handle = AsyncMock(
            return_value=HandlerResult(
                success=False,
                message="Handler failed",
                error="Tool server unavailable",
                should_close=False,
            )
        )
        executor._handlers[TicketType.PASSWORD_RESET] = mock_handler

        await executor.initialize()
        await executor.process_ticket(sample_ticket)

        # Should add work note and escalate
        mock_connector.add_work_note.assert_called()
        calls = mock_connector.update_ticket.call_args_list
        assert any("escalat" in str(call).lower() for call in calls)

    @pytest.mark.asyncio
    async def test_claim_ticket(self, mock_config, mock_connector, sample_ticket):
        """Test ticket claiming."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        await executor._claim_ticket(sample_ticket)

        mock_connector.update_ticket.assert_called_once()
        call_args = mock_connector.update_ticket.call_args
        assert call_args[0][0] == sample_ticket.id
        update = call_args[0][1]
        assert update.state == TicketState.IN_PROGRESS
        assert update.assigned_to == mock_config.agent_user

    @pytest.mark.asyncio
    async def test_escalate_ticket(
        self, mock_config, mock_connector, sample_ticket, sample_classification_password_reset
    ):
        """Test ticket escalation."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        await executor._escalate_ticket(sample_ticket, sample_classification_password_reset, "Test reason")

        mock_connector.update_ticket.assert_called_once()
        call_args = mock_connector.update_ticket.call_args
        update = call_args[0][1]
        assert "escalat" in update.work_notes.lower()
        assert "test reason" in update.work_notes.lower()

    @pytest.mark.asyncio
    async def test_close_method(self, mock_config, mock_connector):
        """Test executor close method."""
        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        await executor.close()

        mock_connector.close.assert_called_once()

    def test_stop_method(self, mock_config):
        """Test executor stop method."""
        executor = TicketExecutor(mock_config)
        executor._running = True

        executor.stop()

        assert executor._running is False

    @pytest.mark.asyncio
    async def test_daemon_mode_start(self, mock_config, mock_connector):
        """Test daemon mode starts and can be stopped."""
        # Override poll interval for fast test execution
        mock_config.poll_interval_seconds = 0.01

        executor = TicketExecutor(mock_config)
        executor._connector = mock_connector

        with patch("agent.pipeline.executor.TicketClassifier"):
            await executor.initialize()

        # Start daemon in background
        daemon_task = asyncio.create_task(executor.run_daemon())

        # Wait a bit
        await asyncio.sleep(0.1)

        # Stop daemon
        executor.stop()

        # Wait for daemon to finish
        await asyncio.wait_for(daemon_task, timeout=1.0)

        # Should have called poll at least once
        assert mock_connector.poll_queue.call_count >= 1
