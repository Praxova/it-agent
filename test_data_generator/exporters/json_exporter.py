"""JSON exporter — saves generated tickets as structured JSON files.

Produces two output formats:
1. Full annotated dataset (includes expected results, metadata)
2. Ticket-only dataset (just the ServiceNow fields, for feeding to agent)

Both can be used for testing. The full annotated version is the foundation
for future training data — run the agent, capture traces, annotate, export.
"""

from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

from ..models import GeneratedTicket, AnnotatedInteraction


class JsonExporter:
    """Export generated tickets to JSON files."""

    def __init__(self, output_dir: str = "./output"):
        self.output_dir = Path(output_dir)
        self.output_dir.mkdir(parents=True, exist_ok=True)

    def export_tickets(
        self,
        tickets: list[GeneratedTicket],
        filename: str | None = None,
        full: bool = True,
    ) -> Path:
        """Export tickets to a JSON file.

        Args:
            tickets: List of generated tickets.
            filename: Output filename (auto-generated if None).
            full: If True, include all metadata. If False, ticket fields only.

        Returns:
            Path to the created file.
        """
        if filename is None:
            ts = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            mode = "full" if full else "tickets"
            filename = f"test_data_{mode}_{ts}.json"

        filepath = self.output_dir / filename

        if full:
            data = {
                "generated_at": datetime.utcnow().isoformat(),
                "ticket_count": len(tickets),
                "summary": self._build_summary(tickets),
                "tickets": [t.model_dump(mode="json") for t in tickets],
            }
        else:
            # Ticket-only: just what you'd send to ServiceNow
            data = {
                "generated_at": datetime.utcnow().isoformat(),
                "ticket_count": len(tickets),
                "tickets": [
                    {
                        "short_description": t.short_description,
                        "description": t.description,
                        "caller_username": t.caller_username,
                        "category": t.category,
                        "subcategory": t.subcategory,
                        "impact": t.impact,
                        "urgency": t.urgency,
                    }
                    for t in tickets
                ],
            }

        filepath.write_text(json.dumps(data, indent=2, default=str))
        return filepath

    def export_scoring_key(
        self,
        tickets: list[GeneratedTicket],
        filename: str | None = None,
    ) -> Path:
        """Export just the expected results for automated scoring.

        Produces a file mapping ticket IDs to expected classifications,
        tool calls, and outcomes. Used to score agent runs.
        """
        if filename is None:
            ts = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            filename = f"scoring_key_{ts}.json"

        filepath = self.output_dir / filename

        data = {
            "generated_at": datetime.utcnow().isoformat(),
            "ticket_count": len(tickets),
            "keys": [
                {
                    "ticket_id": t.id,
                    "scenario_id": t.scenario_id,
                    "expected_classification": t.expected_classification.model_dump(mode="json"),
                    "expected_tool_calls": [
                        tc.model_dump(mode="json") for tc in t.expected_tool_calls
                    ],
                    "expected_outcome": t.expected_outcome.value,
                    "complexity_tier": t.complexity_tier.value,
                }
                for t in tickets
            ],
        }

        filepath.write_text(json.dumps(data, indent=2, default=str))
        return filepath

    def export_training_data(
        self,
        interactions: list[AnnotatedInteraction],
        filename: str | None = None,
    ) -> Path:
        """Export annotated interactions in a format suitable for fine-tuning.

        This produces JSONL (one JSON object per line) where each line is
        a complete conversation trace that the model should learn to produce.
        """
        if filename is None:
            ts = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            filename = f"training_{ts}.jsonl"

        filepath = self.output_dir / filename

        lines = []
        for interaction in interactions:
            if not interaction.is_correct:
                continue  # Only export verified-correct interactions

            record = {
                "scenario_id": interaction.ticket.scenario_id,
                "ticket": {
                    "short_description": interaction.ticket.short_description,
                    "description": interaction.ticket.description,
                    "caller_username": interaction.ticket.caller_username,
                },
                "messages": [
                    {"role": step.role, "content": step.content}
                    for step in interaction.interaction
                ],
            }
            lines.append(json.dumps(record, default=str))

        filepath.write_text("\n".join(lines))
        return filepath

    def _build_summary(self, tickets: list[GeneratedTicket]) -> dict:
        """Build a summary of the ticket distribution."""
        by_type: dict[str, int] = {}
        by_tier: dict[int, int] = {}
        by_outcome: dict[str, int] = {}
        by_scenario: dict[str, int] = {}

        for t in tickets:
            by_type[t.expected_classification.ticket_type.value] = (
                by_type.get(t.expected_classification.ticket_type.value, 0) + 1
            )
            by_tier[t.complexity_tier.value] = (
                by_tier.get(t.complexity_tier.value, 0) + 1
            )
            by_outcome[t.expected_outcome.value] = (
                by_outcome.get(t.expected_outcome.value, 0) + 1
            )
            by_scenario[t.scenario_id] = (
                by_scenario.get(t.scenario_id, 0) + 1
            )

        return {
            "by_type": by_type,
            "by_tier": by_tier,
            "by_outcome": by_outcome,
            "by_scenario": by_scenario,
        }
