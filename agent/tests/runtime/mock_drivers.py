"""Mock LLM drivers for testing."""
from __future__ import annotations
import json
from typing import Any
from dataclasses import dataclass

from griptape.artifacts import TextArtifact


@dataclass
class MockClassificationResponse:
    """Configurable classification response."""
    ticket_type: str = "password_reset"
    confidence: float = 0.95
    affected_user: str = "jsmith"
    target_group: str | None = None
    target_resource: str | None = None
    reasoning: str = "Clear password reset request"


class MockTokenizer:
    """Mock tokenizer for Griptape compatibility."""
    def __init__(self):
        self.stop_sequences = []


class MockPromptDriver:
    """
    Mock LLM driver for testing workflows.

    Returns predictable responses based on configuration.
    """

    def __init__(
        self,
        classification: MockClassificationResponse | None = None,
        should_fail: bool = False,
        fail_message: str = "Mock LLM failure",
    ):
        self.classification = classification or MockClassificationResponse()
        self.should_fail = should_fail
        self.fail_message = fail_message
        self.call_history: list[str] = []
        self.tokenizer = MockTokenizer()
        self.model = "mock-model"
        self.temperature = 0.1
        self.use_native_tools = False
        self.max_tokens = None
        self.stream = False

    def run(self, prompt: str) -> Any:
        """Simulate LLM response."""
        self.call_history.append(prompt)

        if self.should_fail:
            raise RuntimeError(self.fail_message)

        # Return classification JSON
        response = {
            "ticket_type": self.classification.ticket_type,
            "confidence": self.classification.confidence,
            "affected_user": self.classification.affected_user,
            "target_group": self.classification.target_group,
            "target_resource": self.classification.target_resource,
            "reasoning": self.classification.reasoning,
        }

        return MockAgentResult(json.dumps(response, indent=2))


class MockAgentResult:
    """Mock Griptape agent result."""

    def __init__(self, text: str):
        self.output_task = MockOutputTask(text)
        self.text = text

    def to_artifact(self, **kwargs):
        """Convert to TextArtifact for Griptape compatibility."""
        return TextArtifact(self.text)


class MockOutputTask:
    """Mock output task."""

    def __init__(self, text: str):
        self.output = MockOutput(text)


class MockOutput:
    """Mock output with value."""

    def __init__(self, text: str):
        self.value = text


class MockToolServerClient:
    """
    Mock Tool Server client for testing.

    Returns predictable responses for tool operations.
    """

    def __init__(
        self,
        should_succeed: bool = True,
        response_data: dict[str, Any] | None = None,
    ):
        self.should_succeed = should_succeed
        self.response_data = response_data or {}
        self.call_history: list[dict[str, Any]] = []

    async def call(
        self,
        endpoint: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        """Simulate Tool Server call."""
        self.call_history.append({
            "endpoint": endpoint,
            "params": params,
        })

        if not self.should_succeed:
            return {
                "success": False,
                "message": "Mock tool server failure",
            }

        # Default success responses by endpoint
        if "password" in endpoint:
            return {
                "success": True,
                "message": f"Password reset for {params.get('username', 'user')}",
                "temp_password": "TempP@ss123!",
                **self.response_data,
            }
        elif "group" in endpoint:
            return {
                "success": True,
                "message": f"Group operation completed",
                **self.response_data,
            }
        else:
            return {
                "success": True,
                "message": "Operation completed",
                **self.response_data,
            }
