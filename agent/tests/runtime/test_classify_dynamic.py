"""Tests for dynamic classification prompt building."""
import json
import pytest
from agent.runtime.executors.classify import ClassifyExecutor
from agent.runtime.models import ExampleSetExportInfo, ExampleExportInfo


@pytest.fixture
def executor():
    return ClassifyExecutor()


@pytest.fixture
def dispatch_example_set():
    """Multi-type example set matching the it-dispatch-classification seeder."""
    return ExampleSetExportInfo(
        name="it-dispatch-classification",
        display_name="IT Dispatch Classification",
        examples=[
            ExampleExportInfo(
                input_text="User John Smith needs his password reset.",
                expected_output_json=json.dumps({
                    "ticket_type": "password-reset",
                    "confidence": 0.95,
                    "affected_user": "jsmith",
                }),
            ),
            ExampleExportInfo(
                input_text="Please add jane.doe to Finance-Reports group.",
                expected_output_json=json.dumps({
                    "ticket_type": "group-membership",
                    "confidence": 0.92,
                    "affected_user": "jane.doe",
                    "target_group": "Finance-Reports",
                }),
            ),
            ExampleExportInfo(
                input_text="Sarah needs read access to \\\\fileserver\\shared\\marketing.",
                expected_output_json=json.dumps({
                    "ticket_type": "file-permissions",
                    "confidence": 0.88,
                    "affected_user": "sconnor",
                }),
            ),
            ExampleExportInfo(
                input_text="The printer on the 3rd floor is jammed again.",
                expected_output_json=json.dumps({
                    "ticket_type": "unknown",
                    "confidence": 0.15,
                    "escalation_reason": "Hardware issue",
                }),
            ),
        ],
    )


class TestExtractCategories:
    def test_extracts_all_types(self, executor, dispatch_example_set):
        """Categories should be derived from examples' expected outputs."""
        categories = executor._extract_categories(dispatch_example_set)
        assert "password-reset" in categories
        assert "group-membership" in categories
        assert "file-permissions" in categories
        assert "unknown" in categories

    def test_empty_set_returns_unknown(self, executor):
        """Empty example set should return just 'unknown'."""
        example_set = ExampleSetExportInfo(name="empty-set", examples=[])
        categories = executor._extract_categories(example_set)
        assert categories == ["unknown"]

    def test_always_includes_unknown(self, executor):
        """Even if no example has 'unknown', it's always present."""
        example_set = ExampleSetExportInfo(
            name="single-type",
            examples=[
                ExampleExportInfo(
                    input_text="Reset pw",
                    expected_output_json=json.dumps({"ticket_type": "password-reset"}),
                ),
            ],
        )
        categories = executor._extract_categories(example_set)
        assert "unknown" in categories
        assert "password-reset" in categories

    def test_deduplicates(self, executor):
        """Duplicate ticket types should be deduplicated."""
        example_set = ExampleSetExportInfo(
            name="dupes",
            examples=[
                ExampleExportInfo(
                    input_text="reset 1",
                    expected_output_json=json.dumps({"ticket_type": "password-reset"}),
                ),
                ExampleExportInfo(
                    input_text="reset 2",
                    expected_output_json=json.dumps({"ticket_type": "password-reset"}),
                ),
            ],
        )
        categories = executor._extract_categories(example_set)
        assert categories.count("password-reset") == 1

    def test_handles_bad_json(self, executor):
        """Malformed JSON in expected output should be skipped."""
        example_set = ExampleSetExportInfo(
            name="bad-json",
            examples=[
                ExampleExportInfo(
                    input_text="test",
                    expected_output_json="not valid json {{{",
                ),
            ],
        )
        categories = executor._extract_categories(example_set)
        assert categories == ["unknown"]


class TestBuildSystemInstruction:
    def test_includes_all_categories(self, executor):
        """System instruction should list all categories."""
        instruction = executor._build_system_instruction(
            ["file-permissions", "group-membership", "password-reset", "unknown"]
        )
        assert '"password-reset"' in instruction
        assert '"group-membership"' in instruction
        assert '"file-permissions"' in instruction
        assert '"unknown"' in instruction

    def test_includes_json_format_instruction(self, executor):
        """Should instruct the LLM to respond with JSON."""
        instruction = executor._build_system_instruction(["unknown"])
        assert "valid JSON" in instruction
        assert "ticket_type" in instruction
        assert "confidence" in instruction


class TestBuildFewShotSection:
    def test_includes_examples(self, executor, dispatch_example_set):
        """Few-shot section should include all examples with proper formatting."""
        section = executor._build_few_shot_section(dispatch_example_set)
        assert "Example 1:" in section
        assert "Example 2:" in section
        assert "John Smith" in section
        assert "password-reset" in section
        assert "group-membership" in section

    def test_empty_set_returns_empty(self, executor):
        """Empty example set should return empty string."""
        example_set = ExampleSetExportInfo(name="empty", examples=[])
        section = executor._build_few_shot_section(example_set)
        assert section == ""

    def test_skips_examples_without_input(self, executor):
        """Examples with no input_text should be skipped."""
        example_set = ExampleSetExportInfo(
            name="missing-input",
            examples=[
                ExampleExportInfo(
                    input_text="",
                    expected_output_json=json.dumps({"ticket_type": "unknown"}),
                ),
            ],
        )
        section = executor._build_few_shot_section(example_set)
        assert "Example 1:" not in section


class TestBuildTicketSection:
    def test_includes_ticket_fields(self, executor):
        """Ticket section should include resolved caller name and username."""
        ticket_data = {
            "short_description": "Password reset needed",
            "description": "User jsmith forgot password",
            "caller_name": "John Smith",
            "caller_username": "jsmith",
        }
        section = executor._build_ticket_section(ticket_data)
        assert "Password reset needed" in section
        assert "User jsmith forgot password" in section
        assert "John Smith" in section
        assert "jsmith" in section

    def test_caller_display_username_only(self, executor):
        """If only username is resolved, display it alone."""
        ticket_data = {
            "short_description": "VPN issue",
            "description": "Can't connect",
            "caller_username": "twilson",
        }
        section = executor._build_ticket_section(ticket_data)
        assert "twilson" in section

    def test_caller_display_name_only(self, executor):
        """If only display name is resolved, display it alone."""
        ticket_data = {
            "short_description": "VPN issue",
            "description": "Can't connect",
            "caller_name": "Tom Wilson",
        }
        section = executor._build_ticket_section(ticket_data)
        assert "Tom Wilson" in section

    def test_handles_missing_fields(self, executor):
        """Missing fields should default gracefully."""
        section = executor._build_ticket_section({})
        assert "Unknown" in section  # caller defaults to "Unknown"


class TestParseClassificationResponse:
    def test_no_type_map(self, executor):
        """Parsed response should NOT translate ticket_type — no _TYPE_MAP."""
        response = json.dumps({
            "ticket_type": "password-reset",
            "confidence": 0.92,
            "affected_user": "jsmith",
        })
        result = executor._parse_classification_response(response)
        assert result["ticket_type"] == "password-reset"
        assert result["confidence"] == 0.92

    def test_passes_through_kebab_case(self, executor):
        """Kebab-case ticket types should pass through unchanged."""
        for ticket_type in ["password-reset", "group-membership", "file-permissions", "unknown"]:
            response = json.dumps({"ticket_type": ticket_type, "confidence": 0.9})
            result = executor._parse_classification_response(response)
            assert result["ticket_type"] == ticket_type

    def test_handles_markdown_wrapped_json(self, executor):
        """Should extract JSON from markdown code blocks."""
        response = """Here is my classification:
```json
{"ticket_type": "password-reset", "confidence": 0.95}
```"""
        result = executor._parse_classification_response(response)
        assert result["ticket_type"] == "password-reset"
        assert result["confidence"] == 0.95

    def test_defaults_for_missing_fields(self, executor):
        """Missing fields should get sensible defaults."""
        response = json.dumps({"ticket_type": "password-reset"})
        result = executor._parse_classification_response(response)
        assert result["confidence"] == 0.5  # default
        assert result["affected_user"] is None
        assert result["reasoning"] == ""

    def test_unparseable_returns_unknown(self, executor):
        """Completely unparseable response should return unknown."""
        result = executor._parse_classification_response("I don't know how to classify this.")
        assert result["ticket_type"] == "unknown"
        assert result["confidence"] == 0.3


class TestValidateClassification:
    """Tests for post-classification category validation (Gap 2)."""

    def test_invalid_category_becomes_unknown(self, executor, dispatch_example_set):
        """LLM returns a category not in the example set → unknown with confidence 0."""
        classification = {
            "ticket_type": "printer-repair",
            "confidence": 0.85,
            "reasoning": "Looks like a printer issue",
        }
        result = executor._validate_classification(classification, dispatch_example_set)
        assert result["ticket_type"] == "unknown"
        assert result["confidence"] == 0.0
        assert "printer-repair" in result["reasoning"]

    def test_valid_category_passes_through(self, executor, dispatch_example_set):
        """Known category should pass through unmodified."""
        classification = {
            "ticket_type": "password-reset",
            "confidence": 0.92,
            "reasoning": "User requests password reset",
        }
        result = executor._validate_classification(classification, dispatch_example_set)
        assert result["ticket_type"] == "password-reset"
        assert result["confidence"] == 0.92

    def test_unknown_category_passes_through(self, executor, dispatch_example_set):
        """'unknown' is always a valid category."""
        classification = {
            "ticket_type": "unknown",
            "confidence": 0.3,
            "reasoning": "Not sure",
        }
        result = executor._validate_classification(classification, dispatch_example_set)
        assert result["ticket_type"] == "unknown"
        assert result["confidence"] == 0.3

    def test_no_example_set_skips_validation(self, executor):
        """When example_set is None, classification passes through as-is."""
        classification = {
            "ticket_type": "anything-goes",
            "confidence": 0.9,
        }
        result = executor._validate_classification(classification, None)
        assert result["ticket_type"] == "anything-goes"
        assert result["confidence"] == 0.9

    def test_missing_ticket_type_treated_as_unknown(self, executor, dispatch_example_set):
        """Missing ticket_type key defaults to 'unknown' which is always valid."""
        classification = {"confidence": 0.5}
        result = executor._validate_classification(classification, dispatch_example_set)
        # "unknown" is in valid categories, so it passes through
        assert result.get("ticket_type", "unknown") == "unknown"
