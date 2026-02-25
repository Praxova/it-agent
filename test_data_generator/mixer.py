"""Ticket batch mixer — controls the distribution of generated tickets.

The mixer takes a "recipe" specifying how many tickets of each complexity
tier (or type, or tag) you want, then draws from the scenario registry
and generator to produce a balanced batch.

Preset recipes are provided for common use cases:
- smoke_test:  Small quick run, 1-2 of each type
- regression:  Broader coverage, all tiers represented
- load_test:   High volume, realistic distribution
- edge_cases:  Focused on tier 4-5 only
- training:    Exhaustive, maximizes variation
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field

from .models import ComplexityTier, GeneratedTicket, TicketType
from .generators.template_generator import TemplateGenerator
from .scenario_registry import (
    ALL_SCENARIOS,
    get_scenarios_by_tag,
    get_scenarios_by_tier,
    get_scenarios_by_type,
)


@dataclass
class MixRecipe:
    """Defines the desired distribution of a ticket batch."""
    name: str
    total: int
    tier_weights: dict[int, float] = field(default_factory=dict)
    type_weights: dict[str, float] = field(default_factory=dict)
    tag_include: list[str] = field(default_factory=list)
    tag_exclude: list[str] = field(default_factory=list)
    description: str = ""


# ---------------------------------------------------------------------------
# Preset recipes
# ---------------------------------------------------------------------------

PRESETS: dict[str, MixRecipe] = {
    "smoke_test": MixRecipe(
        name="smoke_test",
        total=15,
        tier_weights={1: 0.5, 2: 0.2, 4: 0.2, 5: 0.1},
        description="Quick validation — one or two of each major type",
    ),
    "regression": MixRecipe(
        name="regression",
        total=50,
        tier_weights={1: 0.35, 2: 0.25, 3: 0.15, 4: 0.15, 5: 0.10},
        description="Full coverage across all tiers for regression testing",
    ),
    "load_test": MixRecipe(
        name="load_test",
        total=500,
        tier_weights={1: 0.50, 2: 0.25, 3: 0.10, 4: 0.10, 5: 0.05},
        description="High volume, realistic distribution matching production",
    ),
    "edge_cases": MixRecipe(
        name="edge_cases",
        total=30,
        tier_weights={4: 0.6, 5: 0.4},
        description="Focused on edge cases and ambiguous scenarios only",
    ),
    "training": MixRecipe(
        name="training",
        total=1000,
        tier_weights={1: 0.30, 2: 0.25, 3: 0.15, 4: 0.20, 5: 0.10},
        description="Maximized variation for fine-tuning dataset creation",
    ),
    "classifier_only": MixRecipe(
        name="classifier_only",
        total=200,
        tier_weights={1: 0.3, 2: 0.3, 4: 0.3, 5: 0.1},
        description="Balanced set focused on classification accuracy testing",
    ),
}


class Mixer:
    """Generates batches of tickets according to a distribution recipe.

    Usage:
        mixer = Mixer(seed=42)
        tickets = mixer.generate(PRESETS["regression"])
        tickets = mixer.generate(MixRecipe(name="custom", total=100, ...))
    """

    def __init__(self, seed: int | None = None):
        self._rng = random.Random(seed)
        self._gen = TemplateGenerator(seed=seed)

    def generate(self, recipe: MixRecipe) -> list[GeneratedTicket]:
        """Generate a batch of tickets matching the recipe distribution."""
        tickets: list[GeneratedTicket] = []

        # Calculate how many tickets per tier
        tier_counts = self._distribute(recipe.total, recipe.tier_weights)

        for tier_value, count in tier_counts.items():
            tier = ComplexityTier(tier_value)
            scenarios = get_scenarios_by_tier(tier)

            # Apply tag filters
            if recipe.tag_include:
                scenarios = [
                    s for s in scenarios
                    if any(t in s.tags for t in recipe.tag_include)
                ]
            if recipe.tag_exclude:
                scenarios = [
                    s for s in scenarios
                    if not any(t in s.tags for t in recipe.tag_exclude)
                ]

            if not scenarios:
                continue

            # Distribute count across available scenarios for this tier
            per_scenario = max(1, count // len(scenarios))
            remainder = count - (per_scenario * len(scenarios))

            for i, scenario in enumerate(scenarios):
                n = per_scenario + (1 if i < remainder else 0)
                batch = list(self._gen.sample(scenario, n=n))
                tickets.extend(batch)

        # Shuffle to avoid clustering by type
        self._rng.shuffle(tickets)

        # Trim to exact total if we overshot
        return tickets[:recipe.total]

    def generate_by_type(
        self,
        ticket_type: TicketType,
        n: int = 50,
    ) -> list[GeneratedTicket]:
        """Generate N tickets of a specific type."""
        scenarios = get_scenarios_by_type(ticket_type)
        if not scenarios:
            return []

        tickets: list[GeneratedTicket] = []
        per_scenario = max(1, n // len(scenarios))
        remainder = n - (per_scenario * len(scenarios))

        for i, scenario in enumerate(scenarios):
            count = per_scenario + (1 if i < remainder else 0)
            tickets.extend(self._gen.sample(scenario, n=count))

        self._rng.shuffle(tickets)
        return tickets[:n]

    def generate_exhaustive(self) -> list[GeneratedTicket]:
        """Generate every possible combination across all scenarios.

        Useful for building maximum-coverage training datasets.
        Warning: can produce a large number of tickets.
        """
        tickets: list[GeneratedTicket] = []
        for scenario in ALL_SCENARIOS:
            tickets.extend(self._gen.expand_all(scenario))
        return tickets

    def _distribute(
        self, total: int, weights: dict[int, float]
    ) -> dict[int, int]:
        """Distribute a total count according to weights."""
        if not weights:
            # Equal distribution across all tiers that have scenarios
            active_tiers = {s.complexity_tier.value for s in ALL_SCENARIOS}
            weights = {t: 1.0 for t in active_tiers}

        weight_sum = sum(weights.values())
        counts: dict[int, int] = {}
        allocated = 0

        for tier, weight in weights.items():
            count = int(total * (weight / weight_sum))
            counts[tier] = count
            allocated += count

        # Distribute remainder to highest-weighted tier
        remainder = total - allocated
        if remainder > 0 and weights:
            top_tier = max(weights, key=weights.get)  # type: ignore
            counts[top_tier] = counts.get(top_tier, 0) + remainder

        return counts
