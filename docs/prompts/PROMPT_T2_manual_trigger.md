# Claude Code Prompt: Phase T2 — Manual Trigger Implementation

## Context

This is Phase T2 of the ADR-011 implementation (Composable Workflows & Pluggable Triggers).
Phase T1 (trigger abstraction) and Phase C1 (SubWorkflow designer UI) are both complete.

The trigger abstraction created in T1 includes:
- `TriggerProvider` abstract base class at `agent/src/agent/runtime/triggers/base.py`
- `ManualTriggerProvider` stub at `agent/src/agent/runtime/triggers/manual_provider.py`
- `TriggerProviderFactory` at `agent/src/agent/runtime/triggers/registry.py`
- `WorkItem` dataclass for normalized work items
- `AgentRunner` already uses `self._trigger.poll()` → `_process_work_item()`

The goal of T2 is to make the Manual trigger fully functional as the first new trigger
type, proving the abstraction works. It doubles as a workflow testing tool.

## Architecture Decision: Portal-Mediated Queue

The Python agent has NO HTTP server — it's a polling CLI. Rather than adding FastAPI/uvicorn,
manual submissions flow through the Admin Portal database:

```
Admin Portal UI        Admin Portal API         Agent (Python)
┌──────────┐     POST  ┌──────────────────┐    GET  ┌──────────────────┐
│ Submit    │ ────────→ │ /api/manual-     │ ←───── │ ManualTrigger    │
│ Form      │          │  submissions     │        │  Provider.poll() │
└──────────┘          │                  │ PATCH  │                  │
                       │ SQLite/SQL Server│ ──────→│ acknowledge()    │
┌──────────┐     GET   │                  │ PATCH  │ complete()       │
│ Results   │ ←─────── │                  │ ──────→│ fail()           │
│ History   │          └──────────────────┘        └──────────────────┘
└──────────┘
```

This means:
- No new Python dependencies
- Uses existing httpx for outbound API calls
- Results visible in the portal UI
- Same communication pattern as config loading + heartbeat

## Project Location
```
/home/alton/Documents/lucid-it-agent/
Admin Portal: admin/dotnet/src/
Python Agent: agent/src/agent/
```

---

## Task 1: ManualSubmission Entity (Admin Portal)

Create `admin/dotnet/src/LucidAdmin.Core/Entities/ManualSubmission.cs`:

```csharp
public class ManualSubmission : BaseEntity
{
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    // Work item data
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Requester { get; set; }
    public string? ExtraDataJson { get; set; }  // Additional fields as JSON

    // Status tracking
    public ManualSubmissionStatus Status { get; set; } = ManualSubmissionStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickedUpAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Result data (populated by agent after execution)
    public string? ResultStatus { get; set; }     // "completed", "escalated", "failed"
    public string? ResultMessage { get; set; }     // Summary of what happened
    public string? ResultDetailsJson { get; set; } // Full step results as JSON
}

public enum ManualSubmissionStatus
{
    Pending = 0,      // Waiting for agent to pick up
    InProgress = 1,   // Agent acknowledged, executing
    Completed = 2,    // Workflow finished successfully
    Escalated = 3,    // Workflow escalated to human
    Failed = 4        // Workflow execution failed
}
```

## Task 2: EF Core Configuration

Create `admin/dotnet/src/LucidAdmin.Infrastructure/Data/Configurations/ManualSubmissionConfiguration.cs`:

Follow the pattern of existing configurations (e.g., `AgentConfiguration.cs`).

Key configuration:
- `HasOne(e => e.Agent).WithMany().HasForeignKey(e => e.AgentId)`
- Index on `AgentId` + `Status` (for efficient pending-item queries)
- Index on `SubmittedAt` (for history queries)

Add `DbSet<ManualSubmission> ManualSubmissions` to `LucidDbContext.cs`.

## Task 3: ManualSubmission API Endpoints (Admin Portal)

Create `admin/dotnet/src/LucidAdmin.Web/Endpoints/ManualSubmissionEndpoints.cs`.

Follow the pattern of existing endpoint files (e.g., `AgentEndpoints.cs`).

### Endpoints:

**POST /api/manual-submissions** — Submit a new manual work item
```json
// Request:
{
  "agentId": "guid",           // OR "agentName": "test-agent"
  "title": "Password reset for Luke Skywalker",
  "description": "User locked out, needs password reset",
  "requester": "luke.skywalker",
  "extraData": {               // Optional additional fields
    "caller_id": "luke.skywalker",
    "category": "Access",
    "subcategory": "Password Reset"
  }
}
// Response: 201 Created with the ManualSubmission object
```

Support both `agentId` (GUID) and `agentName` (string) for convenience.
If `agentName` is provided, look up the agent by name.

**GET /api/agents/{agentId}/manual-submissions/pending** — Get pending items for an agent
```json
// Response:
[
  {
    "id": "guid",
    "title": "Password reset for Luke Skywalker",
    "description": "User locked out...",
    "requester": "luke.skywalker",
    "extraData": { ... },
    "submittedAt": "2026-02-05T12:00:00Z"
  }
]
```

Also support by-name: **GET /api/agents/by-name/{agentName}/manual-submissions/pending**

**PATCH /api/manual-submissions/{id}/acknowledge** — Mark as picked up
Sets Status = InProgress, PickedUpAt = now.

**PATCH /api/manual-submissions/{id}/result** — Report execution result
```json
// Request:
{
  "status": "completed",         // "completed", "escalated", "failed"
  "message": "Password reset successful for luke.skywalker",
  "details": { ... }             // Optional step results
}
```
Sets Status based on result, CompletedAt = now, populates result fields.

**GET /api/manual-submissions** — List all submissions (with optional filters)
Query params: `agentId`, `agentName`, `status`, `limit` (default 50)
Returns most recent first.

**GET /api/manual-submissions/{id}** — Get a specific submission with full details

Register these endpoints in `Program.cs` following existing patterns.

## Task 4: Update ManualTriggerProvider (Python Agent)

Rewrite `agent/src/agent/runtime/triggers/manual_provider.py` to poll the Admin Portal:

```python
class ManualTriggerProvider(TriggerProvider):
    """
    Trigger provider for manually submitted work items.

    Polls the Admin Portal for pending manual submissions,
    executes them through the workflow, and reports results back.
    """

    def __init__(self, admin_portal_url: str, agent_name: str):
        self._admin_portal_url = admin_portal_url.rstrip("/")
        self._agent_name = agent_name

    @property
    def trigger_type(self) -> TriggerType:
        return TriggerType.MANUAL

    @property
    def display_name(self) -> str:
        return "Manual Trigger"

    async def poll(self) -> list[WorkItem]:
        """Fetch pending manual submissions from Admin Portal."""
        url = f"{self._admin_portal_url}/api/agents/by-name/{self._agent_name}/manual-submissions/pending"
        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(url, timeout=10.0)
                if response.status_code == 404:
                    return []  # Endpoint not available yet, no items
                response.raise_for_status()
                items_data = response.json()
                return [self._to_work_item(item) for item in items_data]
        except Exception as e:
            logger.debug(f"Manual submission poll failed: {e}")
            return []

    def _to_work_item(self, data: dict) -> WorkItem:
        """Convert portal submission to WorkItem."""
        submission_id = data["id"]
        extra = data.get("extraData") or data.get("extra_data") or {}

        # Build ticket_data dict matching ServiceNow-like shape
        # so existing workflow steps work without modification
        ticket_data = {
            "short_description": data.get("title", ""),
            "description": data.get("description", ""),
            "caller_id": data.get("requester") or extra.get("caller_id", "manual"),
            "number": f"MANUAL-{submission_id[:8].upper()}",
            "sys_id": submission_id,
            **extra,  # Merge any extra fields
        }

        return WorkItem(
            id=submission_id,
            source_type=TriggerType.MANUAL,
            data=ticket_data,
            title=data.get("title", ""),
            description=data.get("description", ""),
            requester=data.get("requester"),
            display_id=f"MANUAL-{submission_id[:8].upper()}",
        )

    async def acknowledge(self, item: WorkItem) -> None:
        """Mark submission as picked up in portal."""
        url = f"{self._admin_portal_url}/api/manual-submissions/{item.id}/acknowledge"
        try:
            async with httpx.AsyncClient() as client:
                await client.patch(url, timeout=10.0)
        except Exception as e:
            logger.warning(f"Failed to acknowledge manual item {item.display_id}: {e}")

    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """Report successful completion to portal."""
        await self._report_result(item, "completed", self._build_message(context))

    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """Report escalation to portal."""
        await self._report_result(
            item, "escalated",
            f"Escalated: {context.escalation_reason}"
        )

    async def fail(self, item: WorkItem, error: str) -> None:
        """Report failure to portal."""
        await self._report_result(item, "failed", f"Failed: {error}")

    async def _report_result(self, item: WorkItem, status: str, message: str) -> None:
        url = f"{self._admin_portal_url}/api/manual-submissions/{item.id}/result"
        try:
            async with httpx.AsyncClient() as client:
                await client.patch(url, json={
                    "status": status,
                    "message": message,
                }, timeout=10.0)
        except Exception as e:
            logger.warning(f"Failed to report result for {item.display_id}: {e}")

    def _build_message(self, context: ExecutionContext) -> str:
        """Build a human-readable result message from execution context."""
        completed_steps = [
            name for name, result in context.step_results.items()
            if result.status == ExecutionStatus.COMPLETED
        ]
        return f"Completed successfully. Steps executed: {', '.join(completed_steps)}"
```

## Task 5: Update TriggerProviderFactory

In `agent/src/agent/runtime/triggers/registry.py`, update the Manual case:

```python
elif trigger_type == TriggerType.MANUAL:
    admin_portal_url = kwargs.get("admin_portal_url", "")
    agent_name = kwargs.get("agent_name", "")
    if not admin_portal_url or not agent_name:
        raise ValueError("Manual trigger requires admin_portal_url and agent_name")
    return ManualTriggerProvider(
        admin_portal_url=admin_portal_url,
        agent_name=agent_name,
    )
```

## Task 6: Update AgentRunner Initialization

In `agent/src/agent/runtime/runner.py`, update the `TriggerProviderFactory.create()` call
in `initialize()` to pass the additional kwargs needed by ManualTriggerProvider:

```python
# Create trigger provider
self._trigger = TriggerProviderFactory.create(
    trigger_type=trigger_type,
    snow_client=self._snow_client,
    assignment_group=assignment_group,
    admin_portal_url=self.admin_portal_url,    # NEW
    agent_name=self.agent_name,                 # NEW
)
```

This is backward-compatible — ServiceNowTriggerProvider ignores these kwargs.

## Task 7: Add "manual" to Designer Trigger Type Options

In the Admin Portal designer, ensure the Trigger step's source/type dropdown includes "manual"
as an option. Check `Designer.razor` for where trigger type options are rendered.

Look for where trigger types are defined (likely a list or switch statement that includes
"servicenow") and add "manual" alongside it. The Trigger step's config should store
`{"source": "manual"}` when selected.

Also check `WorkflowEndpoints.cs` for any trigger type metadata that needs updating.

## Task 8: ManualSubmissions Razor Page (Admin Portal UI)

Create a simple Blazor page at `admin/dotnet/src/LucidAdmin.Web/Components/Pages/ManualSubmissions.razor`.

Follow the pattern of existing pages (e.g., `Agents.razor`).

### Layout:
```
┌─────────────────────────────────────────────────────────────────┐
│  Manual Submissions                                    [+ New]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Submit New Work Item                                            │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Agent:       [test-agent ▼]                              │   │
│  │ Title:       [____________________________________]      │   │
│  │ Description: [____________________________________]      │   │
│  │              [____________________________________]      │   │
│  │ Requester:   [____________________________________]      │   │
│  │                                          [Submit]        │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Recent Submissions                                              │
│  ┌────────┬────────────────────┬───────────┬────────┬────────┐  │
│  │ ID     │ Title              │ Agent     │ Status │ Time   │  │
│  ├────────┼────────────────────┼───────────┼────────┼────────┤  │
│  │ MAN-A3 │ Password reset...  │ test-agent│ ✅ Done│ 2m ago │  │
│  │ MAN-B7 │ Add to group...    │ test-agent│ ⏳ Run │ 5m ago │  │
│  └────────┴────────────────────┴───────────┴────────┴────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

Features:
- Agent dropdown populated from GET /api/agents
- Submit button POSTs to /api/manual-submissions
- Results table auto-refreshes every 5 seconds (simple timer + StateHasChanged)
- Status column shows badges: Pending (yellow), InProgress (blue), Completed (green),
  Escalated (orange), Failed (red)
- Click a row to see full result details (ResultMessage, ResultDetailsJson)

Add navigation entry in `NavMenu.razor` between Agents and Workflows:
```razor
<MudNavLink Href="/manual-submissions" Icon="@Icons.Material.Filled.PlayArrow">
    Test Workflows
</MudNavLink>
```

Use "Test Workflows" as the nav label since that's the primary use case.

## Task 9: EF Core Migration

After all entity changes, create the migration:

```bash
cd admin/dotnet/src/LucidAdmin.Web
dotnet ef migrations add AddManualSubmissions \
  --project ../LucidAdmin.Infrastructure/LucidAdmin.Infrastructure.csproj
```

If the project uses auto-migration on startup, verify that works. Otherwise note that
the migration needs to be applied.

## Task 10: Verify Build

```bash
cd admin/dotnet
dotnet build
```

Ensure 0 errors. Fix any issues.

## Commit Message

```
feat: manual trigger implementation (Phase T2)

- Add ManualSubmission entity with status tracking and result storage
- Add portal API endpoints for submit/poll/acknowledge/result
- Update ManualTriggerProvider to poll portal for pending items
- Update TriggerProviderFactory with portal URL/agent name params
- Add "manual" trigger type option in workflow designer
- Add Test Workflows page for manual submission and result viewing
- Add EF Core migration for ManualSubmissions table
```

## Testing Notes

After implementation, test with:

1. Start the Admin Portal: `cd admin/dotnet && dotnet run --project src/LucidAdmin.Web`
2. Navigate to "Test Workflows" page
3. Select an agent, fill in title/description, submit
4. Verify the submission appears in the table as "Pending"
5. Start the agent: `cd agent && python -m agent.runtime.cli --agent test-agent`
6. Watch the agent pick up the manual submission on next poll cycle
7. Verify status updates to "InProgress" then "Completed"/"Escalated"/"Failed"
8. Check result details in the portal

For quick API testing without the agent running:
```bash
# Submit
curl -X POST http://localhost:5000/api/manual-submissions \
  -H "Content-Type: application/json" \
  -d '{"agentName":"test-agent","title":"Test password reset","description":"Reset password for luke.skywalker","requester":"luke.skywalker"}'

# Check pending
curl http://localhost:5000/api/agents/by-name/test-agent/manual-submissions/pending

# Check all
curl http://localhost:5000/api/manual-submissions?agentName=test-agent
```
