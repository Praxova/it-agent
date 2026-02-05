# Claude Code Prompt: Phase C2 — SubWorkflow Execution (Runtime)

## Context

This is Phase C2 of the ADR-011 implementation (Composable Workflows & Pluggable Triggers).

Previously completed:
- **T1**: Trigger provider abstraction (TriggerProvider, WorkItem, ServiceNowTriggerProvider)
- **C1**: SubWorkflow step type in the designer (StepType.SubWorkflow enum, node model/widget,
  sub-workflow picker in step properties, dynamic palette section)
- **T2**: Manual trigger implementation (ManualSubmission entity, portal-mediated queue,
  ManualTriggerProvider polls portal, Test Workflows page)

The goal of C2 is to make SubWorkflow steps actually execute at runtime. When the workflow
engine encounters a SubWorkflow step, it needs to:
1. Look up the referenced workflow definition
2. Create a child workflow engine
3. Execute the sub-workflow with shared context
4. Map the sub-workflow's terminal state (completed/escalated/failed) to output transitions
5. Prevent infinite recursion (A→B→A loops and excessive nesting depth)

## Architecture Summary

```
Parent Workflow Engine
  ├── Trigger step → ...
  ├── Classify step → ...
  ├── SubWorkflow step (e.g., "Password Reset")
  │     └── SubWorkflowExecutor
  │           ├── Fetch sub-workflow definition from export.sub_workflows
  │           ├── Create child WorkflowEngine with sub-workflow definition
  │           ├── Execute child engine with SAME ExecutionContext (shared)
  │           ├── Map child result: completed → "completed" output, escalated → "escalated"
  │           └── Recursion guard: check workflow_stack + depth limit
  ├── [completed path] → Next step...
  └── [escalated path] → Escalate step...
```

## Project Location
```
/home/alton/Documents/lucid-it-agent/
Admin Portal (C#): admin/dotnet/src/
Python Agent: agent/src/agent/
```

---

## Task 1: Add SubWorkflows to C# Export Response

### 1a. Update `AgentExportModels.cs`

In `admin/dotnet/src/LucidAdmin.Web/Models/AgentExportModels.cs`, add a `SubWorkflows`
field to `AgentExportResponse`:

```csharp
public record AgentExportResponse
{
    // ... existing fields ...
    public required List<string> RequiredCapabilities { get; init; }

    /// <summary>
    /// Sub-workflow definitions referenced by SubWorkflow steps.
    /// Key: workflow name, Value: complete workflow definition.
    /// </summary>
    public Dictionary<string, WorkflowExportInfo> SubWorkflows { get; init; } = new();
}
```

### 1b. Update `AgentExportService.cs`

In `admin/dotnet/src/LucidAdmin.Web/Services/AgentExportService.cs`, modify
`BuildExportResponse()` to collect sub-workflow definitions:

```csharp
private AgentExportResponse BuildExportResponse(Agent agent)
{
    var rulesets = CollectAllRulesets(agent);
    var exampleSets = CollectExampleSets(agent);
    var requiredCapabilities = CollectRequiredCapabilities(agent);
    var subWorkflows = CollectSubWorkflows(agent);  // NEW

    return new AgentExportResponse
    {
        // ... existing fields ...
        RequiredCapabilities = requiredCapabilities,
        SubWorkflows = subWorkflows  // NEW
    };
}
```

Add the `CollectSubWorkflows` method. It should:

1. Scan the main workflow's steps for `StepType.SubWorkflow` steps
2. For each SubWorkflow step, read its `ConfigurationJson` to get the referenced workflow ID
   (stored as `{"workflow_id": "guid", "workflow_name": "name"}` by the C1 designer work)
3. Load each referenced workflow definition from the database (with full includes: steps,
   transitions, rulesets, examples — same pattern as `LoadAgentWithAllRelationsAsync`)
4. Map each to `WorkflowExportInfo` using the existing `MapWorkflowInfo()` method
5. Return as `Dictionary<string, WorkflowExportInfo>` keyed by workflow name

```csharp
private Dictionary<string, WorkflowExportInfo> CollectSubWorkflows(Agent agent)
{
    var subWorkflows = new Dictionary<string, WorkflowExportInfo>();

    if (agent.WorkflowDefinition == null) return subWorkflows;

    foreach (var step in agent.WorkflowDefinition.Steps)
    {
        if (step.StepType != StepType.SubWorkflow) continue;
        if (string.IsNullOrEmpty(step.ConfigurationJson)) continue;

        var config = ParseConfigJsonAsObject(step.ConfigurationJson);
        if (config == null) continue;

        // Get referenced workflow ID from step config
        Guid? workflowId = null;
        if (config.TryGetValue("workflow_id", out var idObj) && idObj != null)
        {
            var idStr = idObj.ToString();
            if (Guid.TryParse(idStr, out var parsed))
                workflowId = parsed;
        }

        if (!workflowId.HasValue) continue;

        // Load the referenced workflow (we need to query DB for this)
        // NOTE: This requires making CollectSubWorkflows async, OR
        // pre-loading sub-workflows in LoadAgentWithAllRelationsAsync
        // See implementation notes below.
    }

    return subWorkflows;
}
```

**Important implementation note**: Since `BuildExportResponse` is currently synchronous,
the cleanest approach is to **pre-load sub-workflow definitions in `LoadAgentWithAllRelationsAsync`**
or make `BuildExportResponse` async. Choose whichever is cleaner. Options:

**Option A (recommended)**: Make `BuildExportResponse` async. Change its signature to
`async Task<AgentExportResponse>`, and inside `CollectSubWorkflows`, load sub-workflows
from the database using `_db.WorkflowDefinitions` with the same Include chain used for
the main workflow. Update callers (`ExportAgentAsync`) accordingly.

**Option B**: Pre-load sub-workflows in `LoadAgentWithAllRelationsAsync` by first loading
the agent, then scanning for SubWorkflow steps, then loading those workflow definitions
separately. Store them in a local variable and pass to `BuildExportResponse`.

Also include rulesets from sub-workflows in the main `Rulesets` dict, so sub-workflow
steps that use rulesets have them available at runtime.

### 1c. Handle Nested Sub-Workflows

If a sub-workflow itself contains SubWorkflow steps (nested composition), those should
also be included. Use a recursive approach with a visited set to prevent infinite loops:

```csharp
private async Task CollectSubWorkflowsRecursive(
    WorkflowDefinition workflow,
    Dictionary<string, WorkflowExportInfo> collected,
    HashSet<Guid> visited)
{
    if (visited.Contains(workflow.Id)) return;  // Already processed or circular
    visited.Add(workflow.Id);

    foreach (var step in workflow.Steps.Where(s => s.StepType == StepType.SubWorkflow))
    {
        // ... parse config, get workflow_id ...
        // Load from DB, add to collected
        // Recursively collect from that sub-workflow too
    }
}
```

---

## Task 2: Update Python Export Models

In `agent/src/agent/runtime/models.py`, add `sub_workflows` to `AgentExport`:

```python
class AgentExport(BaseModel):
    """Complete agent export - root model."""
    model_config = ConfigDict(populate_by_name=True)

    # ... existing fields ...
    required_capabilities: list[str] = Field(default_factory=list, alias="requiredCapabilities")

    # Sub-workflow definitions referenced by SubWorkflow steps
    # Key: workflow name, Value: workflow definition
    sub_workflows: dict[str, WorkflowExportInfo] = Field(
        default_factory=dict, alias="subWorkflows"
    )
```

This ensures backward compatibility — if the portal hasn't been updated yet, the field
defaults to an empty dict and existing behavior is preserved.

---

## Task 3: Add Recursion Tracking to ExecutionContext

In `agent/src/agent/runtime/execution_context.py`, add workflow stack tracking:

```python
@dataclass
class ExecutionContext:
    """Shared context during workflow execution."""
    # ... existing fields ...

    # Sub-workflow recursion tracking
    workflow_stack: list[str] = field(default_factory=list)

    # Maximum nesting depth for sub-workflows
    MAX_WORKFLOW_DEPTH: int = 10
```

---

## Task 4: Create SubWorkflowExecutor

Create `agent/src/agent/runtime/executors/sub_workflow.py`:

```python
"""Executor for SubWorkflow steps — runs a referenced workflow as a sub-step."""
from __future__ import annotations
import logging
from typing import Any, TYPE_CHECKING

from .base import BaseStepExecutor, StepExecutionError
from ..execution_context import ExecutionContext, ExecutionStatus, StepResult
from ..models import WorkflowStepExportInfo, RulesetExportInfo, WorkflowExportInfo, AgentExport

if TYPE_CHECKING:
    pass

logger = logging.getLogger(__name__)


class SubWorkflowExecutor(BaseStepExecutor):
    """
    Executes a referenced workflow as a sub-step.

    When the parent workflow engine encounters a SubWorkflow step, this executor:
    1. Looks up the referenced workflow from the export's sub_workflows dict
    2. Checks for circular references (workflow_stack)
    3. Creates a child WorkflowEngine with the sub-workflow definition
    4. Executes with SHARED context (same variables, same ticket data)
    5. Maps the child's terminal state to output for transition evaluation

    The output dict contains:
    - "outcome": "completed" | "escalated" | "failed"
    - "sub_workflow_name": name of the executed sub-workflow
    - Plus any other context set during sub-workflow execution
    """

    @property
    def step_type(self) -> str:
        return "SubWorkflow"

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        # Get sub-workflow reference from step configuration
        config = step.configuration or {}
        workflow_id = config.get("workflow_id")
        workflow_name = config.get("workflow_name", "unknown")

        if not workflow_id:
            result.fail("SubWorkflow step has no referenced workflow_id in configuration")
            return result

        logger.info(f"SubWorkflow step '{step.name}' referencing workflow '{workflow_name}'")

        # Check recursion: is this workflow already on the stack?
        if workflow_name in context.workflow_stack:
            chain = " → ".join(context.workflow_stack + [workflow_name])
            result.fail(f"Circular workflow reference detected: {chain}")
            return result

        # Check depth limit
        if len(context.workflow_stack) >= context.MAX_WORKFLOW_DEPTH:
            result.fail(
                f"Maximum sub-workflow nesting depth ({context.MAX_WORKFLOW_DEPTH}) exceeded. "
                f"Stack: {' → '.join(context.workflow_stack)}"
            )
            return result

        # Look up sub-workflow definition from export
        # The export's sub_workflows dict is accessible via... we need access to the export.
        # The WorkflowEngine holds the export. We need to pass it through context or
        # have the executor receive it another way.
        #
        # Approach: Store the full AgentExport on the context so executors can access it.
        # This is set by WorkflowEngine before execution begins.
        export: AgentExport | None = getattr(context, '_agent_export', None)
        if not export:
            result.fail("Agent export not available in context — cannot resolve sub-workflow")
            return result

        sub_workflow_def = export.sub_workflows.get(workflow_name)
        if not sub_workflow_def:
            # Also try by ID in case name doesn't match
            for name, wf in export.sub_workflows.items():
                if str(getattr(wf, 'id', '')) == str(workflow_id):
                    sub_workflow_def = wf
                    workflow_name = name
                    break

        if not sub_workflow_def:
            result.fail(
                f"Sub-workflow '{workflow_name}' (id={workflow_id}) not found in export. "
                f"Available: {list(export.sub_workflows.keys())}"
            )
            return result

        # Push onto recursion stack
        context.workflow_stack.append(workflow_name)

        try:
            # Create a child workflow engine
            # Import here to avoid circular imports
            from ..workflow_engine import WorkflowEngine

            # Build a temporary export with the sub-workflow as the main workflow
            child_export = AgentExport(
                version=export.version,
                exported_at=export.exported_at,
                agent=export.agent,
                llm_provider=export.llm_provider,
                service_now=export.service_now,
                workflow=sub_workflow_def,
                rulesets=rulesets,  # Pass through all available rulesets
                example_sets=export.example_sets,
                required_capabilities=export.required_capabilities,
                sub_workflows=export.sub_workflows,  # Pass through for nested sub-workflows
            )

            child_engine = WorkflowEngine(
                export=child_export,
                llm_driver=context.llm_driver,
                admin_portal_url=context.admin_portal_url,
            )

            # Share integration points
            child_engine.servicenow_client = context.servicenow_client
            child_engine.capability_router = context.capability_router

            # Execute sub-workflow with SHARED context
            # We call the engine's internal execution but reuse our context
            child_result = await child_engine.execute(
                ticket_id=context.ticket_id,
                ticket_data=context.ticket_data,
            )

            # Map child result to parent transition output
            if child_result.status == ExecutionStatus.COMPLETED:
                result.complete({
                    "outcome": "completed",
                    "sub_workflow_name": workflow_name,
                })
                logger.info(f"Sub-workflow '{workflow_name}' completed successfully")

            elif child_result.status == ExecutionStatus.ESCALATED:
                result.complete({
                    "outcome": "escalated",
                    "sub_workflow_name": workflow_name,
                    "escalation_reason": child_result.escalation_reason,
                })
                logger.info(f"Sub-workflow '{workflow_name}' escalated: {child_result.escalation_reason}")

            else:
                result.complete({
                    "outcome": "failed",
                    "sub_workflow_name": workflow_name,
                    "error": child_result.escalation_reason,
                })
                logger.warning(f"Sub-workflow '{workflow_name}' failed: {child_result.escalation_reason}")

        except Exception as e:
            logger.error(f"Sub-workflow '{workflow_name}' execution error: {e}", exc_info=True)
            result.fail(f"Sub-workflow execution error: {e}")

        finally:
            # Pop from recursion stack
            if context.workflow_stack and context.workflow_stack[-1] == workflow_name:
                context.workflow_stack.pop()

        return result
```

**Important design note on shared context**: The current `WorkflowEngine.execute()` creates
a NEW `ExecutionContext` internally. For sub-workflows, we need the child engine to share
the parent's context. There are two approaches:

**Approach A (simpler, recommended)**: Have `SubWorkflowExecutor` call `child_engine.execute()`
which creates a new context, but then merge key results back into the parent context. The
child's `variables` and `step_results` get merged into the parent after execution. This
keeps the engine's `execute()` method clean.

**Approach B**: Add an overload to `WorkflowEngine.execute()` that accepts an existing context:
```python
async def execute(self, ticket_id, ticket_data, context=None):
    if context is None:
        context = ExecutionContext(...)  # Create new (existing behavior)
    # ... rest of execution uses provided context
```

Go with **Approach B** — it's cleaner for shared context. Add an optional `context` parameter
to `WorkflowEngine.execute()`. When provided, skip creating a new context and use the
passed-in one. The sub-workflow executor passes the parent context through, so all variables
and step results accumulate in one place.

When using Approach B, update the `SubWorkflowExecutor` to pass the parent context:
```python
child_result = await child_engine.execute(
    ticket_id=context.ticket_id,
    ticket_data=context.ticket_data,
    context=context,  # SHARED context
)
```

---

## Task 5: Store AgentExport on Context

In `agent/src/agent/runtime/workflow_engine.py`, in the `execute()` method, store the
export on the context so executors (specifically SubWorkflowExecutor) can access it:

```python
async def execute(self, ticket_id, ticket_data, context=None):
    # ... create or reuse context ...

    # Store export reference for sub-workflow resolution
    context._agent_export = self.export

    # ... rest of execution ...
```

Also initialize the workflow_stack with the current workflow name if this is the
top-level execution (i.e., context was just created, not passed in):

```python
if not context.workflow_stack and self.export.workflow:
    context.workflow_stack.append(self.export.workflow.name)
```

---

## Task 6: Update WorkflowEngine.execute() for Shared Context

Modify `agent/src/agent/runtime/workflow_engine.py` `execute()` to accept an optional context:

```python
async def execute(
    self,
    ticket_id: str,
    ticket_data: dict[str, Any],
    context: ExecutionContext | None = None,
) -> ExecutionContext:
    """
    Execute workflow for a ticket.

    Args:
        ticket_id: ID of the ticket being processed
        ticket_data: Ticket fields
        context: Optional existing context (for sub-workflow shared context).
                 If None, creates a new context.
    """
    if not self.export.workflow:
        raise WorkflowExecutionError("No workflow defined in agent export")

    # Create or reuse execution context
    if context is None:
        context = ExecutionContext(
            ticket_id=ticket_id,
            ticket_data=ticket_data,
            llm_driver=self.llm_driver,
            admin_portal_url=self.admin_portal_url,
        )
        context.servicenow_client = self.servicenow_client
        context.capability_router = self.capability_router
        context.status = ExecutionStatus.RUNNING

        # Initialize workflow stack for top-level workflow
        if self.export.workflow:
            context.workflow_stack.append(self.export.workflow.name)
    else:
        # Sub-workflow: context already has status RUNNING, integrations set
        pass

    # Store export for sub-workflow resolution
    context._agent_export = self.export

    # ... rest of existing execute() logic unchanged ...
```

The rest of the step execution loop, terminal state handling, and transition evaluation
remains the same. The key change is just the context creation/reuse at the top.

**Critical**: When the engine is executing a sub-workflow (context was passed in), the
terminal state handling needs to be slightly different. When the sub-workflow hits End
or Escalate, it should NOT set context.status to COMPLETED/ESCALATED on the shared
context — that would prematurely end the parent workflow. Instead:

- The `execute()` return value conveys the sub-workflow's terminal state
- The SubWorkflowExecutor reads the return value and maps it to step output
- The parent workflow continues executing based on transition conditions

So for sub-workflow execution, we need a way to track the sub-workflow's own terminal state
separately. The cleanest approach: have `execute()` return a lightweight result when running
as a sub-workflow, without modifying the shared context's overall status.

Add a flag to detect sub-workflow mode:
```python
is_sub_workflow = context is not None  # True if context was passed in (shared)
```

Then in the terminal state handling:
```python
if current_step.step_type == StepType.END:
    if is_sub_workflow:
        # Don't modify shared context status — just return with completed info
        context._sub_workflow_status = ExecutionStatus.COMPLETED
    else:
        context.complete()
    break

if current_step.step_type == StepType.ESCALATE:
    if is_sub_workflow:
        context._sub_workflow_status = ExecutionStatus.ESCALATED
        context._sub_workflow_escalation = result.output.get("reason", "Escalated")
    else:
        context.escalate(result.output.get("reason", "Escalated by workflow"))
    break
```

And update SubWorkflowExecutor to read `context._sub_workflow_status` instead of
`child_result.status` for determining the outcome.

---

## Task 7: Register SubWorkflowExecutor

In `agent/src/agent/runtime/executors/registry.py`:

1. Import SubWorkflowExecutor:
```python
from .sub_workflow import SubWorkflowExecutor
```

2. Add to the builtins list in `_register_builtins()`:
```python
builtins = [
    TriggerExecutor(),
    ClassifyExecutor(),
    QueryExecutor(),
    ValidateExecutor(),
    ExecuteExecutor(),
    UpdateTicketExecutor(),
    NotifyExecutor(),
    EscalateExecutor(),
    EndExecutor(),
    SubWorkflowExecutor(),  # NEW
]
```

Also add it to `__init__.py` if executors are re-exported there.

---

## Task 8: Add `id` Field to Python WorkflowExportInfo

The Python `WorkflowExportInfo` model in `models.py` currently doesn't have an `id` field,
but the C# export includes it. Add it for sub-workflow ID matching:

```python
class WorkflowExportInfo(BaseModel):
    """Complete workflow definition."""
    model_config = ConfigDict(populate_by_name=True)

    id: str | None = None  # NEW — workflow GUID from portal
    name: str
    # ... rest unchanged ...
```

---

## Task 9: Verify Build (Both Sides)

### C# Admin Portal:
```bash
cd admin/dotnet
dotnet build
```
Ensure 0 errors.

### Python Agent:
```bash
cd agent
python -c "from agent.runtime.executors.sub_workflow import SubWorkflowExecutor; print('OK')"
python -c "from agent.runtime.executors.registry import default_registry; print(list(default_registry.get_all().keys()))"
```
The second command should show `SubWorkflow` in the list of registered executors.

---

## Task 10: Update Exports and Verify Integration

Run a quick smoke test to verify the export includes sub-workflows:

1. Start the Admin Portal
2. Create or use an existing workflow that has a SubWorkflow step referencing another workflow
3. Assign that workflow to an agent
4. Call the export endpoint:
```bash
curl http://localhost:5000/api/agents/by-name/test-agent/export | python -m json.tool
```
5. Verify the response has a `subWorkflows` key containing the referenced workflow definition

If no workflow currently has SubWorkflow steps (likely), just verify the export has
`"subWorkflows": {}` and the build succeeds. Actual end-to-end testing with a dispatcher
workflow happens in Phase C3.

---

## Commit Message

```
feat: sub-workflow execution engine (Phase C2)

- Add SubWorkflows dict to C# agent export (AgentExportResponse)
- Collect referenced sub-workflow definitions in AgentExportService
- Handle nested sub-workflows with recursive collection + visited set
- Add sub_workflows field to Python AgentExport model
- Add workflow_stack recursion tracking to ExecutionContext
- Create SubWorkflowExecutor with shared context execution
- Support optional context parameter in WorkflowEngine.execute()
- Handle sub-workflow terminal states without corrupting parent context
- Register SubWorkflowExecutor in executor registry
- Add id field to Python WorkflowExportInfo for sub-workflow matching
```

## Design Decisions Recap

1. **Shared context**: Sub-workflows share the parent's ExecutionContext. Variables and
   step results accumulate in one place. This matches the ADR-011 decision.

2. **Recursion prevention**: Two guards — workflow_stack prevents A→B→A loops,
   MAX_WORKFLOW_DEPTH (10) prevents excessive nesting.

3. **Sub-workflow terminal states**: Sub-workflow End/Escalate don't modify the shared
   context's status. They set internal flags that SubWorkflowExecutor reads to produce
   the right output for transition evaluation.

4. **Export includes sub-workflows**: The C# export service recursively collects all
   referenced workflow definitions, so the Python agent has everything it needs without
   additional API calls at runtime.
