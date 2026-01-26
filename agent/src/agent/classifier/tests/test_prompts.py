"""Tests for prompt templates and builders."""

import json

import pytest

from agent.classifier.models import ClassificationResult
from agent.classifier.prompts import (
    FEW_SHOT_EXAMPLES,
    SYSTEM_PROMPT,
    build_classification_prompt,
    get_classification_schema,
)


class TestPrompts:
    """Test cases for prompt templates."""

    def test_system_prompt_exists(self) -> None:
        """Test that system prompt is defined and not empty."""
        assert SYSTEM_PROMPT
        assert len(SYSTEM_PROMPT) > 100
        assert "IT helpdesk ticket classifier" in SYSTEM_PROMPT

    def test_system_prompt_contains_ticket_types(self) -> None:
        """Test that system prompt lists all ticket types."""
        assert "password_reset" in SYSTEM_PROMPT
        assert "group_access_add" in SYSTEM_PROMPT
        assert "group_access_remove" in SYSTEM_PROMPT
        assert "file_permission" in SYSTEM_PROMPT
        assert "unknown" in SYSTEM_PROMPT

    def test_system_prompt_contains_instructions(self) -> None:
        """Test that system prompt includes key instructions."""
        assert "confidence" in SYSTEM_PROMPT.lower()
        assert "escalate" in SYSTEM_PROMPT.lower()
        assert "json" in SYSTEM_PROMPT.lower()

    def test_few_shot_examples_count(self) -> None:
        """Test that we have sufficient few-shot examples."""
        assert len(FEW_SHOT_EXAMPLES) >= 6  # Minimum requirement
        assert len(FEW_SHOT_EXAMPLES) <= 10  # Reasonable upper bound

    def test_few_shot_examples_structure(self) -> None:
        """Test that all examples have correct structure."""
        for i, example in enumerate(FEW_SHOT_EXAMPLES):
            assert "ticket" in example, f"Example {i} missing 'ticket' key"
            assert "classification" in example, f"Example {i} missing 'classification' key"

            ticket = example["ticket"]
            assert "number" in ticket
            assert "short_description" in ticket
            assert "description" in ticket
            assert "caller_username" in ticket

            classification = example["classification"]
            assert "ticket_type" in classification
            assert "confidence" in classification
            assert "reasoning" in classification

    def test_few_shot_examples_valid_json(self) -> None:
        """Test that all examples can be serialized to JSON."""
        for i, example in enumerate(FEW_SHOT_EXAMPLES):
            try:
                # Should not raise
                json.dumps(example)
            except Exception as e:
                pytest.fail(f"Example {i} is not valid JSON: {e}")

    def test_few_shot_examples_valid_classifications(self) -> None:
        """Test that all example classifications are valid ClassificationResult objects."""
        for i, example in enumerate(FEW_SHOT_EXAMPLES):
            try:
                # Should not raise ValidationError
                ClassificationResult(**example["classification"])
            except Exception as e:
                pytest.fail(
                    f"Example {i} classification is not valid ClassificationResult: {e}"
                )

    def test_few_shot_examples_cover_all_types(self) -> None:
        """Test that examples cover all ticket types."""
        types_covered = set()
        for example in FEW_SHOT_EXAMPLES:
            types_covered.add(example["classification"]["ticket_type"])

        # Should have at least one example of each type
        assert "password_reset" in types_covered
        assert "group_access_add" in types_covered
        assert "group_access_remove" in types_covered
        assert "file_permission" in types_covered
        assert "unknown" in types_covered

    def test_few_shot_examples_have_high_and_low_confidence(self) -> None:
        """Test that examples include both high and low confidence cases."""
        confidences = [
            example["classification"]["confidence"] for example in FEW_SHOT_EXAMPLES
        ]

        # Should have at least one high confidence (>= 0.8)
        assert any(c >= 0.8 for c in confidences), "No high confidence examples"

        # Should have at least one low confidence (< 0.6)
        assert any(c < 0.6 for c in confidences), "No low confidence examples"

    def test_few_shot_examples_have_escalation_cases(self) -> None:
        """Test that examples include cases requiring escalation."""
        escalations = [
            example["classification"]["should_escalate"] for example in FEW_SHOT_EXAMPLES
        ]

        # Should have at least one escalation case
        assert any(escalations), "No escalation examples"

        # Should have at least one non-escalation case
        assert not all(escalations), "All examples require escalation"

    def test_get_classification_schema(self) -> None:
        """Test that schema generation works."""
        schema = get_classification_schema()

        assert isinstance(schema, dict)
        assert "properties" in schema
        assert "ticket_type" in schema["properties"]
        assert "confidence" in schema["properties"]
        assert "reasoning" in schema["properties"]

    def test_build_classification_prompt_returns_string(self) -> None:
        """Test that prompt builder returns a string."""
        ticket_dict = {
            "number": "INC0010001",
            "short_description": "Test ticket",
            "description": "This is a test",
            "caller_username": "testuser",
            "priority": 3,
        }

        prompt = build_classification_prompt(ticket_dict)

        assert isinstance(prompt, str)
        assert len(prompt) > 0

    def test_build_classification_prompt_includes_system_prompt(self) -> None:
        """Test that generated prompt includes system instructions."""
        ticket_dict = {
            "number": "INC0010001",
            "short_description": "Test ticket",
            "description": "This is a test",
            "caller_username": "testuser",
        }

        prompt = build_classification_prompt(ticket_dict)

        # Should include key parts of system prompt
        assert "IT helpdesk ticket classifier" in prompt
        assert "password_reset" in prompt
        assert "JSON" in prompt or "json" in prompt

    def test_build_classification_prompt_includes_examples(self) -> None:
        """Test that generated prompt includes few-shot examples."""
        ticket_dict = {
            "number": "INC0010001",
            "short_description": "Test ticket",
            "description": "This is a test",
            "caller_username": "testuser",
        }

        prompt = build_classification_prompt(ticket_dict)

        # Should include at least some example content
        assert "Example" in prompt or "example" in prompt
        # Should include at least one example ticket number
        example_numbers = [ex["ticket"]["number"] for ex in FEW_SHOT_EXAMPLES]
        assert any(num in prompt for num in example_numbers)

    def test_build_classification_prompt_includes_ticket(self) -> None:
        """Test that generated prompt includes the ticket to classify."""
        ticket_dict = {
            "number": "INC9999999",
            "short_description": "Unique test ticket",
            "description": "This is a very unique test ticket description",
            "caller_username": "unique_user",
            "priority": 1,
        }

        prompt = build_classification_prompt(ticket_dict)

        # Should include the ticket data
        assert ticket_dict["number"] in prompt
        assert ticket_dict["short_description"] in prompt
        assert ticket_dict["description"] in prompt
        assert ticket_dict["caller_username"] in prompt

    def test_build_classification_prompt_includes_schema(self) -> None:
        """Test that generated prompt includes the classification schema."""
        ticket_dict = {
            "number": "INC0010001",
            "short_description": "Test ticket",
            "description": "This is a test",
            "caller_username": "testuser",
        }

        prompt = build_classification_prompt(ticket_dict)

        # Should include schema with key fields
        assert "ticket_type" in prompt
        assert "confidence" in prompt
        assert "affected_user" in prompt
        assert "should_escalate" in prompt
