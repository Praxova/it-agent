"""Ticket classifier using LLM with few-shot prompting."""

import json
import logging
import re
from typing import Any

from griptape.drivers.prompt import BasePromptDriver
from griptape.drivers.prompt.ollama import OllamaPromptDriver
from griptape.structures import Agent

from connectors import Ticket

from .models import ClassificationResult, TicketType
from .prompts import build_classification_prompt

logger = logging.getLogger(__name__)


class TicketClassifier:
    """Classifies IT tickets using LLM with few-shot prompting.

    Uses Griptape Agent with Ollama to classify tickets into predefined
    categories and extract relevant entities.

    Attributes:
        driver: Griptape prompt driver for LLM communication.
        agent: Griptape Agent for running prompts.
    """

    def __init__(
        self,
        driver: BasePromptDriver | None = None,
        model: str = "llama3.1",
        base_url: str = "http://localhost:11434",
        temperature: float = 0.1,  # Low temp for consistency
    ) -> None:
        """Initialize ticket classifier.

        Supports two modes:
        1. Admin Portal mode: Pass a pre-configured PromptDriver (from DriverFactory)
        2. Legacy mode: Pass model/base_url to create an OllamaPromptDriver

        Args:
            driver: (optional) Pre-configured PromptDriver from Admin Portal.
            model: (legacy) Ollama model name (default: llama3.1).
            base_url: (legacy) Ollama server URL (default: http://localhost:11434).
            temperature: (legacy) LLM temperature for sampling (default: 0.1).
        """
        if driver:
            # Admin Portal mode: use provided driver
            self.driver = driver
            logger.info("TicketClassifier initialized with custom PromptDriver")
        else:
            # Legacy mode: create Ollama driver from parameters
            self.driver = OllamaPromptDriver(
                model=model,
                host=base_url,
                options={"temperature": temperature},
            )
            logger.info(f"TicketClassifier initialized with Ollama driver: {model}")

        self.agent = Agent(prompt_driver=self.driver)

    def classify(self, ticket: Ticket) -> ClassificationResult:
        """Classify a ticket and extract relevant entities.

        Args:
            ticket: The ticket to classify (from ServiceNow connector).

        Returns:
            ClassificationResult with type, confidence, and extracted entities.

        Raises:
            ValueError: If unable to parse LLM response.
        """
        prompt = build_classification_prompt(ticket.model_dump())

        logger.info(f"Classifying ticket {ticket.number}")
        logger.debug(f"Prompt length: {len(prompt)} characters")

        try:
            result = self.agent.run(prompt)
            response_text = result.output_task.output.value

            logger.debug(f"LLM response: {response_text[:200]}...")

            # Parse JSON from response
            classification = self._parse_response(response_text)

            logger.info(
                f"Ticket {ticket.number} classified as {classification.ticket_type} "
                f"(confidence: {classification.confidence:.2f}, "
                f"action: {classification.action_recommended})"
            )

            return classification

        except Exception as e:
            logger.error(f"Failed to classify ticket {ticket.number}: {e}")
            # Return safe fallback: unknown with low confidence, escalate
            return ClassificationResult(
                ticket_type=TicketType.UNKNOWN,
                confidence=0.0,
                reasoning=f"Classification failed due to error: {str(e)}",
                affected_user=ticket.caller_username,
                should_escalate=True,
                escalation_reason=f"Classifier error: {str(e)}",
            )

    def _parse_response(self, response: str) -> ClassificationResult:
        """Parse LLM response into ClassificationResult.

        Handles potential markdown code blocks and extracts JSON.
        Also handles responses that include JSON schema definitions ($defs).

        Args:
            response: Raw LLM response text.

        Returns:
            Parsed ClassificationResult.

        Raises:
            ValueError: If unable to extract or parse JSON.
        """
        # Try to extract JSON from markdown code blocks
        json_match = re.search(r'```(?:json)?\s*([\s\S]*?)```', response)
        if json_match:
            json_str = json_match.group(1).strip()
        else:
            # Try to find raw JSON object
            json_match = re.search(r'\{[\s\S]*\}', response)
            if json_match:
                json_str = json_match.group(0)
            else:
                raise ValueError(f"No JSON found in response: {response[:200]}")

        # Parse the JSON
        try:
            data = json.loads(json_str)
        except json.JSONDecodeError as e:
            # If parsing the full JSON fails, try to find just the result object
            # Look for an object with the actual classification fields
            result_pattern = r'\{\s*"ticket_type"\s*:\s*"[^"]+"\s*,[\s\S]*?"confidence"\s*:\s*[\d.]+[\s\S]*?\}'
            result_match = re.search(result_pattern, json_str)
            if result_match:
                try:
                    data = json.loads(result_match.group(0))
                except json.JSONDecodeError:
                    raise ValueError(f"Invalid JSON in response: {e}")
            else:
                raise ValueError(f"Invalid JSON in response: {e}")

        # The result fields we're looking for
        result_field_names = [
            "ticket_type", "confidence", "reasoning", "affected_user",
            "target_group", "target_resource", "should_escalate", "escalation_reason"
        ]

        # If data contains $defs (schema definition), extract only the result fields
        if "$defs" in data:
            # The response included schema - extract only the actual result fields
            result_fields = {k: v for k, v in data.items() if k in result_field_names}
            if result_fields.get("ticket_type"):
                data = result_fields
            else:
                # Maybe the classification is nested somewhere - search the whole structure
                # Look for a dict that has ticket_type and confidence
                def find_classification(obj):
                    if isinstance(obj, dict):
                        if "ticket_type" in obj and "confidence" in obj:
                            return {k: v for k, v in obj.items() if k in result_field_names}
                        for value in obj.values():
                            result = find_classification(value)
                            if result:
                                return result
                    elif isinstance(obj, list):
                        for item in obj:
                            result = find_classification(item)
                            if result:
                                return result
                    return None

                found = find_classification(data)
                if found:
                    data = found
                else:
                    raise ValueError("Could not extract classification from schema-laden response")

        # Also handle case where there's a "description" field (from schema) but the actual values are also present
        elif "description" in data and "properties" in data:
            # This looks like a JSON Schema, not actual data
            raise ValueError("Response contains JSON Schema, not actual classification data")

        # Validate and create ClassificationResult
        try:
            return ClassificationResult(**data)
        except Exception as e:
            raise ValueError(f"Failed to create ClassificationResult: {e}")

    def classify_batch(self, tickets: list[Ticket]) -> list[ClassificationResult]:
        """Classify multiple tickets.

        Currently sequential - could be optimized with batching or parallelization.

        Args:
            tickets: List of tickets to classify.

        Returns:
            List of classification results (same order as input).
        """
        logger.info(f"Classifying batch of {len(tickets)} tickets")

        results = []
        for i, ticket in enumerate(tickets, 1):
            logger.debug(f"Processing ticket {i}/{len(tickets)}: {ticket.number}")
            result = self.classify(ticket)
            results.append(result)

        # Log summary
        by_type = {}
        for result in results:
            by_type[result.ticket_type] = by_type.get(result.ticket_type, 0) + 1

        logger.info(f"Batch classification complete: {dict(by_type)}")

        return results
