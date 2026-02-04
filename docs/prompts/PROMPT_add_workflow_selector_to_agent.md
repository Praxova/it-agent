# Claude Code Prompt: Add Workflow Selector to Agent CRUD UI and API

## Context

The `Agent` entity already has `WorkflowDefinitionId` (Guid?) with a navigation property to
`WorkflowDefinition`. The seed data correctly links "helpdesk-password-reset-workflow" to 
"test-agent". The `AgentExportService` correctly includes the workflow in exports.

However, the Agent CRUD UI (Agents.razor / AgentDialog.razor) and the Agent API endpoints 
completely omit `WorkflowDefinitionId`. There is no way through the portal to assign, change,
or even see which workflow is linked to an agent. This is a UI/API plumbing fix only — 
NO database migration or entity changes needed.

## Files to Modify (4 files)

### 1. `admin/dotnet/src/LucidAdmin.Web/Models/AgentModels.cs`

Add `WorkflowDefinitionId` to all four types:

**`CreateAgentRequest`** — add parameter:
```csharp
[property: JsonPropertyName("workflow_definition_id")] Guid? WorkflowDefinitionId
```

**`UpdateAgentRequest`** — add parameter:
```csharp
[property: JsonPropertyName("workflow_definition_id")] Guid? WorkflowDefinitionId
```

**`AgentResponse`** — add TWO parameters:
```csharp
[property: JsonPropertyName("workflow_definition_id")] Guid? WorkflowDefinitionId,
[property: JsonPropertyName("workflow_name")] string? WorkflowName
```

**`AgentFormModel`** — add property and update all three methods:
```csharp
public Guid? WorkflowDefinitionId { get; set; }
```
- `ToCreateRequest()`: include `WorkflowDefinitionId: WorkflowDefinitionId`
- `ToUpdateRequest()`: include `WorkflowDefinitionId: WorkflowDefinitionId`
- `FromResponse()`: include `WorkflowDefinitionId = response.WorkflowDefinitionId`

### 2. `admin/dotnet/src/LucidAdmin.Web/Endpoints/AgentEndpoints.cs`

**POST (create)**: Add to the new Agent object:
```csharp
WorkflowDefinitionId = request.WorkflowDefinitionId,
```

**PUT (update)**: Add this line (use unconditional assignment so null can clear the field):
```csharp
agent.WorkflowDefinitionId = request.WorkflowDefinitionId;
```
Note: The existing LlmServiceAccountId/ServiceNowAccountId use `if (request.X.HasValue)` 
which prevents clearing to null. For WorkflowDefinitionId, always set it so workflows can 
be unassigned. This is intentionally different from the existing pattern.

**`MapToResponse` method**: Update to include the new fields:
```csharp
private static AgentResponse MapToResponse(Agent agent) => new(
    // ... existing fields ...
    WorkflowDefinitionId: agent.WorkflowDefinitionId,
    WorkflowName: agent.WorkflowDefinition?.Name
    // Note: WorkflowName will be null unless EF eager-loaded the navigation property.
    // That's fine — the UI will look up workflow names from its own preloaded list.
);
```

### 3. `admin/dotnet/src/LucidAdmin.Web/Components/Pages/AgentDialog.razor`

Add a **"Workflow" section** between the "ServiceNow Integration" section and the "Status" 
section. Follow the exact same pattern as the LLM and ServiceNow dropdowns.

**In the `<MudForm>` markup**, after the AssignmentGroup field and before the Status section:

```razor
<!-- WORKFLOW CONFIGURATION -->
<MudItem xs="12">
    <MudText Typo="Typo.h6" Class="mt-4 mb-2">Workflow</MudText>
</MudItem>

<MudItem xs="12">
    @if (_loadingWorkflows)
    {
        <MudProgressLinear Color="Color.Primary" Indeterminate="true" />
    }
    else if (_workflows.Count == 0)
    {
        <MudAlert Severity="Severity.Warning" Class="mb-2">
            No active workflows available. Create one in the Workflows page first.
        </MudAlert>
    }
    else
    {
        <MudSelect T="Guid?"
                   Label="Workflow Definition"
                   @bind-Value="_model.WorkflowDefinitionId"
                   Clearable="true"
                   HelperText="Select the workflow this agent will execute when processing tickets">
            @foreach (var wf in _workflows)
            {
                <MudSelectItem Value="@((Guid?)wf.Id)">
                    @(wf.DisplayName ?? wf.Name) (v@(wf.Version))
                </MudSelectItem>
            }
        </MudSelect>
    }
</MudItem>
```

**In the `@code` section**, add these fields alongside the existing service account fields:

```csharp
private List<WorkflowListItem> _workflows = new();
private bool _loadingWorkflows;
```

Add inner class (alongside the existing `ServiceAccountListItem`):
```csharp
private class WorkflowListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Version { get; set; } = "";
    public bool IsActive { get; set; }
}
```

Add method to load workflows using HttpClient (same pattern as `LoadServiceAccounts`). 
The workflow list endpoint is `GET /api/v1/workflows` and returns an array of objects with 
fields: `id` (Guid), `name` (string), `displayName` (string?), `version` (string), 
`isBuiltIn` (bool), `isActive` (bool), `stepCount` (int), plus dates.

```csharp
private async Task LoadWorkflows()
{
    _loadingWorkflows = true;
    try
    {
        var response = await Http.GetFromJsonAsync<List<WorkflowListItem>>("/api/v1/workflows");
        if (response != null)
        {
            _workflows = response
                .Where(w => w.IsActive)
                .OrderBy(w => w.DisplayName ?? w.Name)
                .ToList();
        }
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Failed to load workflows: {ex.Message}", Severity.Warning);
    }
    finally
    {
        _loadingWorkflows = false;
    }
}
```

NOTE: You will need to inject HttpClient into this dialog component. Add at the top:
```razor
@inject HttpClient Http
```

Update `OnInitializedAsync` to also load workflows:
```csharp
protected override async Task OnInitializedAsync()
{
    await Task.WhenAll(LoadServiceAccounts(), LoadWorkflows());

    if (ExistingAgent != null)
    {
        _model = AgentFormModel.FromResponse(ExistingAgent);
    }
}
```

**In the `Save()` method** — for UPDATE, add alongside the existing field assignments:
```csharp
agent.WorkflowDefinitionId = _model.WorkflowDefinitionId;
```

For CREATE, add to the new Agent object:
```csharp
WorkflowDefinitionId = _model.WorkflowDefinitionId,
```

### 4. `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Agents.razor`

**Add a "Workflow" column** to the table.

In `<HeaderContent>`, add after the ServiceNow column:
```razor
<MudTh>Workflow</MudTh>
```

In `<RowTemplate>`, add the corresponding cell after the ServiceNow cell:
```razor
<MudTd DataLabel="Workflow">
    @if (context.WorkflowDefinitionId.HasValue)
    {
        var wf = _workflows.FirstOrDefault(w => w.Id == context.WorkflowDefinitionId);
        if (wf != null)
        {
            <MudText Typo="Typo.body2">@(wf.DisplayName ?? wf.Name)</MudText>
            <MudText Typo="Typo.caption" Color="Color.Secondary">v@(wf.Version)</MudText>
        }
        else
        {
            <MudText Typo="Typo.caption" Color="Color.Warning">Unknown</MudText>
        }
    }
    else
    {
        <MudText Typo="Typo.caption" Color="Color.Warning">Not assigned</MudText>
    }
</MudTd>
```

**In `@code`**, add fields and loading method:

```csharp
private List<WorkflowListItem> _workflows = new();
```

Add inner class:
```csharp
private class WorkflowListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Version { get; set; } = "";
    public bool IsActive { get; set; }
}
```

Add load method:
```csharp
private async Task LoadWorkflows()
{
    try
    {
        var response = await Http.GetFromJsonAsync<List<WorkflowListItem>>("/api/v1/workflows");
        if (response != null)
        {
            _workflows = response;
        }
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Failed to load workflows: {ex.Message}", Severity.Warning);
    }
}
```

Update `OnInitializedAsync`:
```csharp
protected override async Task OnInitializedAsync()
{
    await Task.WhenAll(LoadAgents(), LoadServiceAccounts(), LoadWorkflows());
}
```

## Important Constraints

- Do NOT modify the Agent entity (`Core/Entities/Agent.cs`) — schema is already correct
- Do NOT create any database migration
- Do NOT modify `AgentExportService` — export already works correctly
- Do NOT modify `WorkflowEndpoints.cs` or workflow response models
- The workflow list API is at `GET /api/v1/workflows` (note the `v1` in the path)
- The `WorkflowListResponse` returned from that API uses PascalCase property names in the 
  record definition but System.Text.Json will serialize as camelCase by default. The 
  `WorkflowListItem` class used in the Blazor components should use PascalCase properties 
  since `GetFromJsonAsync` handles deserialization with case-insensitive matching by default.

## Verification Steps

After changes, rebuild and test:

```bash
# 1. Verify API returns workflow info
curl http://localhost:5000/api/agents | jq '.[0] | {name, workflow_definition_id, workflow_name}'

# 2. Verify export still works
curl http://localhost:5000/api/agents/by-name/test-agent/export | jq '.workflow.name'
# Expected: "helpdesk-password-reset-workflow"

# 3. In browser:
#    - Navigate to /agents
#    - Verify "Workflow" column shows "Helpdesk Password Reset Workflow" for test-agent
#    - Click Edit on test-agent
#    - Verify workflow dropdown shows the workflow pre-selected
#    - Create a new agent and verify you can assign a workflow
#    - Edit test-agent, clear the workflow, save, then re-assign it
```
