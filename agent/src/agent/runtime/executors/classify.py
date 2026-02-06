"""Classify step executor - LLM-powered ticket classification."""
from __future__ import annotations
import json
import logging
import re
from typing import Any

from griptape.structures import Agent
from griptape.rules import Rule, Ruleset

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType, ExampleSetExportInfo
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class ClassifyExecutor(BaseStepExecutor):
    """
    Executes Classify steps - uses LLM to classify tickets.

    Builds prompts with:
    - Rulesets (classification rules, security rules)
    - Few-shot examples from example sets
    - Ticket data to classify
    """

    @property
    def step_type(self) -> str:
        return StepType.CLASSIFY.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute classification step.

        Configuration options:
        - use_example_set: Name of example set to use for few-shot
        - output_format: Expected output fields
        - max_retries: Number of retries on parse failure
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        if not context.llm_driver:
            result.fail("No LLM driver configured")
            return result

        config = step.configuration or {}

        # Get applicable rulesets
        step_rulesets = self.get_step_rulesets(step, rulesets)

        # Build the classification prompt
        prompt = self._build_classification_prompt(
            ticket_data=context.ticket_data,
            rulesets=step_rulesets,
            example_set_name=config.get("use_example_set"),
            context=context,
        )

        # Create Griptape agent with rules
        griptape_rules = self._build_griptape_rules(step_rulesets)

        try:
            agent = Agent(
                prompt_driver=context.llm_driver,
                rules=griptape_rules,
            )

            # Run classification
            logger.debug(f"Classification prompt:\n{prompt[:500]}...")
            response = agent.run(prompt)
            response_text = response.output_task.output.value

            logger.debug(f"Classification response:\n{response_text}")

            # Parse the response
            classification = self._parse_classification_response(response_text)

            # Store results in context for condition evaluation
            for key, value in classification.items():
                context.set_variable(key, value)

            result.complete(classification)
            logger.info(f"Classification result: {classification.get('ticket_type')} "
                       f"(confidence: {classification.get('confidence', 'N/A')})")

        except Exception as e:
            logger.error(f"Classification failed: {e}")
            result.fail(str(e))

        return result

    def _build_classification_prompt(
        self,
        ticket_data: dict[str, Any],
        rulesets: list[RulesetExportInfo],
        example_set_name: str | None,
        context: ExecutionContext,
    ) -> str:
        """Build classification prompt dynamically from portal-defined examples."""

        # 1. Resolve example set
        example_set = self._resolve_example_set(example_set_name, context)

        # 2. Extract valid categories from examples
        categories = self._extract_categories(example_set) if example_set else ["unknown"]

        # 3. Build system instruction with dynamic categories
        system_instruction = self._build_system_instruction(categories)

        # 4. Build few-shot section from examples
        few_shot = self._build_few_shot_section(example_set) if example_set else ""

        # 5. Build ticket section
        ticket_section = self._build_ticket_section(ticket_data)

        # 6. Combine
        parts = [system_instruction]
        rules_text = self.build_rules_prompt(rulesets)
        if rules_text:
            parts.append(rules_text)
        if few_shot:
            parts.append(few_shot)
        parts.append(ticket_section)

        return "\n\n".join(parts)

    def _resolve_example_set(
        self,
        example_set_name: str | None,
        context: ExecutionContext,
    ) -> ExampleSetExportInfo | None:
        """Find the example set to use."""
        export = getattr(context, '_agent_export', None)
        if not export or not export.example_sets:
            logger.warning("No example sets available in agent export")
            return None

        # If explicit name given (from step config), use it
        if example_set_name and example_set_name in export.example_sets:
            return export.example_sets[example_set_name]

        # Otherwise try the workflow's linked example set
        if export.workflow and export.workflow.example_set_name:
            wf_set_name = export.workflow.example_set_name
            if wf_set_name in export.example_sets:
                return export.example_sets[wf_set_name]

        # Fallback: use first available
        if export.example_sets:
            first_name = next(iter(export.example_sets))
            logger.info(f"No explicit example set configured, using '{first_name}'")
            return export.example_sets[first_name]

        return None

    def _extract_categories(self, example_set: ExampleSetExportInfo) -> list[str]:
        """Extract unique ticket_type values from examples' expected outputs."""
        categories: set[str] = set()
        for example in example_set.examples:
            if example.expected_output_json:
                try:
                    output = json.loads(example.expected_output_json)
                    ticket_type = output.get("ticket_type")
                    if ticket_type:
                        categories.add(ticket_type)
                except json.JSONDecodeError:
                    logger.warning("Could not parse expected output JSON for example")
        # Always include "unknown" as a fallback category
        categories.add("unknown")
        return sorted(categories)

    def _build_system_instruction(self, categories: list[str]) -> str:
        """Build the system instruction with dynamic category list."""
        cat_list = ", ".join(f'"{c}"' for c in categories)
        return f"""You are an IT helpdesk ticket classifier. Analyze the ticket and classify it.

Your response MUST be valid JSON with these fields:
- ticket_type: One of {cat_list}
- confidence: A number between 0.0 and 1.0 indicating your confidence
- affected_user: The username of the person the request is about (if identifiable, else null)
- target_group: The AD group name (if this is a group access request, else null)
- target_resource: The file/folder path (if this is a permission request, else null)
- reasoning: Brief explanation of your classification

If the ticket does not clearly match any category, use "unknown" with low confidence.
Respond with ONLY the JSON object, no other text."""

    def _build_few_shot_section(self, example_set: ExampleSetExportInfo) -> str:
        """Build few-shot examples section from example set data."""
        if not example_set or not example_set.examples:
            return ""

        parts = ["## Classification Examples\n"]
        for i, example in enumerate(example_set.examples, 1):
            if not example.input_text:
                continue
            parts.append(f"Example {i}:")
            parts.append(f"Ticket: \"{example.input_text}\"")
            if example.expected_output_json:
                parts.append(f"Classification:\n```json\n{example.expected_output_json}\n```")
            parts.append("")  # blank line between examples

        return "\n".join(parts)

    def _build_ticket_section(self, ticket_data: dict[str, Any]) -> str:
        """Build the ticket-to-classify section."""
        short_desc = ticket_data.get("short_description", "")
        description = ticket_data.get("description", "")
        caller = ticket_data.get("caller_id", "Unknown")
        return f"""## Ticket to Classify

**Caller**: {caller}
**Short Description**: {short_desc}
**Description**: {description}

Classify this ticket and respond with ONLY the JSON object:"""

    def _build_griptape_rules(self, rulesets: list[RulesetExportInfo]) -> list[Rule]:
        """Convert rulesets to Griptape Rule objects."""
        rules = []

        for ruleset in rulesets:
            for rule in ruleset.rules:
                if rule.is_enabled:
                    rules.append(Rule(rule.rule_text))

        return rules

    def _parse_classification_response(self, response: str) -> dict[str, Any]:
        """Parse LLM response to extract classification JSON."""
        # Try to extract JSON from response
        # Handle cases where LLM wraps in markdown code blocks

        # Remove markdown code blocks if present
        json_match = re.search(r'```(?:json)?\s*([\s\S]*?)\s*```', response)
        if json_match:
            json_str = json_match.group(1)
        else:
            # Try to find raw JSON object
            json_match = re.search(r'\{[\s\S]*\}', response)
            if json_match:
                json_str = json_match.group(0)
            else:
                # Return low confidence unknown if we can't parse
                logger.warning(f"Could not parse classification response: {response[:200]}")
                return {
                    "ticket_type": "unknown",
                    "confidence": 0.3,
                    "reasoning": "Failed to parse LLM response",
                    "affected_user": None,
                    "target_group": None,
                    "target_resource": None,
                }

        try:
            classification = json.loads(json_str)

            # Ensure required fields with defaults
            classification.setdefault("ticket_type", "unknown")
            classification.setdefault("confidence", 0.5)
            classification.setdefault("affected_user", None)
            classification.setdefault("target_group", None)
            classification.setdefault("target_resource", None)
            classification.setdefault("reasoning", "")

            # Normalize confidence to float
            classification["confidence"] = float(classification["confidence"])

            return classification

        except json.JSONDecodeError as e:
            logger.warning(f"JSON parse error: {e}")
            return {
                "ticket_type": "unknown",
                "confidence": 0.3,
                "reasoning": f"JSON parse error: {e}",
                "affected_user": None,
                "target_group": None,
                "target_resource": None,
            }
