"""Lucid IT Agent — Test Data Generator

A framework for generating realistic IT helpdesk tickets for:
- E2E agent testing
- Load testing
- Classifier accuracy benchmarking
- Future model fine-tuning dataset creation

Usage:
    from test_data_generator import Mixer, PRESETS, JsonExporter

    mixer = Mixer(seed=42)
    tickets = mixer.generate(PRESETS["regression"])

    exporter = JsonExporter("./output")
    exporter.export_tickets(tickets)
"""

from .models import (
    AnnotatedInteraction,
    ComplexityTier,
    ExpectedOutcome,
    GeneratedTicket,
    Persona,
    Scenario,
    TicketType,
)
from .mixer import Mixer, MixRecipe, PRESETS
from .generators import TemplateGenerator
from .exporters import JsonExporter, ServiceNowExporter
from .scenario_registry import (
    ALL_SCENARIOS,
    SCENARIO_MAP,
    get_scenario,
    get_scenarios_by_tag,
    get_scenarios_by_tier,
    get_scenarios_by_type,
    list_scenario_ids,
    summary as registry_summary,
)

__all__ = [
    # Core models
    "GeneratedTicket",
    "Scenario",
    "Persona",
    "AnnotatedInteraction",
    "TicketType",
    "ComplexityTier",
    "ExpectedOutcome",
    # Generator
    "TemplateGenerator",
    # Mixer
    "Mixer",
    "MixRecipe",
    "PRESETS",
    # Exporters
    "JsonExporter",
    "ServiceNowExporter",
    # Registry
    "ALL_SCENARIOS",
    "SCENARIO_MAP",
    "get_scenario",
    "get_scenarios_by_tag",
    "get_scenarios_by_tier",
    "get_scenarios_by_type",
    "list_scenario_ids",
    "registry_summary",
]
