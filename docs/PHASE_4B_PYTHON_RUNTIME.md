# Phase 4B: Python Workflow Runtime

## Overview

Phase 4B transforms the Lucid IT Agent from a hardcoded Python agent into a **data-driven runtime** that executes workflows defined in the Admin Portal. This is the architectural shift from "code defines behavior" to "configuration defines behavior."

## Goals

1. **Execute exported workflows** - Parse and run workflow definitions from Admin Portal
2. **Follow transitions** - Evaluate conditions and route to correct next step
3. **Apply rulesets** - Inject rules into LLM prompts at appropriate steps
4. **Use examples** - Leverage example sets for few-shot classification
5. **Route capabilities** - Call appropriate Tool Servers based on capability routing

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           Python Workflow Runtime                                │
│                                                                                 │
│  ┌──────────────────────────────────────────────────────────────────────────┐  │
│  │                           ConfigLoader                                    │  │
│  │                                                                          │  │
│  │  1. Fetch: GET /api/agents/by-name/{name}/export                        │  │
│  │  2. Parse: Validate JSON → Pydantic models                              │  │
│  │  3. Resolve: Credential references → Environment variables              │  │
│  │  4. Build: LLM driver, ServiceNow connector                             │  │
│  └──────────────────────────────────────────────────────────────────────────┘  │
│                                      │                                         │
│                                      ▼                                         │
│  ┌──────────────────────────────────────────────────────────────────────────┐  │
│  │                          WorkflowEngine                                   │  │
│  │                                                                          │  │
│  │  ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐   │  │
│  │  │ Trigger │──▶│Classify │──▶│Validate │──▶│ Execute │──▶│ Notify  │   │  │
│  │  └─────────┘   └────┬────┘   └────┬────┘   └────┬────┘   └────┬────┘   │  │
│  │                     │             │             │             │         │  │
│  │                     │ <0.8        │ invalid     │ failed      │         │  │
│  │                     ▼             ▼             ▼             ▼         │  │
│  │               ┌──────────┐                               ┌─────────┐   │  │
│  │               │ Escalate │──────────────────────────────▶│  Close  │   │  │
│  │               └──────────┘                               └─────────┘   │  │
│  └──────────────────────────────────────────────────────────────────────────┘  │
│                                      │                                         │
│                                      ▼                                         │
│  ┌──────────────────────────────────────────────────────────────────────────┐  │
│  │                        ExecutionContext                                   │  │
│  │                                                                          │  │
│  │  • ticket_id, ticket_data    • step_results: dict[step_name, StepResult]│  │
│  │  • llm_driver                • variables: dict[str, Any]                 │  │
│  │  • current_step              • status: RUNNING | COMPLETED | ESCALATED  │  │
│  └──────────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

## Sub-Phases

### Phase 4B-1: Core Runtime Structure ✅ (Current)

**Focus**: Foundation classes for workflow execution

| Component | File | Purpose |
|-----------|------|---------|
| `models.py` | `runtime/models.py` | Pydantic models for export JSON |
| `ConfigLoader` | `runtime/config_loader.py` | Fetch export, resolve credentials |
| `ExecutionContext` | `runtime/execution_context.py` | Shared state during execution |
| `ConditionEvaluator` | `runtime/condition_evaluator.py` | Evaluate transition conditions |
| `BaseStepExecutor` | `runtime/executors/base.py` | Abstract base for executors |
| `WorkflowEngine` | `runtime/workflow_engine.py` | Core execution loop |

**Deliverable**: Can load export JSON and walk through workflow steps (passthrough mode)

---

### Phase 4B-2: Step Executors

**Focus**: Implement executors for each step type

| Executor | Step Type | Key Functionality |
|----------|-----------|-------------------|
| `TriggerExecutor` | Trigger | Initialize context, validate ticket |
| `ClassifyExecutor` | Classify | LLM classification with examples and rules |
| `QueryExecutor` | Query | Query external systems for context |
| `ValidateExecutor` | Validate | Run validation checks against rules |
| `ExecuteExecutor` | Execute | Call Tool Server via capability routing |
| `UpdateTicketExecutor` | UpdateTicket | Update ServiceNow ticket |
| `NotifyExecutor` | Notify | Send notifications (ticket comment, email) |
| `EscalateExecutor` | Escalate | Escalate to human, set escalation reason |
| `EndExecutor` | End | Mark workflow complete |

**Key Integration Points**:
- `ClassifyExecutor` uses `DriverFactory` to create LLM prompts with rules/examples
- `ExecuteExecutor` uses `CapabilityRouter` to find Tool Server URL
- `UpdateTicketExecutor` uses `ServiceNowConnector` to update tickets

**Deliverable**: Full step execution with real LLM calls and Tool Server integration

---

### Phase 4B-3: Integration Layer

**Focus**: Wire runtime with existing components

| Integration | Description |
|-------------|-------------|
| ServiceNow Connector | Poll queue, update tickets, add work notes |
| Capability Router | Query Admin Portal for Tool Server URLs |
| Driver Factory | Create Griptape LLM drivers from provider config |
| Tool Server Client | Call Tool Server REST APIs |

**Updates to Existing Code**:
- `connectors/servicenow/` - Add async support if needed
- `routing/router.py` - Ensure compatible with new runtime
- `drivers/factory.py` - Create drivers from export provider config

**Deliverable**: Runtime can poll ServiceNow, call Tool Servers, update tickets

---

### Phase 4B-4: End-to-End Testing

**Focus**: Process real tickets through the workflow

| Test Scenario | Steps Exercised |
|---------------|-----------------|
| Password reset (happy path) | Trigger → Classify → Validate → Execute → Notify → Close |
| Low confidence escalation | Trigger → Classify → Escalate → Close |
| Validation failure | Trigger → Classify → Validate → Escalate → Close |
| Execution failure | Trigger → Classify → Validate → Execute → Escalate → Close |

**Test Environment**:
- Admin Portal running with test-agent configured
- ServiceNow PDI with test tickets
- Tool Server (mock or real) for AD operations
- Ollama with Llama 3.1 for classification

**Deliverable**: Demo video of ticket flowing through system

---

## Data Flow

### 1. Agent Startup
```
Agent starts with:
  ADMIN_PORTAL_URL=http://localhost:5000
  AGENT_NAME=test-agent

ConfigLoader.load() →
  GET /api/agents/by-name/test-agent/export →
  Parse JSON → AgentExport model →
  Resolve credentials (SNOW_USERNAME, SNOW_PASSWORD, etc.) →
  Create LLM driver, ServiceNow connector
```

### 2. Ticket Processing
```
ServiceNowConnector.poll_queue() →
  Returns list of new tickets

For each ticket:
  WorkflowEngine.execute(ticket_id, ticket_data) →
    Start at Trigger step →
    Execute each step via registered executor →
    Evaluate transition conditions →
    Follow matching transition →
    Continue until End or Escalate
```

### 3. Step Execution (Classify Example)
```
ClassifyExecutor.execute(step, context, rulesets) →
  Get step-level rulesets (classification-rules) →
  Get example set (password-reset-examples) →
  Build prompt with rules and few-shot examples →
  Call LLM via context.llm_driver →
  Parse response → ClassificationResult →
  Return StepResult(output={
    "ticket_type": "password_reset",
    "confidence": 0.92,
    "affected_user": "jsmith"
  })
```

### 4. Transition Evaluation
```
Current step: classify-ticket
Outgoing transitions:
  - "confidence >= 0.8" → validate-request
  - "confidence < 0.8" → escalate-to-human

ConditionEvaluator.evaluate("confidence >= 0.8", context) →
  context.confidence = 0.92 →
  0.92 >= 0.8 = True →
  Next step: validate-request
```

## File Structure (Complete)

```
agent/src/agent/
├── runtime/                          # NEW - Phase 4B
│   ├── __init__.py
│   ├── models.py                     # Pydantic models for export
│   ├── config_loader.py              # Fetch and parse export
│   ├── execution_context.py          # Shared execution state
│   ├── condition_evaluator.py        # Transition conditions
│   ├── workflow_engine.py            # Core execution engine
│   └── executors/
│       ├── __init__.py
│       ├── base.py                   # BaseStepExecutor
│       ├── trigger.py                # TriggerExecutor
│       ├── classify.py               # ClassifyExecutor (LLM)
│       ├── query.py                  # QueryExecutor
│       ├── validate.py               # ValidateExecutor
│       ├── execute.py                # ExecuteExecutor (Tool Server)
│       ├── update_ticket.py          # UpdateTicketExecutor
│       ├── notify.py                 # NotifyExecutor
│       ├── escalate.py               # EscalateExecutor
│       └── end.py                    # EndExecutor
├── classifier/                       # EXISTING - may be refactored
├── pipeline/                         # EXISTING - may be deprecated
├── tools/                            # EXISTING - used by ExecuteExecutor
├── routing/                          # EXISTING - CapabilityRouter
├── drivers/                          # EXISTING - DriverFactory
└── connectors/                       # EXISTING - ServiceNowConnector
```

## Key Design Decisions

### 1. Async-First
All executors use `async/await` for non-blocking I/O:
- HTTP calls to Admin Portal, Tool Servers
- LLM inference (can be slow)
- ServiceNow API calls

### 2. Credential References (Not Secrets)
Export contains references, not actual credentials:
```json
{
  "credentials": {
    "storage": "environment",
    "username_key": "SNOW_USERNAME",
    "password_key": "SNOW_PASSWORD"
  }
}
```
Runtime resolves at startup from environment variables.

### 3. Capability Routing (Not Direct URLs)
Export contains capability names, not Tool Server URLs:
```json
{
  "configuration": {
    "capability": "ad-password-reset"
  }
}
```
Runtime queries Admin Portal at execution time for URL.

### 4. Rules as Prompt Injection
Rulesets become part of LLM prompts:
```
## Rules to Follow

### Security Rules
- Never reset passwords for admin accounts
- Verify requester is authorized for target user
- Log all password reset attempts

### Classification Rules  
- Extract username from ticket description
- Look for keywords: forgot, reset, locked, password
```

### 5. Examples as Few-Shot Learning
Example sets provide few-shot examples for classification:
```
Here are examples of how to classify tickets:

Example 1:
Input: "User jsmith forgot their password and needs a reset"
Output: {"ticket_type": "password_reset", "confidence": 0.95, "affected_user": "jsmith"}

Example 2:
Input: "Please add Mary to the Finance group"
Output: {"ticket_type": "group_access_add", "confidence": 0.90, "affected_user": "mary", "target_group": "Finance"}
```

## Success Criteria

### Phase 4B-1 Complete When:
- [ ] All Pydantic models parse export JSON correctly
- [ ] ConfigLoader fetches and validates export
- [ ] ConditionEvaluator handles all transition conditions
- [ ] WorkflowEngine walks through steps (passthrough mode)
- [ ] Unit tests pass

### Phase 4B-2 Complete When:
- [ ] All step executors implemented
- [ ] ClassifyExecutor makes real LLM calls
- [ ] ExecuteExecutor calls Tool Server
- [ ] Unit tests for each executor

### Phase 4B-3 Complete When:
- [ ] ServiceNow integration working
- [ ] Capability routing working
- [ ] Full ticket processing flow works

### Phase 4B-4 Complete When:
- [ ] Password reset ticket flows end-to-end
- [ ] Escalation scenarios work
- [ ] Error handling robust
- [ ] Demo ready

## Dependencies

### Python Packages
```toml
[project]
dependencies = [
    "griptape>=1.8.0",
    "httpx>=0.25.0",      # Async HTTP client
    "pydantic>=2.0",       # Data models
    "python-dotenv>=1.0",  # Environment loading
]
```

### External Services
- Admin Portal (http://localhost:5000)
- Ollama (http://localhost:11434)
- ServiceNow PDI
- Tool Server (Windows or mock)

## Timeline Estimate

| Phase | Effort | Notes |
|-------|--------|-------|
| 4B-1 | 1-2 hours | Core structure, models |
| 4B-2 | 3-4 hours | All executors |
| 4B-3 | 2-3 hours | Integration, wiring |
| 4B-4 | 2-3 hours | Testing, debugging |
| **Total** | **8-12 hours** | Spread across sessions |

## References

- [Phase 4A Export API](../admin/dotnet/src/LucidAdmin.Web/Services/AgentExportService.cs)
- [Export Models](../admin/dotnet/src/LucidAdmin.Web/Api/Models/Responses/AgentExportModels.cs)
- [Griptape Documentation](https://docs.griptape.ai/latest/)
- [Architecture Overview](./ARCHITECTURE.md)
