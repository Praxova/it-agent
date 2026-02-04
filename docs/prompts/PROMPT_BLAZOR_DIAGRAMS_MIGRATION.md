# PROMPT: Migrate Workflow Designer from Drawflow.js to Z.Blazor.Diagrams

## Context

The Lucid Admin Portal's visual workflow designer currently uses Drawflow.js via JavaScript interop. This has caused persistent issues: SVG connection rendering bugs, DOM timing problems between Blazor and JS lifecycle, CSS class name mismatches, and fundamentally limited node rendering (no labeled ports, no typed connections, no rich node interiors).

We are migrating to Z.Blazor.Diagrams, a Blazor-native diagramming library that eliminates the JS interop layer entirely and provides proper port models, custom node Razor components, and professional-quality rendering.

## Pre-Work: Git Branch

**Do this first before any code changes:**

```bash
cd /home/alton/Documents/lucid-it-agent
git add -A
git commit -m "chore: pre-migration snapshot before Drawflow to Blazor.Diagrams migration"
git checkout -b feature/blazor-diagrams-migration
```

## Step 1: Add NuGet Package, Remove Drawflow

```bash
cd admin/dotnet/src/LucidAdmin.Web
dotnet add package Z.Blazor.Diagrams
```

Then:

1. **Delete** `wwwroot/js/drawflow-interop.js`
2. **Delete** `wwwroot/css/drawflow-custom.css`
3. **Remove** the Drawflow CDN `<script>` and `<link>` tags from the layout file (check `Components/App.razor` or `_Host.cshtml` or `_Layout.cshtml` — whichever is used)
4. **Add** Z.Blazor.Diagrams references in the same layout file:
   ```html
   <!-- In <head> -->
   <link href="_content/Z.Blazor.Diagrams/style.min.css" rel="stylesheet" />
   <link href="_content/Z.Blazor.Diagrams/default.styles.min.css" rel="stylesheet" />
   
   <!-- Before closing </body> or with other scripts -->
   <script src="_content/Z.Blazor.Diagrams/script.min.js"></script>
   ```

## Step 2: Create Workflow Node Models

Create file: `Components/WorkflowDesigner/Models/WorkflowNodeModel.cs`

```csharp
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Components.WorkflowDesigner.Models;

/// <summary>
/// Custom node model for workflow steps. Each node represents a step in the workflow
/// with named, labeled ports that make the flow logic visible.
/// </summary>
public class WorkflowNodeModel : NodeModel
{
    public string StepName { get; set; }
    public string DisplayName { get; set; }
    public StepType StepType { get; set; }
    public string? ConfigurationJson { get; set; }
    public Guid? StepId { get; set; }
    public int SortOrder { get; set; }

    // Named port references for easy connection building
    public PortModel? ExecIn { get; private set; }
    public Dictionary<string, PortModel> OutputPorts { get; } = new();

    public WorkflowNodeModel(Point position, StepType stepType, string stepName, string? displayName = null)
        : base(position)
    {
        StepType = stepType;
        StepName = stepName;
        DisplayName = displayName ?? stepName;

        // Create ports based on step type
        CreatePorts();
    }

    private void CreatePorts()
    {
        // Input port (all types except Trigger)
        if (StepType != StepType.Trigger)
        {
            ExecIn = AddPort(PortAlignment.Left);
        }

        // Output ports based on step type
        switch (StepType)
        {
            case StepType.Trigger:
                OutputPorts["exec_out"] = AddPort(PortAlignment.Right);
                break;

            case StepType.Classify:
                OutputPorts["high_confidence"] = AddPort(PortAlignment.Right);
                OutputPorts["low_confidence"] = AddPort(PortAlignment.Right);
                break;

            case StepType.Validate:
                OutputPorts["valid"] = AddPort(PortAlignment.Right);
                OutputPorts["invalid"] = AddPort(PortAlignment.Right);
                break;

            case StepType.Execute:
                OutputPorts["success"] = AddPort(PortAlignment.Right);
                OutputPorts["failure"] = AddPort(PortAlignment.Right);
                break;

            case StepType.Condition:
                OutputPorts["true"] = AddPort(PortAlignment.Right);
                OutputPorts["false"] = AddPort(PortAlignment.Right);
                break;

            case StepType.End:
                // No outputs — terminal node
                break;

            default:
                // Notify, Escalate, UpdateTicket, Query — single output
                OutputPorts["done"] = AddPort(PortAlignment.Right);
                break;
        }
    }

    /// <summary>
    /// Get output port by index (for transition mapping from database).
    /// </summary>
    public PortModel? GetOutputByIndex(int index)
    {
        var keys = OutputPorts.Keys.ToList();
        return index < keys.Count ? OutputPorts[keys[index]] : null;
    }

    /// <summary>
    /// Get the index of a named output port (for saving transitions to database).
    /// </summary>
    public int GetOutputIndex(PortModel port)
    {
        var keys = OutputPorts.Keys.ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            if (OutputPorts[keys[i]] == port) return i;
        }
        return 0;
    }
}
```

## Step 3: Create Node Widget Razor Components

Create a directory: `Components/WorkflowDesigner/Widgets/`

### Base design specification for ALL node widgets:

Each node should render as a styled card with:
- **Title bar**: Full-width colored header with icon + display name. Color based on StepType.
- **Body**: Contains the step type label and any config preview
- **Input port(s)**: Rendered on the LEFT side with label text to the right of the dot
- **Output port(s)**: Rendered on the RIGHT side with label text to the left of the dot

Use the PortRenderer component from Z.Blazor.Diagrams to render ports. Ports must be rendered inside the node component using `<PortRenderer Port="@port" />`.

### Color scheme (matching current design):
```
Trigger:      #4CAF50 (green)
Classify:     #2196F3 (blue)  
Query:        #9C27B0 (purple)
Validate:     #FF9800 (orange)
Execute:      #F44336 (red)
UpdateTicket: #00BCD4 (cyan)
Notify:       #E91E63 (pink)
Escalate:     #FF5722 (deep orange)
Condition:    #795548 (brown)
End:          #607D8B (blue grey)
```

### Icon mapping:
```
Trigger: 🎯    Classify: 🏷️    Query: 🔍     Validate: ✓
Execute: ⚡    UpdateTicket: 📝  Notify: 📧    Escalate: 🚨
Condition: ❓   End: 🏁
```

### Create: `Components/WorkflowDesigner/Widgets/WorkflowNodeWidget.razor`

This is a SINGLE generic widget that handles all step types. We use one component rather than 10 separate ones because the rendering logic is the same — only the port names and colors differ.

```razor
@using Blazor.Diagrams.Components.Renderers
@using LucidAdmin.Web.Components.WorkflowDesigner.Models
@using LucidAdmin.Core.Enums

<div class="workflow-node @(Node.Selected ? "selected" : "")"
     style="--node-color: @GetColor();">

    @* Title Bar *@
    <div class="workflow-node-header">
        <span class="workflow-node-icon">@GetIcon()</span>
        <span class="workflow-node-title">@Node.DisplayName</span>
    </div>

    @* Body with ports *@
    <div class="workflow-node-body">
        <div class="workflow-node-type">@Node.StepType</div>

        @* Port rows *@
        <div class="workflow-node-ports">
            @* Input port *@
            <div class="port-column port-inputs">
                @if (Node.ExecIn != null)
                {
                    <div class="port-row">
                        <PortRenderer Port="@Node.ExecIn" Class="workflow-port input-port" />
                        <span class="port-label">Exec In</span>
                    </div>
                }
            </div>

            @* Output ports *@
            <div class="port-column port-outputs">
                @foreach (var kvp in Node.OutputPorts)
                {
                    <div class="port-row output">
                        <span class="port-label">@GetPortLabel(kvp.Key)</span>
                        <PortRenderer Port="@kvp.Value" Class="workflow-port output-port" />
                    </div>
                }
            </div>
        </div>
    </div>
</div>

@code {
    [Parameter] public WorkflowNodeModel Node { get; set; } = null!;

    private string GetColor() => Node.StepType switch
    {
        StepType.Trigger => "#4CAF50",
        StepType.Classify => "#2196F3",
        StepType.Query => "#9C27B0",
        StepType.Validate => "#FF9800",
        StepType.Execute => "#F44336",
        StepType.UpdateTicket => "#00BCD4",
        StepType.Notify => "#E91E63",
        StepType.Escalate => "#FF5722",
        StepType.Condition => "#795548",
        StepType.End => "#607D8B",
        _ => "#666"
    };

    private string GetIcon() => Node.StepType switch
    {
        StepType.Trigger => "🎯",
        StepType.Classify => "🏷️",
        StepType.Query => "🔍",
        StepType.Validate => "✓",
        StepType.Execute => "⚡",
        StepType.UpdateTicket => "📝",
        StepType.Notify => "📧",
        StepType.Escalate => "🚨",
        StepType.Condition => "❓",
        StepType.End => "🏁",
        _ => "📦"
    };

    private string GetPortLabel(string portKey) => portKey switch
    {
        "exec_out" => "Exec Out",
        "high_confidence" => "High Confidence ✓",
        "low_confidence" => "Low Confidence ✗",
        "valid" => "Valid ✓",
        "invalid" => "Invalid ✗",
        "success" => "Success ✓",
        "failure" => "Failure ✗",
        "true" => "True",
        "false" => "False",
        "done" => "Done",
        _ => portKey
    };
}
```

## Step 4: Create Custom CSS

Create file: `wwwroot/css/workflow-designer.css`

Design requirements:
- Dark theme matching current Admin Portal (background: #1a1a2e area, nodes: #16213e bodies)
- Grid background on canvas (subtle dotted or lined grid)
- Nodes should have a colored top header bar (not left border — full-width header)
- The header color comes from `--node-color` CSS variable set per node
- Ports render as small circles (10-12px) with a subtle border
- Port labels are small text (0.75em) next to the port circle
- Input ports + labels are left-aligned, output ports + labels are right-aligned
- Selected nodes get a glow/border highlight
- Connection lines (links) should be smooth bezier curves in a visible color (e.g., #e94560 or adapt to a lighter accent)
- Minimum node width ~200px so port labels are readable
- The node body should have subtle padding and the type label in muted text

Here is a starting point — adjust as needed for visual quality:

```css
/* Canvas background */
.diagram-canvas {
    background-color: #1a1a2e;
    background-image:
        radial-gradient(circle, rgba(255,255,255,0.05) 1px, transparent 1px);
    background-size: 24px 24px;
}

/* Node container */
.workflow-node {
    background: #16213e;
    border: 2px solid #0f3460;
    border-radius: 8px;
    min-width: 200px;
    overflow: hidden;
    font-family: 'Segoe UI', system-ui, sans-serif;
    color: #eee;
    transition: border-color 0.2s, box-shadow 0.2s;
}

.workflow-node.selected {
    border-color: #e94560;
    box-shadow: 0 0 15px rgba(233, 69, 96, 0.4);
}

/* Header bar — full width, colored by step type */
.workflow-node-header {
    background: var(--node-color, #666);
    padding: 8px 12px;
    display: flex;
    align-items: center;
    gap: 8px;
}

.workflow-node-icon {
    font-size: 1.1em;
}

.workflow-node-title {
    font-weight: 600;
    font-size: 0.85em;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

/* Body */
.workflow-node-body {
    padding: 8px 12px 12px;
}

.workflow-node-type {
    font-size: 0.7em;
    color: #888;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 8px;
}

/* Port layout */
.workflow-node-ports {
    display: flex;
    justify-content: space-between;
    gap: 16px;
}

.port-column {
    display: flex;
    flex-direction: column;
    gap: 6px;
}

.port-inputs { align-items: flex-start; }
.port-outputs { align-items: flex-end; }

.port-row {
    display: flex;
    align-items: center;
    gap: 6px;
}

.port-row.output {
    flex-direction: row;  /* label then port dot */
}

.port-label {
    font-size: 0.75em;
    color: #bbb;
    white-space: nowrap;
}

/* Port circles */
.workflow-port {
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: #0f3460;
    border: 2px solid #e94560;
    cursor: crosshair;
    transition: background 0.15s;
}

.workflow-port:hover {
    background: #e94560;
}

/* Links/connections */
.diagram-link path {
    stroke: #e94560;
    stroke-width: 2.5px;
    fill: none;
}

.diagram-link path:hover {
    stroke: #ff6b6b;
    stroke-width: 3.5px;
}
```

**IMPORTANT**: The exact CSS class names for Z.Blazor.Diagrams ports and links may differ. Check the library's default CSS (`_content/Z.Blazor.Diagrams/default.styles.min.css`) and adapt class names accordingly. The `PortRenderer` component accepts a `Class` parameter. The diagram canvas has class `diagram-canvas`.

## Step 5: Rewrite Designer.razor

Rewrite `Components/Pages/Workflows/Designer.razor` to use `BlazorDiagram`.

### Key architectural points:

1. **Diagram initialization** in `OnInitialized()`:
```csharp
private BlazorDiagram _diagram = null!;

protected override void OnInitialized()
{
    var options = new BlazorDiagramOptions
    {
        AllowMultiSelection = true,
        AllowPanning = true,
        Zoom = new DiagramZoomOptions
        {
            Enabled = true,
            Minimum = 0.25,
            Maximum = 3.0
        },
        Links = new DiagramLinkOptions
        {
            DefaultRouter = new NormalRouter(),
            DefaultPathGenerator = new SmoothPathGenerator()
        },
        GridSize = 20
    };

    _diagram = new BlazorDiagram(options);

    // Register our custom node widget
    _diagram.RegisterComponent<WorkflowNodeModel, WorkflowNodeWidget>();

    // Event handlers
    _diagram.SelectionChanged += OnSelectionChanged;
    _diagram.Links.Added += OnLinkAdded;
    _diagram.Links.Removed += OnLinkRemoved;
    _diagram.Nodes.Removed += OnNodeRemoved;
}
```

2. **Canvas in the markup** replaces the `<div id="drawflow-container">`:
```razor
<CascadingValue Value="_diagram">
    <DiagramCanvas Class="workflow-canvas" />
</CascadingValue>
```

3. **Loading existing workflow** — convert WorkflowSteps + StepTransitions to diagram nodes and links:
```csharp
private void LoadWorkflowIntoDiagram()
{
    var nodeMap = new Dictionary<string, WorkflowNodeModel>();

    // Create nodes
    foreach (var step in _steps.OrderBy(s => s.SortOrder))
    {
        var node = new WorkflowNodeModel(
            new Point(step.PositionX, step.PositionY),
            step.StepType,
            step.Name,
            step.DisplayName
        );
        node.StepId = step.Id;
        node.ConfigurationJson = step.ConfigurationJson;
        node.SortOrder = step.SortOrder;

        _diagram.Nodes.Add(node);
        nodeMap[step.Name] = node;
    }

    // Create links from transitions
    foreach (var trans in _transitions)
    {
        if (nodeMap.TryGetValue(trans.FromStepName, out var fromNode) &&
            nodeMap.TryGetValue(trans.ToStepName, out var toNode))
        {
            var sourcePort = fromNode.GetOutputByIndex(trans.OutputIndex);
            var targetPort = toNode.ExecIn;

            if (sourcePort != null && targetPort != null)
            {
                var link = new LinkModel(sourcePort, targetPort);
                _diagram.Links.Add(link);
            }
        }
    }
}
```

4. **Adding nodes from palette** — clicking a step type in the palette creates a node at a default position or center of viewport:
```csharp
private void AddStepFromPalette(StepTypeInfo stepType)
{
    var stepName = $"{stepType.Value.ToString().ToLower()}-{_nextStepNumber++}";
    var node = new WorkflowNodeModel(
        new Point(300 + (_nextStepNumber * 30), 200 + (_nextStepNumber * 20)),
        stepType.Value,
        stepName,
        stepType.Name
    );
    _diagram.Nodes.Add(node);
}
```

5. **Saving** — extract nodes and transitions from diagram state:
```csharp
private void BuildStepsAndTransitionsFromDiagram()
{
    _steps.Clear();
    _transitions.Clear();
    int sortOrder = 0;

    foreach (var node in _diagram.Nodes.OfType<WorkflowNodeModel>())
    {
        _steps.Add(new WorkflowStepDto(
            node.StepId,
            node.StepName,
            node.DisplayName,
            node.StepType,
            node.ConfigurationJson,
            (int)node.Position.X,
            (int)node.Position.Y,
            null, // no more DrawflowNodeId
            sortOrder++
        ));
    }

    foreach (var link in _diagram.Links.OfType<LinkModel>())
    {
        if (link.Source?.Model is PortModel sourcePort &&
            link.Target?.Model is PortModel targetPort)
        {
            var fromNode = sourcePort.Parent as WorkflowNodeModel;
            var toNode = targetPort.Parent as WorkflowNodeModel;

            if (fromNode != null && toNode != null)
            {
                _transitions.Add(new StepTransitionDto(
                    null,
                    fromNode.StepName,
                    toNode.StepName,
                    null, // label
                    null, // condition
                    fromNode.GetOutputIndex(sourcePort),
                    0     // input index (always 0, single ExecIn)
                ));
            }
        }
    }
}
```

6. **Selection handling** — when a node is selected, update the sidebar properties panel:
```csharp
private void OnSelectionChanged(SelectableModel model)
{
    if (model is WorkflowNodeModel node && node.Selected)
    {
        _selectedStep = _steps.FirstOrDefault(s => s.Name == node.StepName);
        LoadStepConfig();
    }
    else
    {
        _selectedStep = null;
    }
    InvokeAsync(StateHasChanged);
}
```

7. **Remove all IJSRuntime usage** from Designer.razor. No more `JS.InvokeVoidAsync("drawflowInterop.*")` calls. The zoom controls can use `_diagram.SetZoom()` and `_diagram.SetPan()` methods or the built-in controls.

8. **Remove `IAsyncDisposable`** implementation (no JS cleanup needed). If you keep event subscriptions, implement `IDisposable` instead to unsubscribe.

### The overall layout structure stays similar:
- Left sidebar (3/12 columns): Workflow Details, Step Palette, Selected Step Properties
- Canvas area (9/12 columns): `<DiagramCanvas>` with zoom toolbar overlay
- Step Palette changes from drag-and-drop to click-to-add buttons (simpler, more reliable)

### Remove the `@ondrop`, `@ondragover`, `@ondragstart` event handlers from the old drag-and-drop implementation.

## Step 6: Update DrawflowLayoutGenerator

The `DrawflowLayoutGenerator` in Infrastructure/Services is no longer needed for the designer (the diagram state is now managed by Z.Blazor.Diagrams in memory). However, the LayoutJson field in the database may still be useful for:
- Persisting diagram viewport state (pan/zoom)
- Caching node positions (though these are already stored in WorkflowStep.PositionX/PositionY)

**Option A (Simpler)**: Remove LayoutJson usage entirely. Node positions are already stored in WorkflowStep. Remove the `LayoutJson` field from save/load if it's only used for Drawflow import. The seeder doesn't need to generate LayoutJson anymore.

**Option B (Keep as metadata)**: Repurpose LayoutJson to store viewport state (zoom level, pan position). This is a small JSON blob, not the full Drawflow export.

**Recommend Option A** for now. Clean is better. We can add viewport persistence later if needed.

- In `WorkflowSeeder.cs`, remove calls to `DrawflowLayoutGenerator` and layout generation
- The `DrawflowLayoutGenerator.cs` file can be kept temporarily but its registration and usage removed
- In the save layout endpoint, stop saving/loading LayoutJson (or save empty/null)

## Step 7: Update Service Registration

In `Program.cs`:
- Remove `builder.Services.AddScoped<DrawflowLayoutGenerator>();` (or leave it — not harmful)
- No new services needed for Z.Blazor.Diagrams (it works through Blazor's component model)

## Step 8: Update Head/Layout References

In whatever layout file contains the `<head>` tag (check `Components/App.razor` or `_Host.cshtml`):
- Remove any Drawflow CDN `<link>` and `<script>` tags
- Add the Z.Blazor.Diagrams CSS and JS references (see Step 1)
- Add reference to our custom CSS: `<link href="css/workflow-designer.css" rel="stylesheet" />`

## Step 9: Build and Verify

```bash
cd /home/alton/Documents/lucid-it-agent/admin/dotnet
dotnet build
```

Fix any build errors. The most common issues will be:
- Missing `using` statements for `Blazor.Diagrams.Core.*` namespaces
- `PortAlignment` is in `Blazor.Diagrams.Core.Models`
- `Point` is in `Blazor.Diagrams.Core.Geometry`
- `BlazorDiagram` is in `Blazor.Diagrams`
- `SmoothPathGenerator` and `NormalRouter` are in `Blazor.Diagrams.Core.PathGenerators` and `Blazor.Diagrams.Core.Routers`
- The `LinkModel` source/target API may use `Anchor` objects — check library docs

## Step 10: Commit

```bash
cd /home/alton/Documents/lucid-it-agent
git add -A
git commit -m "feat: migrate workflow designer from Drawflow.js to Z.Blazor.Diagrams

- Replace Drawflow.js + JS interop with Blazor-native Z.Blazor.Diagrams
- Create WorkflowNodeModel with named, labeled ports per step type
- Create WorkflowNodeWidget Razor component for Blueprint-style node rendering
- Rewrite Designer.razor to use BlazorDiagram (no JS interop)
- Add custom CSS for dark-themed node editor with colored headers
- Remove drawflow-interop.js and drawflow-custom.css
- Node positions persist via WorkflowStep.PositionX/PositionY (unchanged)
- All backend APIs and entities remain unchanged"
```

## Important Notes for Implementation

1. **Z.Blazor.Diagrams API may vary by version.** If the exact API doesn't match what's described above (e.g., `LinkModel` constructor signature, `PortAlignment` location, `SelectionChanged` event signature), check the library's GitHub README and source code. The NuGet package version on nuget.org as of Feb 2026 should be consulted for the current API.

2. **Port rendering**: The `PortRenderer` component MUST be rendered inside the custom node widget. Ports that aren't rendered in the DOM won't be connectable. Make sure every port in the model is rendered with `<PortRenderer>`.

3. **Node sizing**: Z.Blazor.Diagrams may need explicit size hints. If nodes appear with zero size, you may need to call `node.RefreshAll()` after the first render, or set a default `Size` on the `WorkflowNodeModel`.

4. **Link anchors**: Z.Blazor.Diagrams uses an anchor system. When creating links programmatically from saved transitions, you may need `new LinkModel(new SinglePortAnchor(sourcePort), new SinglePortAnchor(targetPort))` rather than passing ports directly. Check the library API.

5. **Diagram canvas sizing**: Ensure the `<DiagramCanvas>` has explicit width/height via its parent container CSS. A `height: 100%` chain from the page root to the canvas is necessary. The canvas won't render if it has 0 dimensions (same issue we had with Drawflow, but easier to debug since it's pure CSS).

6. **Do NOT change any files outside the Admin Portal** — agent/, tool-server/, docs/ should be untouched. The only changes are in `admin/dotnet/src/LucidAdmin.Web/`.

## Files to Create
- `Components/WorkflowDesigner/Models/WorkflowNodeModel.cs`
- `Components/WorkflowDesigner/Widgets/WorkflowNodeWidget.razor`
- `wwwroot/css/workflow-designer.css`

## Files to Modify
- `Components/Pages/Workflows/Designer.razor` (major rewrite)
- `Components/App.razor` or `_Host.cshtml` (CSS/JS references)
- `LucidAdmin.Web.csproj` (NuGet reference — added by `dotnet add package`)

## Files to Delete
- `wwwroot/js/drawflow-interop.js`
- `wwwroot/css/drawflow-custom.css`

## Files to Optionally Modify
- `Program.cs` (remove DrawflowLayoutGenerator registration if desired)
- Seeder files (remove LayoutJson generation if going with Option A)

## What NOT to Change
- Entity models (WorkflowStep, StepTransition, WorkflowDefinition)
- API endpoints (WorkflowEndpoints.cs)
- Service classes (AgentExportService.cs, etc.)
- Any files outside admin/dotnet/src/LucidAdmin.Web/
