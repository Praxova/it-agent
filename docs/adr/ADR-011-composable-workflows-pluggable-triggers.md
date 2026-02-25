# Architecture Vision: Composable Workflows & Pluggable Triggers
# ADR-011: Evolution Toward a Modular Workflow Platform

**Status**: Accepted — Implemented (E2E validated 2026-02-06)  
**Date**: 2026-02-04  
**Decision Makers**: Alton  

---

## 1. The Vision (Plain English)

Today, the Lucid IT Agent is a ServiceNow-specific helpdesk automation tool. Every
workflow starts with "poll ServiceNow for new tickets," every agent hardcodes
ServiceNow credentials, and every workflow type (password reset, group membership,
file permissions) independently runs classification — even though classification
is the same logic every time.

The vision is two interconnected capabilities:

**Composable Workflows**: Workflows can reference other workflows as sub-steps.
A "dispatcher" workflow runs classification once, then hands off to the right
specialized workflow. In the visual designer, a sub-workflow appears as a single
node with defined inputs and outputs — a black box that hides its internals.

**Pluggable Triggers**: Workflows declare what kind of event starts them (ServiceNow
ticket, Jira issue, email, webhook, manual), and the agent configuration screen
dynamically adapts to show the right service account pickers. The workflow defines
*what* it needs; the agent defines *which specific credentials* to use.

Together, these transform Lucid from "ServiceNow helpdesk bot" into a general-purpose
IT automation platform that can ingest work from anywhere and compose complex
multi-step processes from reusable building blocks.

---

## 2. What Exists Today (Inventory)

### Already in place (foundations we keep):

| Component | What It Does | Status |
|-----------|-------------|--------|
| `WorkflowDefinition.TriggerType` | String field on entity | Exists but unused |
| `WorkflowDefinition.TriggerConfigJson` | JSON trigger config | Exists but unused |
| `WorkflowExportInfo.trigger_type` | Python model field | Exists but unused |
| `TriggerExecutor` | Reads `source` from step config | Works, but source is informational only |
| `ServiceAccount.Provider` | "servicenow", "llm-ollama", etc. | Works, extensible to new providers |
| `Agent` entity | Has `LlmServiceAccountId` + `ServiceNowAccountId` | Works, but only supports two fixed account types |
| `AgentRunner._poll_and_process()` | Polls ServiceNow, processes tickets | Works, but hardcoded to ServiceNow |
| `WorkflowEngine` | Step executor registry, transition evaluation | Clean abstraction, trigger-agnostic already |
| `ExecutionContext` | Carries ticket_data dict through steps | Trigger-agnostic — any dict works |
| `StepType` enum | 10 step types including Trigger, Classify, etc. | No SubWorkflow type yet |

### Key insight: The workflow engine is already trigger-agnostic.
The `WorkflowEngine.execute()` method takes `ticket_data: dict` — it doesn't care
where that dict came from. The ServiceNow coupling lives entirely in `AgentRunner`,
not in the engine. This means pluggable triggers can be implemented by abstracting
`AgentRunner`'s polling layer without touching the engine at all.

---

## 3. Feature A: Pluggable Triggers

### 3.1 The Problem

The `AgentRunner._poll_and_process()` method does this:
```
1. Get assignment_group from ServiceNow config
2. Poll ServiceNow for new tickets with state=1 (New)
3. For each ticket, call engine.execute(ticket_id, ticket_data)
4. Update ticket state in ServiceNow
```

Steps 1-2 are ServiceNow-specific. Steps 3-4 are generic (execute workflow, update
source system). To support Jira, email, webhooks, etc., we need to extract the
"get new work items" and "update work item status" operations into an interface.

### 3.2 The Design: Trigger Provider Interface

```
┌─────────────────────────────────────────────────┐
│  AgentRunner                                     │
│                                                  │
│  ┌──────────────────────────────────────────┐   │
│  │  TriggerProvider (interface)              │   │
│  │    poll() → list[WorkItem]               │   │
│  │    acknowledge(item)                      │   │
│  │    complete(item, result)                 │   │
│  │    escalate(item, reason)                 │   │
│  │    fail(item, error)                      │   │
│  └──────┬───────┬───────┬───────┬───────────┘   │
│         │       │       │       │                │
│   ┌─────┴──┐ ┌──┴───┐ ┌┴─────┐ ┌┴────────┐    │
│   │ServiceNow│ │ Jira │ │Email│ │Webhook │    │
│   │Provider │ │Provid│ │Prov.│ │Provider │    │
│   └────────┘ └──────┘ └─────┘ └─────────┘    │
│                                                  │
│  for item in trigger.poll():                     │
│      trigger.acknowledge(item)                   │
│      result = engine.execute(item.id, item.data) │
│      trigger.complete(item, result)              │
│                                                  │
└─────────────────────────────────────────────────┘
```

### 3.3 WorkItem: The Universal Input

Instead of coupling to "ticket" everywhere, we introduce a generic `WorkItem`:

```python
class WorkItem:
    """Universal work item from any trigger source."""
    id: str                    # Unique ID in source system
    source_type: str           # "servicenow", "jira", "email", "webhook"
    data: dict[str, Any]       # Source-specific fields
    raw: Any                   # Original object from source API

    # Normalized fields (same regardless of source)
    title: str                 # short_description / summary / subject
    description: str           # description / body / content
    requester: str | None      # caller / reporter / from
    priority: str | None       # priority
    created_at: datetime | None
```

The `WorkItem.data` dict is what gets passed to `engine.execute()` as `ticket_data`.
Each trigger provider maps its source-specific fields into the normalized fields.
This means the workflow engine sees the same shape regardless of whether the work
came from ServiceNow, Jira, or an email.

### 3.4 Trigger Provider Implementations

**ServiceNowTriggerProvider** (what exists today, refactored):
```python
class ServiceNowTriggerProvider(TriggerProvider):
    def __init__(self, client: ServiceNowClient, assignment_group: str):
        ...

    async def poll(self) -> list[WorkItem]:
        tickets = await self.client.poll_queue(assignment_group, state="1")
        return [self._to_work_item(t) for t in tickets]

    async def acknowledge(self, item: WorkItem):
        await self.client.set_state(item.id, "2")  # In Progress
        await self.client.add_work_note(item.id, "Picked up by Lucid IT Agent")

    async def complete(self, item: WorkItem, result: ExecutionContext):
        await self.client.set_state(item.id, "6")  # Resolved
        await self.client.add_work_note(item.id, self._build_note(result))

    async def fail(self, item: WorkItem, error: str):
        await self.client.add_work_note(item.id, f"Processing failed: {error}")
```

**WebhookTriggerProvider** (simplest second implementation):
```python
class WebhookTriggerProvider(TriggerProvider):
    """Exposes an HTTP endpoint. Work items are pushed, not polled."""

    async def poll(self) -> list[WorkItem]:
        # Returns items from internal queue (populated by HTTP handler)
        return self._pending_items.copy()

    async def acknowledge(self, item: WorkItem):
        self._pending_items.remove(item)

    async def complete(self, item: WorkItem, result: ExecutionContext):
        # Optional: POST result to a callback URL if provided
        if callback_url := item.data.get("callback_url"):
            await self._post_result(callback_url, result)
```

**EmailTriggerProvider** (future):
```python
class EmailTriggerProvider(TriggerProvider):
    """Monitors an email inbox via IMAP or Microsoft Graph."""
    # poll() → fetch unread emails matching criteria
    # acknowledge() → mark as read or move to processing folder
    # complete() → send reply email with resolution
```

**JiraTriggerProvider** (future):
```python
class JiraTriggerProvider(TriggerProvider):
    """Monitors a Jira queue via REST API."""
    # poll() → JQL query for new issues in project/queue
    # acknowledge() → transition to "In Progress"
    # complete() → transition to "Done", add comment
```

### 3.5 How Trigger Type Connects to Agent Configuration

Currently the Agent entity has:
```csharp
public Guid? LlmServiceAccountId { get; set; }
public Guid? ServiceNowAccountId { get; set; }
```

This is hardcoded to exactly two account types. Instead:

**Option A (Simple — recommended for now):** Keep the fixed fields but add
a generic collection for additional service accounts:

```csharp
// Keep existing for backward compatibility
public Guid? LlmServiceAccountId { get; set; }
public Guid? ServiceNowAccountId { get; set; }

// NEW: Dynamic service account bindings
public ICollection<AgentServiceAccountBinding> ServiceAccountBindings { get; set; }
```

```csharp
public class AgentServiceAccountBinding : BaseEntity
{
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; }

    public Guid ServiceAccountId { get; set; }
    public ServiceAccount ServiceAccount { get; set; }

    /// <summary>
    /// Role this account plays: "trigger", "llm", "execution", "notification"
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Optional qualifier when multiple accounts of same role exist.
    /// E.g., role="execution", qualifier="ad-password-reset"
    /// </summary>
    public string? Qualifier { get; set; }
}
```

**Option B (Full migration):** Replace the fixed FK fields with bindings entirely.
Cleaner but bigger migration. Do this later when the binding system is proven.

### 3.6 Dynamic Agent UI

The Edit Agent dialog changes:

```
CURRENT:                          FUTURE:
┌─────────────────────┐          ┌─────────────────────────────┐
│ Workflow Definition  │          │ Workflow Definition          │
│ [Password Reset ▼]  │          │ [Password Reset ▼]          │
│                      │          │                              │
│ LLM Provider         │          │ This workflow requires:      │
│ [Local Ollama ▼]     │          │                              │
│                      │          │ 🔑 LLM Provider              │
│ ServiceNow Connection│          │ [Local Ollama ▼]             │
│ [Service Now PDI ▼]  │          │                              │
│                      │          │ 🎯 Trigger: ServiceNow       │
│ Assignment Group     │          │ [Service Now PDI ▼]          │
│ [Help Desk]          │          │ Assignment Group: [Help Desk]│
│                      │          │                              │
│ Status               │          │ ⚡ Execution: AD Operations   │
│ [Enabled/Disabled]   │          │ (via capability routing)     │
└─────────────────────┘          │                              │
                                  │ Status                       │
                                  │ [Enabled/Disabled]           │
                                  └─────────────────────────────┘
```

The key change: **Workflow picker comes first.** When you select a workflow, the
form populates the service account pickers dynamically based on what the workflow
declares it needs.

How the workflow declares requirements:
1. **Trigger type** — from the Trigger step or `WorkflowDefinition.TriggerType`
2. **LLM needed** — if any Classify step exists
3. **Execution targets** — from Execute step configurations (capabilities)
4. **Notification channels** — from Notify step configurations

This is computed at export time and included in the export JSON:
```json
{
  "workflow": {
    "name": "password-reset-standard",
    "requirements": {
      "trigger": { "type": "servicenow", "needs_assignment_group": true },
      "llm": true,
      "capabilities": ["ad-password-reset"],
      "notifications": ["email"]
    }
  }
}
```

### 3.7 Implementation Phases for Triggers

**Phase T1: Abstraction** (refactor, no new functionality)
- Create `TriggerProvider` interface and `WorkItem` model
- Refactor `AgentRunner._poll_and_process()` to use `ServiceNowTriggerProvider`
- All existing behavior preserved, just restructured
- Tests pass, no user-visible change

**Phase T2: Webhook Trigger** (first new trigger)
- Implement `WebhookTriggerProvider`
- Add "webhook" as a TriggerType option in designer
- Create a simple webhook endpoint in the agent
- No external dependencies needed — perfect for proving the architecture
- Can test with `curl` or Postman

**Phase T3: Dynamic Agent UI** (UI adaptation)
- Workflow picker moves to top of agent form
- Service account pickers appear dynamically based on workflow requirements
- `AgentServiceAccountBinding` entity added
- Backward compatible with existing fixed fields

**Phase T4: Email Trigger** (second real trigger)
- Implement `EmailTriggerProvider` with Microsoft Graph API
- Add "email" ServiceAccount provider type
- Good demo scenario: monitor a shared mailbox for IT requests

**Phase T5: Jira Trigger** (third trigger)
- Implement `JiraTriggerProvider` with Jira REST API
- Free Atlassian Cloud tier for testing
- Validates the abstraction works across very different source systems

---

## 4. Feature B: Composable Workflows

### 4.1 The Problem

Currently, three separate workflows each independently:
1. Trigger (poll ServiceNow)
2. Classify (call LLM to determine ticket type)
3. Validate/Execute/Notify (handle specific ticket type)

This means:
- Classification runs 3x (once per workflow type check)
- Each agent watches a single queue with a single workflow
- Adding a new ticket type means creating an entirely new workflow
  and assigning it to a new agent

The better pattern:
```
Dispatcher Workflow:
  Trigger → Classify → Route
                          ├── [password-reset] → Sub-Workflow: Password Reset
                          ├── [group-membership] → Sub-Workflow: Group Membership
                          ├── [file-permissions] → Sub-Workflow: File Permissions
                          └── [unknown] → Escalate
```

One agent, one workflow, one classification pass. The specialized logic lives in
sub-workflows that are tested and maintained independently.

### 4.2 Design: SubWorkflow Step Type

Add a new step type:

```csharp
public enum StepType
{
    Trigger, Classify, Query, Validate, Execute,
    UpdateTicket, Notify, Escalate, Condition, End,
    SubWorkflow  // NEW
}
```

A SubWorkflow step:
- References another `WorkflowDefinition` by ID
- In the visual designer, appears as a single node (like any other step)
- Has one input port ("Exec In") and output ports based on how the sub-workflow terminates
- When executed, the workflow engine recursively executes the referenced workflow
- The sub-workflow inherits the parent's `ExecutionContext` (same ticket data, variables)

### 4.3 SubWorkflow Node in the Designer

In the designer, when you add a SubWorkflow step from the palette, it:
1. Shows a workflow picker in the configuration panel
2. Once a workflow is selected, the node displays the workflow name
3. The output ports are derived from the sub-workflow's terminal states:
   - If the sub-workflow has an End step → "Completed" output
   - If the sub-workflow has an Escalate step → "Escalated" output
   - Both are common → two output ports

Visual representation:
```
┌──────────────────────────────┐
│ 📋  Password Reset Workflow   │  (header: teal/purple for sub-workflows)
├──────────────────────────────┤
│ SUBWORKFLOW                   │
│                               │
│ ● Exec In    Completed ●     │
│              Escalated ●     │
│                               │
│ ▸ password-reset-standard     │  (small text: referenced workflow name)
│   v1.0.0                      │
└──────────────────────────────┘
```

### 4.4 Execution Model

When the workflow engine encounters a SubWorkflow step:

```python
class SubWorkflowExecutor(BaseStepExecutor):
    """Executes a referenced workflow as a sub-step."""

    @property
    def step_type(self) -> str:
        return StepType.SUB_WORKFLOW.value

    async def execute(self, step, context, rulesets):
        config = step.configuration or {}
        sub_workflow_name = config.get("workflow_name")

        if not sub_workflow_name:
            result.fail("No sub-workflow configured")
            return result

        # Get the sub-workflow definition from the export
        sub_workflow = self._load_sub_workflow(sub_workflow_name)

        # Create a child engine for the sub-workflow
        child_engine = WorkflowEngine(
            export=self._build_sub_export(sub_workflow),
            llm_driver=context.llm_driver,
            admin_portal_url=context.admin_portal_url,
        )
        child_engine.servicenow_client = context.servicenow_client
        child_engine.capability_router = context.capability_router

        # Execute with SHARED context (not a copy)
        # The sub-workflow sees and can modify the same variables
        child_result = await child_engine.execute(
            ticket_id=context.ticket_id,
            ticket_data=context.ticket_data,
        )

        # Map child result to parent transition
        if child_result.status == ExecutionStatus.COMPLETED:
            result.complete({"outcome": "completed", "sub_results": ...})
        elif child_result.status == ExecutionStatus.ESCALATED:
            result.complete({"outcome": "escalated", "reason": child_result.escalation_reason})
        else:
            result.fail(f"Sub-workflow failed: {child_result.escalation_reason}")
```

### 4.5 Context Sharing vs. Isolation

Two options:

**Shared Context (recommended for V1):** The sub-workflow reads and writes the same
`ExecutionContext`. Variables set by the parent (like `ticket_type`, `affected_user`)
are visible to the sub-workflow, and variables set by the sub-workflow (like
`execution_result`) are visible to the parent after it completes.

Pros: Simple. Variables flow naturally. The sub-workflow behaves exactly like
inlining its steps into the parent.

Cons: No isolation. A sub-workflow could accidentally overwrite parent variables.

**Isolated Context (future consideration):** The sub-workflow gets a child context
with explicit input/output mapping. The parent declares "pass `affected_user` as
input" and "read `execution_result` as output."

Pros: Clean contracts. Sub-workflows are truly modular.

Cons: Requires defining input/output schemas on workflows, which adds complexity
to the designer and the execution model.

**Recommendation:** Start with shared context. It matches how Griptape pipelines
share context between tasks. If variable collision becomes a real problem, add
isolation later with a "namespace" prefix (e.g., sub-workflow variables get
prefixed with `sub_workflow_name.`).

### 4.6 Recursion Prevention

Must prevent: Workflow A → SubWorkflow(B) → SubWorkflow(A) → infinite loop

Solution: Track the call stack. The `ExecutionContext` maintains a list of
workflow names currently executing. Before entering a sub-workflow, check if
it's already on the stack:

```python
class ExecutionContext:
    workflow_stack: list[str] = []  # NEW

# In SubWorkflowExecutor:
if sub_workflow_name in context.workflow_stack:
    result.fail(f"Circular workflow reference detected: {context.workflow_stack} → {sub_workflow_name}")
    return result

context.workflow_stack.append(sub_workflow_name)
# ... execute ...
context.workflow_stack.pop()
```

Also set a maximum nesting depth (e.g., 10 levels).

### 4.7 Sub-Workflow in the Step Palette

In the designer's Step Palette, below the existing step types:

```
Step Palette:
┌────────────────────────────┐
│ 🎯 Trigger                  │
│ 🏷️ Classify                 │
│ 🔍 Query                    │
│ ✓  Validate                 │
│ ⚡ Execute                   │
│ 📝 Update Ticket             │
│ 📧 Notify                   │
│ 🚨 Escalate                 │
│ ❓ Condition                 │
│ 🏁 End                      │
│ ─────────────────────────── │
│ 📋 Sub-Workflows:           │
│   📋 Password Reset          │
│   📋 Group Membership        │
│   📋 File Permissions        │
│   📋 (any other active wf)   │
└────────────────────────────┘
```

The sub-workflow section is populated dynamically from active workflows in the
system. Clicking one adds a pre-configured SubWorkflow node to the canvas.

### 4.8 Implementation Phases for Composable Workflows

**Phase C1: Data Model** (entity changes only)
- Add `SubWorkflow` to `StepType` enum
- Add SubWorkflow node model in designer (port definitions)
- Add SubWorkflow widget in designer (visual rendering)
- Sub-workflow picker in step properties panel
- No runtime changes yet — just the designer

**Phase C2: Export & Runtime** (execution support)
- Include sub-workflow definitions in agent export JSON
- Implement `SubWorkflowExecutor` with shared context
- Add recursion detection to `ExecutionContext`
- Add depth limit to `WorkflowEngine`

**Phase C3: Dispatcher Pattern** (the payoff)
- Create a "Dispatcher" workflow that classifies and routes
- Configure it with sub-workflow steps for each ticket type
- Single agent handles all ticket types through one workflow
- Remove redundant classification from specialized workflows

**Phase C4: Dynamic Palette** (UI polish)
- Step palette dynamically lists available workflows as sub-workflow options
- Sub-workflow node auto-populates display name and output ports from referenced workflow
- "Drill into" capability to open sub-workflow in a new tab

---

## 5. How The Two Features Interact

The dispatcher pattern shows how triggers and sub-workflows work together:

```
                    ┌────────────────────────────────────────────────────┐
                    │  Agent: IT Operations Dispatcher                   │
                    │  Trigger Account: ServiceNow PDI (servicenow)     │
                    │  LLM Account: Local Ollama (llm-ollama)           │
                    │  Workflow: IT-Dispatch                             │
                    └────────────────────┬───────────────────────────────┘
                                         │
                    ┌────────────────────▼───────────────────────────────┐
                    │  Workflow: IT-Dispatch                              │
                    │                                                     │
                    │  Trigger → Classify ──┬── [password-reset] ──→ 📋  │
                    │                       ├── [group-membership] ─→ 📋  │
                    │                       ├── [file-permissions] ─→ 📋  │
                    │                       └── [unknown] ──────→ Escalate│
                    └─────────────────────────────────────────────────────┘
```

The sub-workflows don't have their own Trigger steps — they start directly
with the processing logic (Validate → Execute → Notify → End). They inherit
the trigger's ticket data from the parent context.

For the trigger architecture, the dispatcher workflow declares:
- `TriggerType: "servicenow"` → agent needs a ServiceNow service account
- Has Classify step → agent needs an LLM service account
- Sub-workflows have Execute steps → capabilities resolved at runtime

A different agent could use the SAME sub-workflows with a DIFFERENT trigger:
- Jira agent uses IT-Dispatch-Jira with TriggerType: "jira"
- Same sub-workflows (password reset, group membership) reused unchanged
- Only the trigger and classification prompts differ

---

## 6. Recommended Implementation Order

The two features are somewhat independent but share infrastructure.
Here's the recommended sequence:

```
Phase 1: Trigger Abstraction (T1)                    [1 week effort]
  ├── Create TriggerProvider interface + WorkItem
  ├── Refactor AgentRunner to use ServiceNowTriggerProvider
  ├── All existing tests pass — no behavior change
  └── Commit: "refactor: extract trigger provider interface"

Phase 2: SubWorkflow Step Type (C1)                   [1 week effort]
  ├── Add SubWorkflow to StepType enum
  ├── Add node model + widget in designer
  ├── Add sub-workflow picker in step properties
  ├── Dynamic palette section for available workflows
  └── Commit: "feat: add SubWorkflow step type to designer"

Phase 3: Webhook Trigger (T2)                         [3-4 days effort]
  ├── Implement WebhookTriggerProvider
  ├── Add webhook endpoint to agent HTTP server
  ├── Add "webhook" TriggerType option in designer
  ├── Test with curl/Postman
  └── Commit: "feat: add webhook trigger provider"

Phase 4: SubWorkflow Execution (C2)                   [1 week effort]
  ├── SubWorkflowExecutor implementation
  ├── Include sub-workflow defs in export
  ├── Recursion detection + depth limit
  ├── Test with password-reset as sub-workflow
  └── Commit: "feat: sub-workflow execution engine"

Phase 5: Dynamic Agent UI (T3)                        [3-4 days effort]
  ├── AgentServiceAccountBinding entity
  ├── Workflow requirements computation
  ├── Dynamic agent edit form
  └── Commit: "feat: dynamic agent configuration from workflow"

Phase 6: Dispatcher Workflow (C3)                     [2-3 days effort]
  ├── Create IT-Dispatch workflow with sub-workflow steps
  ├── Remove redundant classification from sub-workflows
  ├── Single agent handles all ticket types
  └── Commit: "feat: dispatcher workflow with sub-workflow routing"

Phase 7+: Additional triggers as needed (T4, T5)
  ├── Email trigger (Microsoft Graph)
  ├── Jira trigger (REST API)
  └── Each is ~3-5 days with a test environment
```

### Why This Order?

1. **T1 first** — refactoring doesn't add features but makes everything else
   easier. The trigger abstraction is the foundation for both webhook support
   AND for sub-workflows that might use different triggers.

2. **C1 second** — getting the designer UI for sub-workflows done early lets
   you start building and testing dispatcher workflows visually, even before
   the runtime supports them.

3. **T2 third** — webhook trigger is the simplest proof that the abstraction
   works. Zero external dependencies. Tests with curl.

4. **C2 fourth** — with the designer already supporting sub-workflow nodes and
   the trigger abstraction in place, the executor implementation is focused
   and testable.

5. **T3 fifth** — dynamic agent UI is the polish layer. It's nice-to-have
   but not blocking on any runtime work.

---

## 7. Data Model Changes Summary

### New Entities:
```csharp
AgentServiceAccountBinding
{
    AgentId, ServiceAccountId, Role, Qualifier
}
```

### Modified Entities:
```csharp
// Agent — keep existing FKs, add bindings collection
Agent.ServiceAccountBindings: ICollection<AgentServiceAccountBinding>

// WorkflowDefinition — TriggerType already exists, start using it
WorkflowDefinition.TriggerType → set from Trigger step configuration

// WorkflowStep.ConfigurationJson — for SubWorkflow type, stores:
{
    "workflow_id": "guid",
    "workflow_name": "password-reset-standard"
}
```

### New Enum Value:
```csharp
StepType.SubWorkflow  // Add to existing enum
```

### New Python Models:
```python
class WorkItem(BaseModel): ...
class TriggerProvider(ABC): ...
class SubWorkflowExecutor(BaseStepExecutor): ...
```

---

## 8. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Variable collision in shared context | Medium | Low | Namespace prefix convention; add isolation in V2 |
| Circular sub-workflow references | Low | High | Stack tracking + depth limit |
| Trigger provider abstraction too leaky | Medium | Medium | Start with ServiceNow + Webhook; 2 implementations prove the interface |
| Export JSON grows large with sub-workflows | Low | Low | Lazy-load sub-workflow defs; cache |
| Designer performance with many sub-workflow options | Low | Low | Paginate palette; search filter |
| Different trigger sources have very different data shapes | Medium | Medium | WorkItem normalization layer; per-trigger field mapping |

---

## 9. What We're NOT Doing (Explicit Scope Boundaries)

- **No multi-agent orchestration** — one agent runs one workflow. Sub-workflows
  execute in-process, not by delegating to another agent. (This could come later
  but adds distributed systems complexity we don't need now.)

- **No visual sub-workflow "drill-in"** in V1 — clicking a sub-workflow node
  won't open an embedded editor. It shows the name and a link. Drill-in is
  a future polish item.

- **No typed data ports** in V1 — all data flows as dict. We won't add
  typed input/output schemas to workflow interfaces yet. Variables are untyped
  strings/dicts in the ExecutionContext.

- **No trigger marketplace/plugins** — triggers are built-in code, not
  dynamically loaded plugins. Each new trigger type requires a code change
  (but it's a small, well-scoped change).

- **No parallel sub-workflow execution** — sub-workflows execute sequentially.
  A node that fans out to multiple sub-workflows would execute them one at a time.
  Parallel execution is future work.

---

## 10. Questions for Alton

Before we start coding:

1. **Implementation order** — Does the phased order in Section 6 make sense?
   Would you prefer to swap any phases?

2. **Webhook vs. Manual as first new trigger** — Webhook requires adding an
   HTTP server to the Python agent. An even simpler "Manual" trigger could
   just be a button in the Admin Portal that lets you paste ticket data and
   run the workflow on demand. This would be useful for testing regardless.
   Which would you prefer as the first proof-of-concept trigger?

3. **Context sharing** — Shared context for sub-workflows (simpler, V1) vs.
   isolated context with explicit I/O mapping? I recommend shared for now.

4. **Agent-to-agent handoff** — You mentioned "possibly to another agent."
   Do you envision scenarios where a dispatcher agent literally sends work
   to a different agent process (true distributed), or is in-process
   sub-workflow execution sufficient? The in-process model is dramatically
   simpler and still achieves the "classify once, route to specialized logic"
   goal.

5. **For Jira testing** — Atlassian Cloud has a free tier that's easy to set up.
   We could also use a local Jira Data Center trial if you prefer to keep
   everything on-premises. But honestly, webhook + manual triggers might be
   enough to prove the architecture without Jira's complexity. Thoughts?

6. **Trigger configuration in designer** — When someone changes the Trigger
   node's type (ServiceNow → Jira), should the Step Properties panel show
   trigger-specific config fields? E.g., ServiceNow shows "Assignment Group",
   Jira shows "JQL Filter", Email shows "Mailbox". Or should all trigger-specific
   config stay on the Agent screen?
