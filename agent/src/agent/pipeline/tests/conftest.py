"""Test fixtures for pipeline tests."""

from datetime import datetime
from unittest.mock import AsyncMock, Mock

import pytest

from connectors import Ticket, TicketState


from agent.pipeline.config import PipelineConfig


@pytest.fixture
def mock_config():
    """Create a mock pipeline configuration."""
    return PipelineConfig(
        servicenow_instance="test.service-now.com",
        servicenow_username="test",
        servicenow_password="test",
        assignment_group="Test Group",
        agent_user="Test Agent",
        tool_server_url="http://localhost:8100",
        ollama_model="llama3.1",
        ollama_base_url="http://localhost:11434",
        confidence_threshold_auto=0.8,
        confidence_threshold_review=0.6,
        poll_interval_seconds=30,
        escalation_group="Test Escalation",
    )


@pytest.fixture
def sample_ticket():
    """Create a sample ticket for testing."""
    return Ticket(
        id="abc123",
        number="INC0012345",
        short_description="Password reset needed",
        description="I forgot my password for user luke.skywalker",
        state=TicketState.NEW,
        priority=3,
        caller_username="user123",
        assignment_group="Helpdesk",
        created_at=datetime.now(),
        updated_at=datetime.now(),
    )


@pytest.fixture
def sample_classification_password_reset():
    """Create a sample classification result for password reset."""
    return ClassificationResult(
        ticket_type=TicketType.PASSWORD_RESET,
        confidence=0.9,
        affected_user="luke.skywalker",
        target_group=None,
        action_recommended="AUTO_RESOLVE",
        reasoning="User explicitly requests password reset for luke.skywalker",
        should_escalate=False,
        escalation_reason=None,
    )


@pytest.fixture
def sample_classification_group_add():
    """Create a sample classification result for group access add."""
    return ClassificationResult(
        ticket_type=TicketType.GROUP_ACCESS_ADD,
        confidence=0.85,
        affected_user="han.solo",
        target_group="Contributors",
        action_recommended="AUTO_RESOLVE",
        reasoning="User requests access to Contributors group for han.solo",
        should_escalate=False,
        escalation_reason=None,
    )


@pytest.fixture
def sample_classification_low_confidence():
    """Create a sample classification result with low confidence."""
    return ClassificationResult(
        ticket_type=TicketType.PASSWORD_RESET,
        confidence=0.5,
        affected_user="test.user",
        target_group=None,
        action_recommended="REVIEW",
        reasoning="Unclear if this is a password reset request",
        should_escalate=False,
        escalation_reason=None,
    )


@pytest.fixture
def sample_classification_unknown():
    """Create a sample classification result for unknown ticket type."""
    return ClassificationResult(
        ticket_type=TicketType.UNKNOWN,
        confidence=0.3,
        affected_user=None,
        target_group=None,
        action_recommended="ESCALATE",
        reasoning="Unable to classify ticket type",
        should_escalate=True,
        escalation_reason="Ticket type not recognized",
    )


@pytest.fixture
def mock_connector():
    """Create a mock ServiceNow connector."""
    connector = Mock()
    connector.poll_queue = AsyncMock(return_value=[])
    connector.update_ticket = AsyncMock()
    connector.add_work_note = AsyncMock()
    connector.add_comment = AsyncMock()
    connector.close_ticket = AsyncMock()
    connector.close = AsyncMock()
    return connector


@pytest.fixture
def mock_classifier():
    """Create a mock ticket classifier."""
    classifier = Mock()
    classifier.classify = Mock()
    return classifier


@pytest.fixture
def mock_handler():
    """Create a mock ticket handler."""
    handler = Mock()
    handler.handles_ticket_types = [TicketType.PASSWORD_RESET]
    handler.validate = AsyncMock(return_value=(True, None))
    handler.handle = AsyncMock()
    return handler
