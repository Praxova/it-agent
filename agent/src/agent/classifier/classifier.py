"""Ticket classifier using LLM with few-shot prompting."""

import json
import logging
import re
from typing import Any

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
        model: str = "llama3.1",
        base_url: str = "http://localhost:11434",
        temperature: float = 0.1,  # Low temp for consistency
    ) -> None:
        """Initialize ticket classifier.

        Args:
            model: Ollama model name (default: llama3.1).
            base_url: Ollama server URL (default: http://localhost:11434).
            temperature: LLM temperature for sampling (default: 0.1 for consistency).
        """
        self.driver = OllamaPromptDriver(
            model=model,
            host=base_url,
            options={"temperature": temperature},
        )
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

        Args:
            response: Raw LLM response text.

        Returns:
            Parsed ClassificationResult.

        Raises:
            ValueError: If unable to extract or parse JSON.
        """
        # Try to extract JSON from markdown code block (greedy match for nested braces)
        json_match = re.search(r"```(?:json)?\s*(\{.*\})\s*```", response, re.DOTALL)

        if json_match:
            json_str = json_match.group(1)
        else:
            # Try to find JSON object directly (greedy match for nested braces)
            json_match = re.search(r"(\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\})", response, re.DOTALL)
            if json_match:
                json_str = json_match.group(1)
            else:
                raise ValueError(f"No JSON found in response: {response[:200]}")

        try:
            # Parse JSON
            data = json.loads(json_str)

            # Validate and create ClassificationResult
            return ClassificationResult(**data)

        except json.JSONDecodeError as e:
            raise ValueError(f"Invalid JSON in response: {e}")
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
