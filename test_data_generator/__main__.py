#!/usr/bin/env python3
"""CLI for the Lucid IT Agent Test Data Generator.

Usage:
    # Show registry summary
    python -m test_data_generator info

    # Generate a smoke test batch to JSON
    python -m test_data_generator generate --preset smoke_test

    # Generate a regression batch and push to ServiceNow
    python -m test_data_generator generate --preset regression --snow

    # Generate custom batch
    python -m test_data_generator generate --total 200 --tier1 0.4 --tier2 0.3 --tier4 0.2 --tier5 0.1

    # Generate exhaustive dataset (all combinations)
    python -m test_data_generator generate --exhaustive

    # Cleanup ServiceNow tickets
    python -m test_data_generator cleanup

    # List all scenarios
    python -m test_data_generator scenarios
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Add parent directory to path so we can import as a package
_here = Path(__file__).resolve().parent
if str(_here.parent) not in sys.path:
    sys.path.insert(0, str(_here.parent))

from test_data_generator.models import ComplexityTier
from test_data_generator.mixer import Mixer, MixRecipe, PRESETS
from test_data_generator.exporters.json_exporter import JsonExporter
from test_data_generator.exporters.servicenow_exporter import ServiceNowExporter
from test_data_generator.scenario_registry import (
    ALL_SCENARIOS,
    summary as registry_summary,
    list_scenario_ids,
)


def cmd_info(args: argparse.Namespace) -> int:
    """Show registry summary."""
    print(registry_summary())

    print("\n\nAvailable Presets:")
    print("-" * 50)
    for name, recipe in PRESETS.items():
        print(f"  {name:<20} {recipe.total:>5} tickets — {recipe.description}")

    return 0


def cmd_scenarios(args: argparse.Namespace) -> int:
    """List all scenarios with details."""
    for s in ALL_SCENARIOS:
        var_count = len(s.variations)
        print(
            f"  {s.id:<35} "
            f"T{s.complexity_tier.value}  "
            f"{s.ticket_type.value:<25} "
            f"{s.expected_outcome.value:<30} "
            f"{var_count} variations"
        )
        if args.verbose:
            for v in s.variations:
                print(f"    └─ {v.label}: {v.short_description_template[:60]}")
    print(f"\n  Total: {len(ALL_SCENARIOS)} scenarios")
    return 0


def cmd_generate(args: argparse.Namespace) -> int:
    """Generate test tickets."""
    mixer = Mixer(seed=args.seed)

    if args.exhaustive:
        print("Generating exhaustive dataset (all combinations)...")
        tickets = mixer.generate_exhaustive()
        print(f"  Generated {len(tickets)} tickets")

    elif args.preset:
        if args.preset not in PRESETS:
            print(f"Unknown preset: {args.preset}")
            print(f"Available: {', '.join(PRESETS.keys())}")
            return 1

        recipe = PRESETS[args.preset]
        print(f"Generating with preset '{args.preset}': {recipe.description}")
        tickets = mixer.generate(recipe)
        print(f"  Generated {len(tickets)} tickets")

    else:
        # Custom recipe from CLI args
        tier_weights = {}
        if args.tier1:
            tier_weights[1] = args.tier1
        if args.tier2:
            tier_weights[2] = args.tier2
        if args.tier3:
            tier_weights[3] = args.tier3
        if args.tier4:
            tier_weights[4] = args.tier4
        if args.tier5:
            tier_weights[5] = args.tier5

        recipe = MixRecipe(
            name="custom",
            total=args.total,
            tier_weights=tier_weights if tier_weights else {1: 0.4, 2: 0.25, 4: 0.25, 5: 0.1},
        )
        print(f"Generating custom batch: {args.total} tickets")
        tickets = mixer.generate(recipe)
        print(f"  Generated {len(tickets)} tickets")

    # Show distribution summary
    by_type: dict[str, int] = {}
    by_tier: dict[int, int] = {}
    for t in tickets:
        tt = t.expected_classification.ticket_type.value
        by_type[tt] = by_type.get(tt, 0) + 1
        by_tier[t.complexity_tier.value] = by_tier.get(t.complexity_tier.value, 0) + 1

    print("\n  Distribution:")
    print(f"    By type:  {json.dumps(by_type, indent=0).replace(chr(10), ' ')}")
    print(f"    By tier:  {json.dumps(by_tier, indent=0).replace(chr(10), ' ')}")

    # Export to JSON
    output_dir = args.output or "./output"
    exporter = JsonExporter(output_dir)

    full_path = exporter.export_tickets(tickets, full=True)
    print(f"\n  Full dataset:  {full_path}")

    key_path = exporter.export_scoring_key(tickets)
    print(f"  Scoring key:   {key_path}")

    if not args.no_tickets_only:
        tickets_path = exporter.export_tickets(tickets, full=False)
        print(f"  Tickets only:  {tickets_path}")

    # Optionally push to ServiceNow
    if args.snow:
        print("\n  Pushing to ServiceNow...")
        try:
            snow = ServiceNowExporter.from_env()
            if not snow.test_connection():
                print("  ✗  ServiceNow connection failed")
                return 1
            created = snow.create_tickets(tickets)
            print(f"  ✓  Created {len(created)} tickets in ServiceNow")
        except Exception as e:
            print(f"  ✗  ServiceNow error: {e}")
            return 1

    # Show a few example tickets
    if args.preview and tickets:
        print(f"\n  Preview (first {min(3, len(tickets))} tickets):")
        print("  " + "─" * 60)
        for t in tickets[:3]:
            print(f"  [{t.scenario_id}] {t.short_description}")
            desc_preview = t.description[:120].replace("\n", " ")
            print(f"    {desc_preview}...")
            print(f"    Expected: {t.expected_classification.ticket_type.value} "
                  f"(outcome: {t.expected_outcome.value})")
            print()

    return 0


def cmd_cleanup(args: argparse.Namespace) -> int:
    """Clean up open tickets in ServiceNow."""
    print("Cleaning up open tickets in ServiceNow...")
    try:
        snow = ServiceNowExporter.from_env()
        if not snow.test_connection():
            print("  ✗  Connection failed")
            return 1
        count = snow.cleanup_open_tickets()
        print(f"  ✓  Closed {count} tickets")
    except Exception as e:
        print(f"  ✗  Error: {e}")
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Lucid IT Agent — Test Data Generator",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    subparsers = parser.add_subparsers(dest="command", help="Command to run")

    # --- info ---
    subparsers.add_parser("info", help="Show registry summary and available presets")

    # --- scenarios ---
    p_scenarios = subparsers.add_parser("scenarios", help="List all scenarios")
    p_scenarios.add_argument("-v", "--verbose", action="store_true")

    # --- generate ---
    p_gen = subparsers.add_parser("generate", help="Generate test tickets")
    p_gen.add_argument("--preset", choices=list(PRESETS.keys()),
                       help="Use a preset recipe")
    p_gen.add_argument("--total", type=int, default=50,
                       help="Total tickets for custom recipe (default: 50)")
    p_gen.add_argument("--tier1", type=float, help="Weight for Tier 1 tickets")
    p_gen.add_argument("--tier2", type=float, help="Weight for Tier 2 tickets")
    p_gen.add_argument("--tier3", type=float, help="Weight for Tier 3 tickets")
    p_gen.add_argument("--tier4", type=float, help="Weight for Tier 4 tickets")
    p_gen.add_argument("--tier5", type=float, help="Weight for Tier 5 tickets")
    p_gen.add_argument("--exhaustive", action="store_true",
                       help="Generate all possible combinations")
    p_gen.add_argument("--seed", type=int, default=42,
                       help="Random seed for reproducibility (default: 42)")
    p_gen.add_argument("--output", "-o", help="Output directory (default: ./output)")
    p_gen.add_argument("--snow", action="store_true",
                       help="Also push tickets to ServiceNow")
    p_gen.add_argument("--no-tickets-only", action="store_true",
                       help="Skip generating the tickets-only export")
    p_gen.add_argument("--preview", action="store_true", default=True,
                       help="Show preview of generated tickets (default: true)")
    p_gen.add_argument("--no-preview", dest="preview", action="store_false")

    # --- cleanup ---
    subparsers.add_parser("cleanup", help="Close all open tickets in ServiceNow")

    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        return 0

    handlers = {
        "info": cmd_info,
        "scenarios": cmd_scenarios,
        "generate": cmd_generate,
        "cleanup": cmd_cleanup,
    }

    return handlers[args.command](args)


if __name__ == "__main__":
    sys.exit(main())
