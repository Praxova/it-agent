# ADR-010: Visual Workflow Designer Architecture

## Status
Accepted

## Date
2026-01-30

## Context

As Lucid IT Agent matures, we need to enable IT teams to customize agent behavior without writing Python code. The current hardcoded pipeline works for demos but doesn't scale to diverse enterprise environments where each organization has unique:

- Ticket classification categories
- Escalation rules
- Security policies
- Communication templates
- Integration workflows

We need a visual design system that:
1. Allows non-developers to modify agent behavior
2. Generates valid Griptape Python code
3. Provides guardrails to prevent misconfiguration
4. Ships with built-in templates as starting points

## Decision

### Architecture: Blazor + Drawflow.js

We will build the workflow designer within the existing Blazor Admin Portal using Drawflow.js for the visual canvas and Blazor components for configuration forms.

**Why Blazor + Drawflow.js:**
- Single deployment with existing Admin Portal
- Shared authentication (critical for security audit)
- Drawflow is lightweight (~15KB), MIT-licensed, well-documented
- JS interop is manageable complexity
- Rules and Examples editors are pure Blazor (no JS needed)

**Rejected alternatives:**
- Pure Blazor SVG: Too much development effort reinventing drag-and-drop
- React-flow in iframe: Auth sharing complexity, security concerns, fragmented UX
- Separate application: Deployment complexity, auth sharing, maintenance burden

### Three-Screen Design

| Screen | Technology | Purpose |
|--------|------------|---------|
| **Workflow Designer** | Blazor + Drawflow.js | Visual flow of steps and transitions |
| **Rules Editor** | Pure Blazor + MudBlazor | Behavioral constraints and policies |
| **Examples Editor** | Pure Blazor + MudBlazor | Few-shot training data for classifier |

### Data Model

```
WorkflowDefinition (visual pipeline)
    │
    ├── References → Ruleset[] (behavioral rules)
    │
    ├── References → ExampleSet (few-shot examples)
    │
    └── Contains → WorkflowStep[] (nodes in the diagram)
                      │
                      └── Contains → StepTransition[] (connections)
```

### Code Generation Strategy

1. **JSON as source of truth** - Visual designer saves/loads JSON definitions
2. **On-demand generation** - "Generate Python" button creates downloadable code
3. **Runtime interpretation** - Agent can load workflow from API and execute directly
4. **Validation layer** - Prevent invalid configurations before saving

### Built-In Content

Ship with seed data that users can copy and customize:

**Built-in Workflows:**
- Password Reset Standard
- Group Access Request
- File Permission Request
- Generic with Escalation

**Built-in Rulesets:**
- Security (deny lists, privileged account protection)
- Escalation (confidence thresholds, unknown types)
- Communication (customer message templates)
- Validation (required fields, format checks)

**Built-in Example Sets:**
- Password Reset Examples
- Group Access Examples
- File Permission Examples

## Consequences

### Positive
- Non-developers can customize agent behavior
- Visual representation aids understanding
- Built-in templates accelerate deployment
- Single secure deployment
- Reusable rules and examples across workflows

### Negative
- JS interop adds complexity to workflow designer
- Code generation must be carefully tested
- More database entities to maintain
- UI development effort is significant

### Risks and Mitigations
- **Risk**: Generated code has bugs → **Mitigation**: Extensive test suite, "Test Workflow" button
- **Risk**: Users create invalid workflows → **Mitigation**: Validation before save, required connections
- **Risk**: Drawflow.js abandoned → **Mitigation**: Simple library, could replace or fork if needed

## Implementation Order

1. **Phase 1**: Rules Editor (pure Blazor, immediately useful)
2. **Phase 2**: Examples Editor (pure Blazor, feeds classifier)
3. **Phase 3**: Workflow Designer (Blazor + Drawflow.js)
4. **Phase 4**: Code generation and runtime interpretation

## References
- Drawflow: https://github.com/jerosoler/Drawflow
- Griptape Workflows: https://docs.griptape.ai/latest/
- Related: ADR-009 LLM Reasons Tools Execute
