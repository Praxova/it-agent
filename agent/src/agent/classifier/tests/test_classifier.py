"""Tests for ticket classifier."""

import json
from unittest.mock import MagicMock, Mock, patch

import pytest

from agent.classifier.classifier import TicketClassifier
from agent.classifier.models import ClassificationResult, TicketType
from agent.classifier.tests.fixtures.sample_tickets import (
    get_escalation_tickets,
    get_high_confidence_tickets,
    get_sample_tickets,
    get_tickets_by_type,
)


class TestClassificationResult:
    """Test cases for ClassificationResult model."""

    def test_action_recommended_high_confidence(self) -> None:
        """Test action_recommended for high confidence (>= 0.8)."""
        result = ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.95,
            reasoning="Clear password reset request",
        )

        assert result.action_recommended == "proceed"

    def test_action_recommended_medium_confidence(self) -> None:
        """Test action_recommended for medium confidence (>= 0.6, < 0.8)."""
        result = ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.7,
            reasoning="Password reset with some ambiguity",
        )

        assert result.action_recommended == "proceed_with_review"

    def test_action_recommended_low_confidence(self) -> None:
        """Test action_recommended for low confidence (< 0.6)."""
        result = ClassificationResult(
            ticket_type=TicketType.UNKNOWN,
            confidence=0.4,
            reasoning="Unclear request",
        )

        assert result.action_recommended == "escalate"

    def test_action_recommended_boundary_high(self) -> None:
        """Test boundary at 0.8."""
        result = ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.8,
            reasoning="Test",
        )

        assert result.action_recommended == "proceed"

    def test_action_recommended_boundary_medium(self) -> None:
        """Test boundary at 0.6."""
        result = ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.6,
            reasoning="Test",
        )

        assert result.action_recommended == "proceed_with_review"


class TestTicketClassifier:
    """Test cases for TicketClassifier."""

    @pytest.fixture
    def mock_agent(self) -> Mock:
        """Create a mock Griptape Agent."""
        mock_agent = Mock()

        # Mock the output structure
        mock_output = Mock()
        mock_output.value = json.dumps(
            {
                "ticket_type": "password_reset",
                "confidence": 0.92,
                "reasoning": "Clear password reset request",
                "affected_user": "testuser",
                "target_group": None,
                "target_resource": None,
                "should_escalate": False,
                "escalation_reason": None,
            }
        )

        mock_task = Mock()
        mock_task.output = mock_output

        mock_result = Mock()
        mock_result.output_task = mock_task

        mock_agent.run.return_value = mock_result

        return mock_agent

    @pytest.fixture
    def classifier(self, mock_agent: Mock) -> TicketClassifier:
        """Create TicketClassifier with mocked agent."""
        with patch("agent.classifier.classifier.Agent") as MockAgent:
            MockAgent.return_value = mock_agent
            classifier = TicketClassifier()
            classifier.agent = mock_agent
            return classifier

    def test_classifier_initialization(self) -> None:
        """Test that classifier initializes with correct defaults."""
        with patch("agent.classifier.classifier.Agent"):
            with patch("agent.classifier.classifier.OllamaPromptDriver") as MockDriver:
                classifier = TicketClassifier()

                # Should create driver with defaults
                MockDriver.assert_called_once()
                call_kwargs = MockDriver.call_args.kwargs
                assert call_kwargs.get("model") == "llama3.1"
                assert call_kwargs.get("host") == "http://localhost:11434"

    def test_classifier_custom_params(self) -> None:
        """Test classifier initialization with custom parameters."""
        with patch("agent.classifier.classifier.Agent"):
            with patch("agent.classifier.classifier.OllamaPromptDriver") as MockDriver:
                classifier = TicketClassifier(
                    model="custom-model",
                    base_url="http://custom:1234",
                    temperature=0.5,
                )

                call_kwargs = MockDriver.call_args.kwargs
                assert call_kwargs.get("model") == "custom-model"
                assert call_kwargs.get("host") == "http://custom:1234"
                assert call_kwargs.get("options", {}).get("temperature") == 0.5

    def test_parse_response_valid_json(self, classifier: TicketClassifier) -> None:
        """Test parsing valid JSON response."""
        response = """
        {
            "ticket_type": "password_reset",
            "confidence": 0.92,
            "reasoning": "Test",
            "affected_user": "testuser",
            "target_group": null,
            "target_resource": null,
            "should_escalate": false,
            "escalation_reason": null
        }
        """

        result = classifier._parse_response(response)

        assert isinstance(result, ClassificationResult)
        assert result.ticket_type == TicketType.PASSWORD_RESET
        assert result.confidence == 0.92
        assert result.affected_user == "testuser"

    def test_parse_response_json_in_code_block(
        self, classifier: TicketClassifier
    ) -> None:
        """Test parsing JSON wrapped in markdown code block."""
        response = """
        Here's the classification:

        ```json
        {
            "ticket_type": "group_access_add",
            "confidence": 0.88,
            "reasoning": "Test",
            "affected_user": "testuser",
            "target_group": "TestGroup",
            "target_resource": null,
            "should_escalate": false,
            "escalation_reason": null
        }
        ```
        """

        result = classifier._parse_response(response)

        assert isinstance(result, ClassificationResult)
        assert result.ticket_type == TicketType.GROUP_ACCESS_ADD
        assert result.target_group == "TestGroup"

    def test_parse_response_json_without_language_marker(
        self, classifier: TicketClassifier
    ) -> None:
        """Test parsing JSON in code block without 'json' marker."""
        response = """
        ```
        {
            "ticket_type": "file_permission",
            "confidence": 0.85,
            "reasoning": "Test",
            "affected_user": "testuser",
            "target_group": null,
            "target_resource": "/path/to/share",
            "should_escalate": false,
            "escalation_reason": null
        }
        ```
        """

        result = classifier._parse_response(response)

        assert isinstance(result, ClassificationResult)
        assert result.ticket_type == TicketType.FILE_PERMISSION
        assert result.target_resource == "/path/to/share"

    def test_parse_response_invalid_json(self, classifier: TicketClassifier) -> None:
        """Test that invalid JSON raises ValueError."""
        response = "This is not JSON at all"

        with pytest.raises(ValueError, match="No JSON found"):
            classifier._parse_response(response)

    def test_parse_response_malformed_json(self, classifier: TicketClassifier) -> None:
        """Test that malformed JSON raises ValueError."""
        response = """
        {
            "ticket_type": "password_reset",
            "confidence": 0.92,
            // Missing closing brace
        """

        with pytest.raises(ValueError):
            classifier._parse_response(response)

    def test_parse_response_missing_required_fields(
        self, classifier: TicketClassifier
    ) -> None:
        """Test that JSON missing required fields raises ValueError."""
        response = """
        {
            "ticket_type": "password_reset"
        }
        """

        with pytest.raises(ValueError, match="Failed to create ClassificationResult"):
            classifier._parse_response(response)

    def test_classify_returns_classification_result(
        self, classifier: TicketClassifier
    ) -> None:
        """Test that classify returns ClassificationResult."""
        sample = get_sample_tickets()[0]
        ticket = sample["ticket"]

        result = classifier.classify(ticket)

        assert isinstance(result, ClassificationResult)
        assert result.ticket_type in TicketType
        assert 0.0 <= result.confidence <= 1.0

    def test_classify_calls_agent_run(
        self, classifier: TicketClassifier, mock_agent: Mock
    ) -> None:
        """Test that classify calls agent.run with prompt."""
        sample = get_sample_tickets()[0]
        ticket = sample["ticket"]

        classifier.classify(ticket)

        # Should call agent.run once
        mock_agent.run.assert_called_once()

        # Prompt should contain ticket information
        call_args = mock_agent.run.call_args
        prompt = call_args[0][0]
        assert ticket.number in prompt
        assert ticket.short_description in prompt

    def test_classify_handles_agent_error(self, classifier: TicketClassifier) -> None:
        """Test that classify handles agent errors gracefully."""
        sample = get_sample_tickets()[0]
        ticket = sample["ticket"]

        # Make agent.run raise an exception
        classifier.agent.run.side_effect = Exception("LLM error")

        result = classifier.classify(ticket)

        # Should return safe fallback
        assert result.ticket_type == TicketType.UNKNOWN
        assert result.confidence == 0.0
        assert result.should_escalate is True
        assert "error" in result.escalation_reason.lower()

    def test_classify_batch_processes_all_tickets(
        self, classifier: TicketClassifier
    ) -> None:
        """Test that classify_batch processes all tickets."""
        samples = get_sample_tickets()[:3]  # First 3 tickets
        tickets = [s["ticket"] for s in samples]

        results = classifier.classify_batch(tickets)

        assert len(results) == len(tickets)
        assert all(isinstance(r, ClassificationResult) for r in results)

    def test_classify_batch_calls_classify_for_each(
        self, classifier: TicketClassifier
    ) -> None:
        """Test that classify_batch calls classify for each ticket."""
        samples = get_sample_tickets()[:2]
        tickets = [s["ticket"] for s in samples]

        with patch.object(classifier, "classify") as mock_classify:
            mock_classify.return_value = ClassificationResult(
                ticket_type=TicketType.PASSWORD_RESET,
                confidence=0.9,
                reasoning="Test",
            )

            classifier.classify_batch(tickets)

            assert mock_classify.call_count == len(tickets)


class TestSampleTickets:
    """Test cases for sample ticket fixtures."""

    def test_sample_tickets_exist(self) -> None:
        """Test that sample tickets are available."""
        samples = get_sample_tickets()

        assert len(samples) >= 10  # Should have at least 10 samples

    def test_sample_tickets_structure(self) -> None:
        """Test that all sample tickets have correct structure."""
        samples = get_sample_tickets()

        for sample in samples:
            assert "ticket" in sample
            assert "expected" in sample
            assert isinstance(sample["expected"], ClassificationResult)

    def test_get_tickets_by_type(self) -> None:
        """Test filtering tickets by type."""
        password_tickets = get_tickets_by_type(TicketType.PASSWORD_RESET)

        assert len(password_tickets) > 0
        for sample in password_tickets:
            assert sample["expected"].ticket_type == TicketType.PASSWORD_RESET

    def test_get_high_confidence_tickets(self) -> None:
        """Test filtering high confidence tickets."""
        high_conf = get_high_confidence_tickets()

        assert len(high_conf) > 0
        for sample in high_conf:
            assert sample["expected"].confidence >= 0.8

    def test_get_escalation_tickets(self) -> None:
        """Test filtering escalation tickets."""
        escalations = get_escalation_tickets()

        assert len(escalations) > 0
        for sample in escalations:
            assert sample["expected"].should_escalate is True

    def test_sample_tickets_cover_all_types(self) -> None:
        """Test that samples cover all ticket types."""
        samples = get_sample_tickets()
        types_found = set()

        for sample in samples:
            types_found.add(sample["expected"].ticket_type)

        # Should have examples of each type
        assert TicketType.PASSWORD_RESET in types_found
        assert TicketType.GROUP_ACCESS_ADD in types_found
        assert TicketType.GROUP_ACCESS_REMOVE in types_found
        assert TicketType.FILE_PERMISSION in types_found
        assert TicketType.UNKNOWN in types_found
