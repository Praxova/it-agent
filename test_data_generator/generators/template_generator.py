"""Template-based ticket generator.

Expands scenario definitions into concrete ticket instances by:
1. Selecting a variation (or cycling through all)
2. Filling template slots with values (cartesian product or random sampling)
3. Auto-resolving {display_name} from the test user CSV when {username} is filled
4. Optionally applying a persona's communication style
5. Producing GeneratedTicket instances with full metadata
"""

from __future__ import annotations

import itertools
import random
from typing import Iterator

from ..models import (
    ComplexityTier,
    GeneratedTicket,
    Persona,
    Scenario,
    ScenarioVariation,
)
from ..personas import get_personas_for_issue
from ..data import get_user


class TemplateGenerator:
    """Generates tickets from scenario templates via slot expansion.

    Usage:
        gen = TemplateGenerator(seed=42)

        # Generate all possible combinations for a scenario
        tickets = list(gen.expand_all(scenario))

        # Generate N random tickets from a scenario
        tickets = list(gen.sample(scenario, n=50))

        # Generate with persona styling
        tickets = list(gen.sample(scenario, n=10, persona=karen))
    """

    def __init__(self, seed: int | None = None):
        self._rng = random.Random(seed)

    def expand_all(self, scenario: Scenario) -> Iterator[GeneratedTicket]:
        """Generate every combination of variation × slot values.

        This is the exhaustive mode — useful for building complete coverage
        test sets. Can produce a large number of tickets for scenarios with
        many slots.
        """
        for variation in scenario.variations:
            slot_names = list(scenario.slot_values.keys())

            if not slot_names:
                yield self._build_ticket(scenario, variation, {})
                continue

            slot_lists = [scenario.slot_values[name] for name in slot_names]
            for combo in itertools.product(*slot_lists):
                slot_fill = dict(zip(slot_names, combo))
                yield self._build_ticket(scenario, variation, slot_fill)

    def sample(
        self,
        scenario: Scenario,
        n: int = 10,
        persona: Persona | None = None,
        auto_persona: bool = True,
    ) -> Iterator[GeneratedTicket]:
        """Generate N random tickets from a scenario.

        Args:
            scenario: The scenario blueprint.
            n: Number of tickets to generate.
            persona: Optional specific persona to apply.
            auto_persona: If True and no persona given, randomly select
                          an appropriate persona for each ticket.
        """
        for _ in range(n):
            variation = self._rng.choice(scenario.variations)

            slot_fill = {}
            for name, values in scenario.slot_values.items():
                slot_fill[name] = self._rng.choice(values)

            active_persona = persona
            if active_persona is None and auto_persona:
                candidates = get_personas_for_issue(scenario.ticket_type.value)
                if candidates:
                    active_persona = self._rng.choice(candidates)

            ticket = self._build_ticket(
                scenario, variation, slot_fill, active_persona
            )
            yield ticket

    def _build_ticket(
        self,
        scenario: Scenario,
        variation: ScenarioVariation,
        slot_fill: dict[str, str],
        persona: Persona | None = None,
    ) -> GeneratedTicket:
        """Build a single GeneratedTicket from a variation + slot values."""

        # Auto-resolve {display_name} from user CSV when {username} is present
        resolved = dict(slot_fill)
        if "username" in resolved and "display_name" not in resolved:
            user = get_user(resolved["username"])
            if user:
                resolved["display_name"] = user.display_name
            else:
                resolved["display_name"] = resolved["username"]

        # Fill templates
        try:
            short_desc = variation.short_description_template.format(**resolved)
        except KeyError:
            short_desc = variation.short_description_template

        try:
            description = variation.description_template.format(**resolved)
        except KeyError:
            description = variation.description_template

        # Apply persona styling
        if persona:
            description = self._apply_persona_style(description, persona)
            caller = persona.username
        else:
            caller = resolved.get("username", "jsmith")

        # Update expected classification with actual slot values
        expected = scenario.expected_classification.model_copy()
        if "username" in resolved and not expected.affected_user:
            expected.affected_user = resolved["username"]
        if "group_name" in resolved and not expected.target_group:
            expected.target_group = resolved["group_name"]
        if "path" in resolved and not expected.target_resource:
            expected.target_resource = resolved["path"]

        sn = scenario.servicenow_fields

        return GeneratedTicket(
            scenario_id=scenario.id,
            variation_label=variation.label,
            persona_username=persona.username if persona else None,
            short_description=short_desc,
            description=description,
            caller_username=caller,
            category=sn.get("category", "Software"),
            subcategory=sn.get("subcategory", ""),
            impact=sn.get("impact", "3"),
            urgency=sn.get("urgency", "3"),
            expected_classification=expected,
            expected_tool_calls=scenario.expected_tool_calls,
            expected_outcome=scenario.expected_outcome,
            expected_workflow_path=scenario.expected_workflow_path,
            complexity_tier=scenario.complexity_tier,
            tags=scenario.tags,
        )

    def _apply_persona_style(self, text: str, persona: Persona) -> str:
        """Transform ticket text to match a persona's communication style.

        This is a rule-based approximation. For higher fidelity, the
        LLM generator can rewrite in the persona's voice.
        """
        style = persona.communication_style

        if style == "terse":
            for phrase in ["please ", "Please ", "Hi,\n\n", "Hi, ", "Thanks,\n",
                           "Thank you!", "\n\nThank you!", "\n\nThanks!"]:
                text = text.replace(phrase, "")
            text = text.strip()

        elif style == "angry":
            text = text.upper()
            if not text.endswith("!!!"):
                text = text.rstrip(".!") + "!!!"
            text = "THIS IS URGENT. " + text

        elif style == "rambling":
            fillers = [
                "I'm not sure if this is the right place for this but ",
                "Sorry to bother you, I know you're probably busy, but ",
                "I tried calling the other number but nobody answered so ",
            ]
            prefix = self._rng.choice(fillers)
            suffixes = [
                " Also my monitor has been flickering but that's probably a different issue.",
                " Let me know if you need anything else. I'm usually at my desk except Tuesdays.",
                " I think someone else on my team had this problem too but I'm not sure who.",
            ]
            suffix = self._rng.choice(suffixes)
            text = prefix + text + suffix

        elif style == "formal":
            if not text.startswith("Dear"):
                text = (
                    f"Dear IT Support Team,\n\n"
                    f"I am writing to formally request the following:\n\n"
                    f"{text}\n\n"
                    f"Please confirm receipt of this request and provide an "
                    f"estimated resolution time.\n\n"
                    f"Best regards,\n{persona.display_name}\n"
                    f"{persona.role}, {persona.department}"
                )

        elif style == "verbose":
            context_additions = [
                f"For context, I'm in the {persona.department} department "
                f"and I've been with the company for about 3 years. ",
                f"I spoke with my manager about this and they agreed I "
                f"should submit a ticket. ",
                f"I tried to figure this out myself first but couldn't "
                f"find the right documentation anywhere. ",
            ]
            text = self._rng.choice(context_additions) + text

        return text
