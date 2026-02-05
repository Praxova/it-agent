# PROMPT: Fix Workflow Designer Load/Save Lifecycle Bug

## Problem

Existing workflows (both seeded and user-created) appear as blank canvases when opened in the designer. New nodes added from the palette render correctly, and saving reports success, but reopening the workflow shows blank again.

## Root Cause

Blazor Server lifecycle race condition in `Designer.razor`. The `OnAfterRender(firstRender: true)` fires before `OnInitializedAsync()` completes loading workflow data. By the time `_steps` is populated, `firstRender` has already passed and `LoadWorkflowIntoDiagram()` never executes.

## Fix

File: `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Workflows/Designer.razor`

### 1. Add two new fields in the `@code` block:

```csharp
private bool _dataLoaded = false;
private bool _diagramPopulated = false;
```

### 2. Set `_dataLoaded` flag after loading workflow data in `OnInitializedAsync()`:

Change:
```csharp
if (!_isNew)
{
    await LoadWorkflow();
}
```

To:
```csharp
if (!_isNew)
{
    await LoadWorkflow();
    _dataLoaded = true;
}
```

### 3. Replace `OnAfterRender` implementation:

Change:
```csharp
protected override void OnAfterRender(bool firstRender)
{
    if (firstRender && !_isNew && _steps.Any())
    {
        LoadWorkflowIntoDiagram();
        StateHasChanged();
    }
}
```

To:
```csharp
protected override void OnAfterRender(bool firstRender)
{
    if (!_diagramPopulated && !_isNew && _dataLoaded && _steps.Any())
    {
        _diagramPopulated = true;
        LoadWorkflowIntoDiagram();
        StateHasChanged();
    }
}
```

### 4. Also make `SaveWorkflowLayoutRequest` LayoutJson nullable-safe:

In the save method, the current code passes `null` for the `LayoutJson` parameter:
```csharp
var layoutRequest = new SaveWorkflowLayoutRequest(null, _steps.ToList(), _transitions.ToList());
```

This should still work, but to be clean, update it to pass an empty string:
```csharp
var layoutRequest = new SaveWorkflowLayoutRequest("", _steps.ToList(), _transitions.ToList());
```

And optionally, in `admin/dotnet/src/LucidAdmin.Web/Api/Models/Requests/WorkflowRequests.cs`, make LayoutJson nullable:
```csharp
public record SaveWorkflowLayoutRequest(
    string? LayoutJson,   // was: string LayoutJson
    List<WorkflowStepDto> Steps,
    List<StepTransitionDto> Transitions
);
```

## Verification

1. `dotnet build` from `admin/dotnet/` — should build clean
2. Run the app, open any seed workflow — nodes and connections should appear
3. Add nodes from palette, connect them, save
4. Navigate away, reopen — saved nodes and connections should persist

## Files to Modify
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Workflows/Designer.razor` (3 changes)
- `admin/dotnet/src/LucidAdmin.Web/Api/Models/Requests/WorkflowRequests.cs` (optional nullable fix)
