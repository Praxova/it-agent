# Praxova IT Agent — Test Data Generator

A framework for generating realistic IT helpdesk tickets for testing the Praxova IT Agent.

## Dual Purpose

This tool serves two roles:

1. **Now**: Generate structured test data for E2E testing, regression testing, load testing, and classifier accuracy benchmarking
2. **Future**: Build annotated datasets for fine-tuning an open-source model (QLoRA on Llama 3.1) to specialize in your tool-calling patterns

## Quick Start

```bash
# From the project root
cd /path/to/lucid-it-agent

# Show what's available
python3 -m test_data_generator info

# List all scenarios
python3 -m test_data_generator scenarios -v

# Generate a smoke test batch (15 tickets)
python3 -m test_data_generator generate --preset smoke_test

# Generate a regression suite (50 tickets)
python3 -m test_data_generator generate --preset regression

# Generate and push to ServiceNow
python3 -m test_data_generator generate --preset smoke_test --snow

# Clean up ServiceNow tickets
python3 -m test_data_generator cleanup
```

## Architecture

```
test_data_generator/
├── models.py              # Pydantic models (tickets, scenarios, annotations)
├── scenario_registry.py   # All scenario definitions (the "recipe book")
├── personas.py            # Synthetic employee personas
├── mixer.py               # Distribution control and batch generation
├── generators/
│   └── template_generator.py  # Slot-filling template expansion engine
├── exporters/
│   ├── json_exporter.py       # JSON/JSONL output (test data + future training)
│   └── servicenow_exporter.py # Push tickets to ServiceNow PDI
├── __main__.py            # CLI entry point
└── output/                # Generated files land here
```

## Concepts

### Scenarios
A scenario is a blueprint for generating tickets. It defines:
- **Ticket type** and **complexity tier** (1-5)
- **Variations**: Different phrasings of the same request
- **Slot values**: Named placeholders with possible fill values
- **Expected agent behavior**: Classification, tool calls, workflow path

### Complexity Tiers
| Tier | Description | Example |
|------|-------------|---------|
| 1 | Single-tool, unambiguous | "Reset password for jsmith" |
| 2 | Requires interpretation | "Can't log in" (could be many things) |
| 3 | Multi-step workflows | New hire onboarding (multiple tools) |
| 4 | Edge cases / failures | Admin account on deny list, bulk requests |
| 5 | Ambiguous escalation | Social engineering attempts |

### Personas
Synthetic employees with distinct communication styles:
- **Karen (Accounting)**: Low tech literacy, rambling style
- **Marcus (Engineering)**: High tech literacy, terse
- **Mike (Sales)**: Low tech literacy, angry/urgent
- **Sarah (HR)**: Medium tech literacy, formal
- etc.

Personas automatically transform ticket text to create realistic variation.

### Presets
| Preset | Count | Description |
|--------|-------|-------------|
| `smoke_test` | 15 | Quick validation run |
| `regression` | 50 | Full coverage across all tiers |
| `load_test` | 500 | Production-like distribution |
| `edge_cases` | 30 | Tier 4-5 only |
| `training` | 1000 | Maximized variation for datasets |
| `classifier_only` | 200 | Classification accuracy focus |

### Exhaustive Mode
`--exhaustive` generates every possible combination of variation × slot values.
Currently produces ~3,150 unique tickets from 13 scenarios.

## Output Files

Each generation run produces:

1. **Full dataset** (`test_data_full_*.json`): Complete tickets with all metadata, expected results, and traceability back to scenarios
2. **Scoring key** (`scoring_key_*.json`): Expected classifications and outcomes, for automated scoring of agent runs
3. **Tickets only** (`test_data_tickets_*.json`): Just the ServiceNow fields — feed directly to the agent

## Extending

### Adding a New Scenario

Edit `scenario_registry.py` and add to the appropriate list:

```python
Scenario(
    id="my_new_scenario",
    name="Descriptive Name",
    ticket_type=TicketType.PASSWORD_RESET,
    complexity_tier=ComplexityTier.TIER_2,
    expected_outcome=ExpectedOutcome.RESOLVE,
    variations=[
        ScenarioVariation(
            label="variant_a",
            short_description_template="Short desc with {username}",
            description_template="Full description with {username} and {detail}",
        ),
    ],
    slot_values={
        "username": ["jsmith", "mjohnson"],
        "detail": ["some context", "other context"],
    },
    expected_classification=ExpectedClassification(
        ticket_type=TicketType.PASSWORD_RESET,
        min_confidence=0.7,
    ),
    tags=["password", "tier2", "custom"],
)
```

### Adding a New Persona

Edit `personas.py` and add to the `PERSONAS` dict. Map to a valid PDI demo user.

### Python API

```python
from test_data_generator import Mixer, PRESETS, JsonExporter

mixer = Mixer(seed=42)

# Use a preset
tickets = mixer.generate(PRESETS["regression"])

# Or generate by type
from test_data_generator import TicketType
pwd_tickets = mixer.generate_by_type(TicketType.PASSWORD_RESET, n=100)

# Export
exporter = JsonExporter("./output")
exporter.export_tickets(tickets)
exporter.export_scoring_key(tickets)
```

## Future: Training Data Pipeline

The `AnnotatedInteraction` model and `export_training_data()` method are
scaffolded for when you're ready to create fine-tuning datasets:

1. Generate tickets with this tool
2. Run the agent against them
3. Capture interaction traces
4. Mark correct ones, fix incorrect ones
5. Export as JSONL for QLoRA fine-tuning

The annotated interactions capture the full conversation including tool calls,
which is exactly the format needed for supervised fine-tuning on tool use.
