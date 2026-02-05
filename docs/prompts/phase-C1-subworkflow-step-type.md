# Claude Code Prompt: Phase C1 — SubWorkflow Step Type in Designer

## Context

Read `/home/alton/Documents/lucid-it-agent/docs/adr/ADR-011-composable-workflows-pluggable-triggers.md` for the full architecture vision (especially Section 4: Composable Workflows). This is Phase C1: adding SubWorkflow as a new step type in the visual workflow designer.

**This phase is designer-only.** No runtime/execution changes. The SubWorkflow step type will be visible and configurable in the designer, but won't execute until Phase C2.

**Project Location**: `/home/alton/Documents/lucid-it-agent`
**Admin Portal Location**: `/home/alton/Documents/lucid-it-agent/admin/dotnet`

## What Exists Today

The designer uses Z.Blazor.Diagrams with:
- `StepType` enum in `LucidAdmin.Core/Enums/StepType.cs` — 10 values (Trigger through End)
- `WorkflowNodeModel` in `Components/WorkflowDesigner/Models/` — creates ports based on StepType
- `WorkflowNodeWidget.razor` in `Components/WorkflowDesigner/Widgets/` — renders nodes with icons, colors, port labels
- `Designer.razor` — main page with Step Palette (click to add), Step Properties panel, and canvas
- `WorkflowEndpoints.cs` — `/api/v1/workflows/step-types` returns metadata for each step type
- Step Properties panel already has type-specific config fields (Execute → Capability, Condition → Condition expression, Notify → Notify type)
- The existing `/api/v1/workflows` GET endpoint already returns a list of all workflows with Id, Name, DisplayName, IsActive

The Python agent also has a `StepType` enum in `agent/src/agent/runtime/models.py` that must stay in sync.

## Goal

Add `SubWorkflow` as a new step type that:
1. Appears in the Step Palette with a divider separating it from the standard step types
2. Has a distinctive visual appearance (teal/purple header color, 📋 icon)
3. Has two output ports: "Completed" and "Escalated"
4. Shows a workflow picker dropdown in the Step Properties panel when selected
5. Stores the selected workflow reference in ConfigurationJson
6. Also adds a dynamic "Sub-Workflows" quick-add section in the palette that lists active workflows

## Task 1: Add SubWorkflow to StepType Enum

**File**: `admin/dotnet/src/LucidAdmin.Core/Enums/StepType.cs`

Add after `End`:
```csharp
    /// <summary>End of workflow path.</summary>
    End,

    /// <summary>Execute another workflow as a sub-step.</summary>
    SubWorkflow
```

## Task 2: Add SubWorkflow to Step Types API

**File**: `admin/dotnet/src/LucidAdmin.Web/Endpoints/WorkflowEndpoints.cs`

Add to the step types list in the `/step-types` endpoint:
```csharp
new(StepType.SubWorkflow, "Sub-Workflow", "Execute another workflow as a sub-step", "📋", "#009688", 1, 2)
```

The color `#009688` is teal — visually distinct from all other step types.

## Task 3: Update WorkflowNodeModel for SubWorkflow Ports

**File**: `admin/dotnet/src/LucidAdmin.Web/Components/WorkflowDesigner/Models/WorkflowNodeModel.cs`

Add SubWorkflow to the `CreatePorts()` switch statement. SubWorkflow nodes have:
- One input port (Exec In) — already handled by the default `StepType != Trigger` check
- Two output ports: "completed" and "escalated"

Also add a property to hold the referenced workflow info:

```csharp
// Add these properties to the class:
public Guid? ReferencedWorkflowId { get; set; }
public string? ReferencedWorkflowName { get; set; }

// Add this case to the switch in CreatePorts():
case StepType.SubWorkflow:
    OutputPorts["completed"] = AddPort(PortAlignment.Right);
    OutputPorts["escalated"] = AddPort(PortAlignment.Right);
    break;
```

Also, when loading from saved data, parse the ConfigurationJson to populate these properties. Add a method:

```csharp
/// <summary>
/// Load sub-workflow reference from configuration JSON.
/// Called after setting ConfigurationJson when loading saved workflows.
/// </summary>
public void LoadSubWorkflowConfig()
{
    if (StepType != StepType.SubWorkflow || string.IsNullOrEmpty(ConfigurationJson))
        return;

    try
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(ConfigurationJson);
        if (config != null)
        {
            if (config.TryGetValue("workflow_id", out var wfId) && Guid.TryParse(wfId.GetString(), out var guid))
                ReferencedWorkflowId = guid;
            if (config.TryGetValue("workflow_name", out var wfName))
                ReferencedWorkflowName = wfName.GetString();
        }
    }
    catch { /* ignore malformed config */ }
}
```

## Task 4: Update WorkflowNodeWidget for SubWorkflow Rendering

**File**: `admin/dotnet/src/LucidAdmin.Web/Components/WorkflowDesigner/Widgets/WorkflowNodeWidget.razor`

Add SubWorkflow cases to the helper methods:

```csharp
// In GetColor():
StepType.SubWorkflow => "#009688",

// In GetIcon():
StepType.SubWorkflow => "📋",

// In GetPortLabel():
"completed" => "Completed",
"escalated" => "Escalated",
```

Also add a visual indicator in the node body when a workflow is referenced. Below the `workflow-node-type` div, add:

```razor
@if (Node.StepType == StepType.SubWorkflow)
{
    <div class="workflow-node-subref">
        @if (!string.IsNullOrEmpty(Node.ReferencedWorkflowName))
        {
            <span class="subref-label">▸ @Node.ReferencedWorkflowName</span>
        }
        else
        {
            <span class="subref-empty">No workflow selected</span>
        }
    </div>
}
```

## Task 5: Add CSS for SubWorkflow Visual Elements

**File**: `admin/dotnet/src/LucidAdmin.Web/wwwroot/css/workflow-designer.css`

Append these styles:

```css
/* Sub-workflow reference indicator */
.workflow-node-subref {
    margin-top: 4px;
    padding-top: 4px;
    border-top: 1px solid rgba(255,255,255,0.1);
}

.subref-label {
    font-size: 0.75em;
    color: #4DB6AC;
    font-style: italic;
}

.subref-empty {
    font-size: 0.7em;
    color: #666;
    font-style: italic;
}
```

## Task 6: Add SubWorkflow Configuration to Step Properties Panel

**File**: `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Workflows/Designer.razor`

This is the most significant change. We need to:

### 6a: Add state variables for workflow list and sub-workflow config

In the `@code` block, add:

```csharp
// Available workflows for sub-workflow picker (loaded on init)
private List<WorkflowListResponse> _availableWorkflows = new();

// Sub-workflow config fields
private Guid? _stepConfig_SubWorkflowId;
private string? _stepConfig_SubWorkflowName;
```

### 6b: Load available workflows on init

In `OnInitializedAsync()`, add after loading example sets:

```csharp
// Load available workflows (for sub-workflow picker)
_availableWorkflows = await Http.GetFromJsonAsync<List<WorkflowListResponse>>("/api/v1/workflows") ?? new();
```

### 6c: Add SubWorkflow picker to the Step Properties panel

In the `@switch (_selectedStep.StepType)` block inside the Step Properties expansion panel, add a case:

```razor
case StepType.SubWorkflow:
    <MudSelect @bind-Value="_stepConfig_SubWorkflowId" Label="Target Workflow" Class="mb-2"
               T="Guid?" Clearable="true"
               HelperText="Select the workflow to execute as a sub-step">
        @foreach (var wf in _availableWorkflows.Where(w => w.IsActive && w.Id != Id))
        {
            <MudSelectItem T="Guid?" Value="@((Guid?)wf.Id)">
                @(wf.DisplayName ?? wf.Name)
            </MudSelectItem>
        }
    </MudSelect>
    @if (_stepConfig_SubWorkflowId.HasValue)
    {
        var selectedWf = _availableWorkflows.FirstOrDefault(w => w.Id == _stepConfig_SubWorkflowId);
        if (selectedWf != null)
        {
            <MudText Typo="Typo.caption" Class="mb-1" Style="color: #888;">
                @(selectedWf.Description ?? "No description")
            </MudText>
            <MudText Typo="Typo.caption" Style="color: #666;">
                Steps: @selectedWf.StepCount | Version: @selectedWf.Version
            </MudText>
        }
    }
    break;
```

**IMPORTANT**: The filter `w.Id != Id` prevents a workflow from referencing itself (immediate circular reference). Full recursion detection happens at runtime in Phase C2, but we prevent the obvious case in the UI.

### 6d: Update LoadStepConfig to handle SubWorkflow

In the `LoadStepConfig()` method, add parsing for sub-workflow config:

```csharp
// Add after existing config parsing:
if (_selectedStep.StepType == StepType.SubWorkflow && _selectedStep.ConfigurationJson is not null)
{
    try
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(_selectedStep.ConfigurationJson);
        if (config != null)
        {
            if (config.TryGetValue("workflow_id", out var wfId) && Guid.TryParse(wfId.GetString(), out var guid))
                _stepConfig_SubWorkflowId = guid;
            if (config.TryGetValue("workflow_name", out var wfName))
                _stepConfig_SubWorkflowName = wfName.GetString();
        }
    }
    catch { }
}
else
{
    _stepConfig_SubWorkflowId = null;
    _stepConfig_SubWorkflowName = null;
}
```

NOTE: You'll need to add `using System.Text.Json;` at the top of the file if not already present. The existing `LoadStepConfig` uses `JsonSerializer.Deserialize<Dictionary<string, string>>` which won't work for the sub-workflow config since the value types differ. You may need to adjust the existing deserialization to use `JsonElement` values instead of `string` values, or handle SubWorkflow config separately before the existing try/catch.

### 6e: Update UpdateSelectedStepConfig to save SubWorkflow config

In the `UpdateSelectedStepConfig()` method, add a case for SubWorkflow:

```csharp
case StepType.SubWorkflow when _stepConfig_SubWorkflowId.HasValue:
    var subWf = _availableWorkflows.FirstOrDefault(w => w.Id == _stepConfig_SubWorkflowId);
    if (subWf != null)
    {
        config["workflow_id"] = subWf.Id.ToString();
        config["workflow_name"] = subWf.Name;
    }
    break;
```

### 6f: Update the node model when sub-workflow selection changes

When the user picks a workflow from the dropdown, the node on the canvas should update to show the selected workflow name. Add a handler that watches for `_stepConfig_SubWorkflowId` changes.

The simplest approach: When saving step config (UpdateSelectedStepConfig), also update the node model:

```csharp
// After updating config for SubWorkflow, also update the node visual:
if (_selectedStep?.StepType == StepType.SubWorkflow)
{
    var node = _diagram.Nodes.OfType<WorkflowNodeModel>()
        .FirstOrDefault(n => n.StepName == _selectedStep.Name);
    if (node != null)
    {
        node.ReferencedWorkflowId = _stepConfig_SubWorkflowId;
        node.ReferencedWorkflowName = subWf?.Name;
        node.Refresh();
    }
}
```

However, this only updates on save. For immediate visual feedback, you should also update the node when the dropdown value changes. Add a method:

```csharp
private void OnSubWorkflowChanged(Guid? newValue)
{
    _stepConfig_SubWorkflowId = newValue;
    
    // Update node visual immediately
    if (_selectedStep != null)
    {
        var node = _diagram.Nodes.OfType<WorkflowNodeModel>()
            .FirstOrDefault(n => n.StepName == _selectedStep.Name);
        if (node != null)
        {
            var wf = _availableWorkflows.FirstOrDefault(w => w.Id == newValue);
            node.ReferencedWorkflowId = newValue;
            node.ReferencedWorkflowName = wf?.Name;
            node.Refresh();
        }
    }
}
```

Then change the MudSelect to use this handler instead of `@bind-Value`:
```razor
<MudSelect Value="_stepConfig_SubWorkflowId" 
           ValueChanged="@((Guid? val) => OnSubWorkflowChanged(val))"
           Label="Target Workflow" Class="mb-2"
           T="Guid?" Clearable="true"
           HelperText="Select the workflow to execute as a sub-step">
```

## Task 7: Add Dynamic Sub-Workflows Section to Step Palette

In `Designer.razor`, add a "Sub-Workflows" section below the existing step palette. This gives users a quick way to add a pre-configured SubWorkflow node for any active workflow.

In the Step Palette expansion panel, after the existing `@foreach` loop for step types, add:

```razor
@* Divider and sub-workflow shortcuts *@
@if (_availableWorkflows.Any(w => w.IsActive && w.Id != Id))
{
    <MudDivider Class="my-2" />
    <MudText Typo="Typo.overline" Class="mb-1">Sub-Workflows</MudText>
    @foreach (var wf in _availableWorkflows.Where(w => w.IsActive && w.Id != Id))
    {
        <MudPaper Class="pa-2 mb-2 step-palette-item" Elevation="1"
                  Style="border-left: 4px solid #009688; cursor: pointer;"
                  @onclick="@(() => AddSubWorkflowFromPalette(wf))">
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                <MudText>📋</MudText>
                <MudStack Spacing="0">
                    <MudText Typo="Typo.body2">@(wf.DisplayName ?? wf.Name)</MudText>
                    <MudText Typo="Typo.caption" Style="font-size: 0.7em;">Sub-workflow</MudText>
                </MudStack>
            </MudStack>
        </MudPaper>
    }
}
```

Add the helper method in the `@code` block:

```csharp
private void AddSubWorkflowFromPalette(WorkflowListResponse targetWorkflow)
{
    var stepName = $"sub-{targetWorkflow.Name}-{_nextStepNumber++}";
    
    // Position nodes in a cascading pattern
    var xOffset = 300 + ((_nextStepNumber - 1) % 5) * 50;
    var yOffset = 100 + ((_nextStepNumber - 1) / 5) * 150;
    
    var node = new WorkflowNodeModel(
        new Point(xOffset, yOffset),
        StepType.SubWorkflow,
        stepName,
        targetWorkflow.DisplayName ?? targetWorkflow.Name
    );
    node.SortOrder = _steps.Count;
    node.ReferencedWorkflowId = targetWorkflow.Id;
    node.ReferencedWorkflowName = targetWorkflow.Name;
    
    // Pre-populate config
    var config = new Dictionary<string, string>
    {
        ["workflow_id"] = targetWorkflow.Id.ToString(),
        ["workflow_name"] = targetWorkflow.Name
    };
    node.ConfigurationJson = JsonSerializer.Serialize(config);
    
    _diagram.Nodes.Add(node);
    
    _steps.Add(new WorkflowStepDto(
        null, stepName, targetWorkflow.DisplayName ?? targetWorkflow.Name,
        StepType.SubWorkflow,
        node.ConfigurationJson, xOffset, yOffset, null, _steps.Count
    ));
    
    StateHasChanged();
}
```

## Task 8: Update LoadWorkflowIntoDiagram for SubWorkflow Nodes

In the `LoadWorkflowIntoDiagram()` method, after creating each node and setting its properties, call `LoadSubWorkflowConfig()` for SubWorkflow nodes:

```csharp
// After setting node.ConfigurationJson:
node.ConfigurationJson = step.ConfigurationJson;
node.SortOrder = step.SortOrder;

// Load sub-workflow reference if applicable
node.LoadSubWorkflowConfig();
```

## Task 9: Update Python StepType Enum

**File**: `agent/src/agent/runtime/models.py`

Add SubWorkflow to the Python enum so the agent doesn't break when it encounters the new step type in exported workflows:

```python
class StepType(str, Enum):
    """Workflow step types matching C# enum."""
    TRIGGER = "Trigger"
    CLASSIFY = "Classify"
    QUERY = "Query"
    VALIDATE = "Validate"
    EXECUTE = "Execute"
    UPDATE_TICKET = "UpdateTicket"
    NOTIFY = "Notify"
    ESCALATE = "Escalate"
    CONDITION = "Condition"
    END = "End"
    SUB_WORKFLOW = "SubWorkflow"  # NEW - Phase C2 will add executor
```

No executor registration needed yet — the engine's existing passthrough behavior (logged warning, returns completed) will handle SubWorkflow steps gracefully until Phase C2 adds the real executor.

## Task 10: Update TriggerType on WorkflowDefinition (Bonus — ties into Phase T1)

While we're touching the designer, let's also wire up the `TriggerType` field on `WorkflowDefinition` so it gets set when saving a workflow that has a Trigger step.

In `Designer.razor`'s `SaveWorkflow()` method, after building steps and transitions from the diagram, determine the trigger type from the Trigger step's configuration and include it in the update:

First, add a helper method:
```csharp
private string? DetermineTriggerType()
{
    var triggerStep = _steps.FirstOrDefault(s => s.StepType == StepType.Trigger);
    if (triggerStep?.ConfigurationJson != null)
    {
        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(triggerStep.ConfigurationJson);
            if (config?.TryGetValue("source", out var source) == true)
                return source;
        }
        catch { }
    }
    // Default for workflows with a trigger step
    return triggerStep != null ? "servicenow" : null;
}
```

This doesn't require backend changes since `WorkflowDefinition.TriggerType` already exists — it just needs to be set during save. If the existing update endpoint doesn't expose TriggerType yet, skip this task; we'll handle it in Phase T2 when we add trigger type configuration to the Trigger step's properties panel.

## Verification Checklist

After implementation:

1. **StepType enum** has `SubWorkflow` as the last value in both C# and Python
2. **Step Palette** shows SubWorkflow with teal color and 📋 icon below the standard step types
3. **Dynamic sub-workflow section** appears below the palette with shortcuts for active workflows
4. **Adding a SubWorkflow node** from either the palette or the shortcut section creates a node with Exec In, Completed, and Escalated ports
5. **Selecting a SubWorkflow node** shows the Step Properties panel with a workflow picker dropdown
6. **Selecting a target workflow** from the dropdown:
   - Updates the node on the canvas to show the workflow name (▸ workflow-name)
   - Stores `{"workflow_id": "...", "workflow_name": "..."}` in ConfigurationJson
7. **Self-reference prevention**: The current workflow does NOT appear in the picker or shortcuts
8. **Saving and reloading** preserves the sub-workflow configuration
9. **The Python agent** doesn't crash when encountering SubWorkflow steps (passthrough behavior)

## What NOT To Change

- `workflow_engine.py` — No executor registration for SubWorkflow yet (Phase C2)
- `executors/` — No SubWorkflowExecutor yet (Phase C2)
- `AgentExportService.cs` — No sub-workflow inclusion in export yet (Phase C2)
- `runner.py` — No changes
- Any trigger provider code — No changes

## Commit Message

```
feat: add SubWorkflow step type to workflow designer

- Add SubWorkflow to StepType enum (C# and Python)
- Add SubWorkflow node with Completed/Escalated output ports
- Add workflow picker in Step Properties panel for SubWorkflow steps
- Add dynamic Sub-Workflows section to Step Palette
- SubWorkflow nodes display referenced workflow name on canvas
- Self-reference prevention in workflow picker
- Python agent handles SubWorkflow as passthrough (executor in Phase C2)

Part of ADR-011: Composable Workflows & Pluggable Triggers (Phase C1)
```
