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
        """Build the classification prompt with rules and examples."""
        parts = []

        # System instruction
        parts.append("""You are an IT helpdesk ticket classifier. Analyze the ticket and classify it.

Your response MUST be valid JSON with these fields:
- ticket_type: One of "password_reset", "group_access_add", "group_access_remove", "file_permission", "unknown"
- confidence: A number between 0.0 and 1.0 indicating your confidence
- affected_user: The username of the person the request is about (if identifiable)
- target_group: The AD group name (if this is a group access request)
- target_resource: The file/folder path (if this is a permission request)
- reasoning: Brief explanation of your classification

Respond with ONLY the JSON object, no other text.""")

        # Add rules from rulesets
        rules_text = self.build_rules_prompt(rulesets)
        if rules_text:
            parts.append(rules_text)

        # Add few-shot examples if available
        # Note: Example sets would come from context.export.example_sets
        # For now, we'll use inline examples
        parts.append("""
## Examples

Example 1:
Ticket: "User jsmith forgot their password and needs it reset"
Classification:
```json
{"ticket_type": "password_reset", "confidence": 0.95, "affected_user": "jsmith", "target_group": null, "target_resource": null, "reasoning": "Clear password reset request with username identified"}
```

Example 2:
Ticket: "Please add Mary Johnson (mjohnson) to the Finance-ReadOnly group"
Classification:
```json
{"ticket_type": "group_access_add", "confidence": 0.92, "affected_user": "mjohnson", "target_group": "Finance-ReadOnly", "target_resource": null, "reasoning": "Request to add user to AD group"}
```

Example 3:
Ticket: "I need access to the Q4 reports folder at \\\\fileserver\\finance\\Q4"
Classification:
```json
{"ticket_type": "file_permission", "confidence": 0.88, "affected_user": null, "target_group": null, "target_resource": "\\\\fileserver\\finance\\Q4", "reasoning": "File permission request, user not explicitly named"}
```
""")

        # Add the ticket to classify
        short_desc = ticket_data.get("short_description", "")
        description = ticket_data.get("description", "")
        caller = ticket_data.get("caller_id", "Unknown")

        parts.append(f"""
## Ticket to Classify

**Caller**: {caller}
**Short Description**: {short_desc}
**Description**: {description}

Classify this ticket and respond with ONLY the JSON object:""")

        return "\n\n".join(parts)

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
