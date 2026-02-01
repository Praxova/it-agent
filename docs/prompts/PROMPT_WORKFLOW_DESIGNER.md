# Claude Code Prompt: Visual Workflow Designer (Phase 3)

## Context

This is Phase 3 of the Workflow Designer feature set. We're building a visual drag-and-drop workflow designer using Drawflow.js integrated with Blazor Server via JavaScript interop.

Phases 1 (Rules Editor) and 2 (Examples Editor) are complete. This phase builds on those foundations.

See:
- `docs/adr/ADR-010-visual-workflow-designer.md` - Architecture decision
- `docs/WORKFLOW_DESIGNER_ENTITIES.md` - Full entity model documentation
- Existing Rulesets and ExampleSets implementations for patterns

## Overview

The Workflow Designer allows users to visually create ticket processing pipelines by:
1. Dragging step nodes onto a canvas
2. Connecting steps with transitions
3. Configuring each step's behavior
4. Associating rulesets with workflows or specific steps
5. Linking an example set for classifier training

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Blazor Server Page                           │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Left Sidebar          │  Canvas (Drawflow.js)            │  │
│  │  ┌─────────────────┐   │  ┌─────────────────────────────┐ │  │
│  │  │ Step Palette    │   │  │                             │ │  │
│  │  │ - Trigger       │   │  │    [Trigger]                │ │  │
│  │  │ - Classify      │   │  │        │                    │ │  │
│  │  │ - Validate      │   │  │        ▼                    │ │  │
│  │  │ - Execute       │   │  │    [Classify]───►[Escalate] │ │  │
│  │  │ - Notify        │   │  │        │                    │ │  │
│  │  │ - Escalate      │   │  │        ▼                    │ │  │
│  │  │ - Condition     │   │  │    [Execute]                │ │  │
│  │  │ - End           │   │  │        │                    │ │  │
│  │  └─────────────────┘   │  │        ▼                    │ │  │
│  │                        │  │      [End]                  │ │  │
│  │  ┌─────────────────┐   │  │                             │ │  │
│  │  │ Properties      │   │  └─────────────────────────────┘ │  │
│  │  │ (selected node) │   │                                  │  │
│  │  └─────────────────┘   │                                  │  │
│  └────────────────────────┴──────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

## Files to Create/Modify

### 1. Core Layer - Entities

**Create `/src/LucidAdmin.Core/Entities/WorkflowDefinition.cs`:**
```csharp
namespace LucidAdmin.Core.Entities;

/// <summary>
/// A workflow defines how tickets are processed through a series of steps.
/// </summary>
public class WorkflowDefinition : BaseEntity
{
    /// <summary>
    /// Unique name for the workflow (e.g., "password-reset-standard").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Description of what this workflow handles.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Version string for tracking changes.
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    
    /// <summary>
    /// Whether this workflow ships with the product.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;
    
    /// <summary>
    /// Whether this workflow is active and can be assigned to agents.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// JSON export from Drawflow containing visual layout.
    /// </summary>
    public string? LayoutJson { get; set; }
    
    /// <summary>
    /// Optional: Example set used for classifier training in this workflow.
    /// </summary>
    public Guid? ExampleSetId { get; set; }
    
    /// <summary>
    /// Navigation property to example set.
    /// </summary>
    public ExampleSet? ExampleSet { get; set; }
    
    /// <summary>
    /// Steps in this workflow.
    /// </summary>
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    
    /// <summary>
    /// Rulesets that apply to all steps in this workflow.
    /// </summary>
    public ICollection<WorkflowRulesetMapping> RulesetMappings { get; set; } = new List<WorkflowRulesetMapping>();
}
```

**Create `/src/LucidAdmin.Core/Entities/WorkflowStep.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single step/node in a workflow.
/// </summary>
public class WorkflowStep : BaseEntity
{
    /// <summary>
    /// Foreign key to parent workflow.
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }
    
    /// <summary>
    /// Parent workflow navigation property.
    /// </summary>
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    
    /// <summary>
    /// Unique name within the workflow (e.g., "classify-ticket").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Display label shown on the node.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Type of step (determines behavior and available configuration).
    /// </summary>
    public required StepType StepType { get; set; }
    
    /// <summary>
    /// JSON configuration specific to this step type.
    /// </summary>
    public string? ConfigurationJson { get; set; }
    
    /// <summary>
    /// X position on canvas (for visual layout).
    /// </summary>
    public int PositionX { get; set; } = 100;
    
    /// <summary>
    /// Y position on canvas (for visual layout).
    /// </summary>
    public int PositionY { get; set; } = 100;
    
    /// <summary>
    /// Drawflow node ID (for syncing with JS).
    /// </summary>
    public int? DrawflowNodeId { get; set; }
    
    /// <summary>
    /// Order for execution when multiple steps could run.
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    /// <summary>
    /// Outgoing transitions from this step.
    /// </summary>
    public ICollection<StepTransition> OutgoingTransitions { get; set; } = new List<StepTransition>();
    
    /// <summary>
    /// Incoming transitions to this step.
    /// </summary>
    public ICollection<StepTransition> IncomingTransitions { get; set; } = new List<StepTransition>();
    
    /// <summary>
    /// Rulesets that apply specifically to this step.
    /// </summary>
    public ICollection<StepRulesetMapping> RulesetMappings { get; set; } = new List<StepRulesetMapping>();
}
```

**Create `/src/LucidAdmin.Core/Entities/StepTransition.cs`:**
```csharp
namespace LucidAdmin.Core.Entities;

/// <summary>
/// A connection/transition between two workflow steps.
/// </summary>
public class StepTransition : BaseEntity
{
    /// <summary>
    /// Source step (where the connection starts).
    /// </summary>
    public Guid FromStepId { get; set; }
    
    /// <summary>
    /// Navigation property to source step.
    /// </summary>
    public WorkflowStep? FromStep { get; set; }
    
    /// <summary>
    /// Target step (where the connection ends).
    /// </summary>
    public Guid ToStepId { get; set; }
    
    /// <summary>
    /// Navigation property to target step.
    /// </summary>
    public WorkflowStep? ToStep { get; set; }
    
    /// <summary>
    /// Label shown on the connection (e.g., "success", "failure", "escalate").
    /// </summary>
    public string? Label { get; set; }
    
    /// <summary>
    /// Condition expression for conditional transitions (e.g., "confidence &lt; 0.6").
    /// </summary>
    public string? Condition { get; set; }
    
    /// <summary>
    /// Output port index on source node (Drawflow uses numbered outputs).
    /// </summary>
    public int OutputIndex { get; set; } = 0;
    
    /// <summary>
    /// Input port index on target node.
    /// </summary>
    public int InputIndex { get; set; } = 0;
    
    /// <summary>
    /// Order when multiple transitions from same output.
    /// </summary>
    public int SortOrder { get; set; } = 0;
}
```

**Create `/src/LucidAdmin.Core/Entities/WorkflowRulesetMapping.cs`:**
```csharp
namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a ruleset to an entire workflow (applies to all steps).
/// </summary>
public class WorkflowRulesetMapping : BaseEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    
    public Guid RulesetId { get; set; }
    public Ruleset? Ruleset { get; set; }
    
    /// <summary>
    /// Order in which rulesets are evaluated.
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Whether this mapping is active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
```

**Create `/src/LucidAdmin.Core/Entities/StepRulesetMapping.cs`:**
```csharp
namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a ruleset to a specific workflow step.
/// </summary>
public class StepRulesetMapping : BaseEntity
{
    public Guid WorkflowStepId { get; set; }
    public WorkflowStep? WorkflowStep { get; set; }
    
    public Guid RulesetId { get; set; }
    public Ruleset? Ruleset { get; set; }
    
    /// <summary>
    /// Order in which rulesets are evaluated.
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Whether this mapping is active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
```

**Create `/src/LucidAdmin.Core/Enums/StepType.cs`:**
```csharp
namespace LucidAdmin.Core.Enums;

/// <summary>
/// Types of steps available in workflow designer.
/// </summary>
public enum StepType
{
    /// <summary>Entry point - triggered when ticket matches criteria.</summary>
    Trigger,
    
    /// <summary>Use LLM to classify ticket type and extract data.</summary>
    Classify,
    
    /// <summary>Query external systems for context (AD user info, etc.).</summary>
    Query,
    
    /// <summary>Validate data against rules before proceeding.</summary>
    Validate,
    
    /// <summary>Execute an action via tool server (reset password, etc.).</summary>
    Execute,
    
    /// <summary>Update the ticket in ServiceNow.</summary>
    UpdateTicket,
    
    /// <summary>Send notification (email, Teams, etc.).</summary>
    Notify,
    
    /// <summary>Escalate to human operator.</summary>
    Escalate,
    
    /// <summary>Conditional branch based on data.</summary>
    Condition,
    
    /// <summary>End of workflow path.</summary>
    End
}
```

### 2. Repository Interface

**Create `/src/LucidAdmin.Core/Interfaces/Repositories/IWorkflowDefinitionRepository.cs`:**
```csharp
namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IWorkflowDefinitionRepository : IRepository<WorkflowDefinition>
{
    /// <summary>
    /// Get workflow with all steps, transitions, and ruleset mappings.
    /// </summary>
    Task<WorkflowDefinition?> GetFullWorkflowAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Get all active workflows (for dropdown selections).
    /// </summary>
    Task<IEnumerable<WorkflowDefinition>> GetAllActiveAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Check if workflow name exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
```

### 3. EF Core Configurations

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/WorkflowDefinitionConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.HasIndex(e => e.Name)
            .IsUnique();
            
        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);
            
        builder.Property(e => e.Description)
            .HasMaxLength(2000);
            
        builder.Property(e => e.Version)
            .HasMaxLength(20);
            
        builder.HasOne(e => e.ExampleSet)
            .WithMany()
            .HasForeignKey(e => e.ExampleSetId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasMany(e => e.Steps)
            .WithOne(s => s.WorkflowDefinition)
            .HasForeignKey(s => s.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(e => e.RulesetMappings)
            .WithOne(m => m.WorkflowDefinition)
            .HasForeignKey(m => m.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/WorkflowStepConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);
            
        builder.Property(e => e.StepType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
            
        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.Name })
            .IsUnique();
            
        builder.HasMany(e => e.OutgoingTransitions)
            .WithOne(t => t.FromStep)
            .HasForeignKey(t => t.FromStepId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(e => e.IncomingTransitions)
            .WithOne(t => t.ToStep)
            .HasForeignKey(t => t.ToStepId)
            .OnDelete(DeleteBehavior.Restrict);
            
        builder.HasMany(e => e.RulesetMappings)
            .WithOne(m => m.WorkflowStep)
            .HasForeignKey(m => m.WorkflowStepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/StepTransitionConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class StepTransitionConfiguration : IEntityTypeConfiguration<StepTransition>
{
    public void Configure(EntityTypeBuilder<StepTransition> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Label)
            .HasMaxLength(100);
            
        builder.Property(e => e.Condition)
            .HasMaxLength(500);
            
        builder.HasIndex(e => new { e.FromStepId, e.ToStepId, e.OutputIndex })
            .IsUnique();
    }
}
```

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/WorkflowRulesetMappingConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class WorkflowRulesetMappingConfiguration : IEntityTypeConfiguration<WorkflowRulesetMapping>
{
    public void Configure(EntityTypeBuilder<WorkflowRulesetMapping> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.RulesetId })
            .IsUnique();
            
        builder.HasOne(e => e.Ruleset)
            .WithMany()
            .HasForeignKey(e => e.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/StepRulesetMappingConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class StepRulesetMappingConfiguration : IEntityTypeConfiguration<StepRulesetMapping>
{
    public void Configure(EntityTypeBuilder<StepRulesetMapping> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.HasIndex(e => new { e.WorkflowStepId, e.RulesetId })
            .IsUnique();
            
        builder.HasOne(e => e.Ruleset)
            .WithMany()
            .HasForeignKey(e => e.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Update `/src/LucidAdmin.Infrastructure/Data/LucidDbContext.cs`:**
Add DbSets:
```csharp
public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
public DbSet<StepTransition> StepTransitions => Set<StepTransition>();
public DbSet<WorkflowRulesetMapping> WorkflowRulesetMappings => Set<WorkflowRulesetMapping>();
public DbSet<StepRulesetMapping> StepRulesetMappings => Set<StepRulesetMapping>();
```

### 4. Repository Implementation

**Create `/src/LucidAdmin.Infrastructure/Repositories/WorkflowDefinitionRepository.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Infrastructure.Repositories;

public class WorkflowDefinitionRepository : RepositoryBase<WorkflowDefinition>, IWorkflowDefinitionRepository
{
    public WorkflowDefinitionRepository(LucidDbContext context) : base(context) { }
    
    public async Task<WorkflowDefinition?> GetFullWorkflowAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet
            .Include(w => w.ExampleSet)
            .Include(w => w.Steps.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.OutgoingTransitions)
            .Include(w => w.Steps)
                .ThenInclude(s => s.RulesetMappings)
                    .ThenInclude(m => m.Ruleset)
            .Include(w => w.RulesetMappings.OrderBy(m => m.Priority))
                .ThenInclude(m => m.Ruleset)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }
    
    public async Task<IEnumerable<WorkflowDefinition>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await DbSet
            .Where(w => w.IsActive)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }
    
    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await DbSet.AnyAsync(w => w.Name == name, ct);
    }
}
```

**Update `/src/LucidAdmin.Infrastructure/DependencyInjection.cs`:**
Add registration:
```csharp
services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
```

### 5. Drawflow.js Integration

**Create `/src/LucidAdmin.Web/wwwroot/js/drawflow-interop.js`:**
```javascript
// Drawflow.js interop for Blazor
window.drawflowInterop = {
    editor: null,
    dotnetHelper: null,
    
    // Initialize Drawflow editor
    initialize: function(containerId, dotnetHelper) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Drawflow container not found:', containerId);
            return false;
        }
        
        this.dotnetHelper = dotnetHelper;
        this.editor = new Drawflow(container);
        
        // Configure editor
        this.editor.reroute = true;
        this.editor.reroute_fix_curvature = true;
        this.editor.force_first_input = false;
        
        // Register node types
        this.registerNodeTypes();
        
        // Start editor
        this.editor.start();
        
        // Set up event handlers
        this.setupEventHandlers();
        
        return true;
    },
    
    // Register custom node HTML templates
    registerNodeTypes: function() {
        // Trigger node
        this.editor.registerNode('Trigger', this.createNodeHtml('Trigger', '🎯', '#4CAF50'));
        // Classify node
        this.editor.registerNode('Classify', this.createNodeHtml('Classify', '🏷️', '#2196F3'));
        // Query node
        this.editor.registerNode('Query', this.createNodeHtml('Query', '🔍', '#9C27B0'));
        // Validate node
        this.editor.registerNode('Validate', this.createNodeHtml('Validate', '✓', '#FF9800'));
        // Execute node  
        this.editor.registerNode('Execute', this.createNodeHtml('Execute', '⚡', '#F44336'));
        // UpdateTicket node
        this.editor.registerNode('UpdateTicket', this.createNodeHtml('Update Ticket', '📝', '#00BCD4'));
        // Notify node
        this.editor.registerNode('Notify', this.createNodeHtml('Notify', '📧', '#E91E63'));
        // Escalate node
        this.editor.registerNode('Escalate', this.createNodeHtml('Escalate', '🚨', '#FF5722'));
        // Condition node
        this.editor.registerNode('Condition', this.createNodeHtml('Condition', '❓', '#795548'));
        // End node
        this.editor.registerNode('End', this.createNodeHtml('End', '🏁', '#607D8B'));
    },
    
    createNodeHtml: function(name, icon, color) {
        return `
            <div class="workflow-node" style="border-left: 4px solid ${color};">
                <div class="node-header">
                    <span class="node-icon">${icon}</span>
                    <span class="node-title" df-name>${name}</span>
                </div>
                <div class="node-body">
                    <small class="node-type" df-stepType></small>
                </div>
            </div>
        `;
    },
    
    // Set up Drawflow event handlers
    setupEventHandlers: function() {
        const self = this;
        
        // Node added
        this.editor.on('nodeCreated', function(nodeId) {
            if (self.dotnetHelper) {
                const node = self.editor.getNodeFromId(nodeId);
                self.dotnetHelper.invokeMethodAsync('OnNodeCreated', nodeId, node.name, node.pos_x, node.pos_y);
            }
        });
        
        // Node removed
        this.editor.on('nodeRemoved', function(nodeId) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnNodeRemoved', nodeId);
            }
        });
        
        // Node moved
        this.editor.on('nodeMoved', function(nodeId) {
            if (self.dotnetHelper) {
                const node = self.editor.getNodeFromId(nodeId);
                self.dotnetHelper.invokeMethodAsync('OnNodeMoved', nodeId, node.pos_x, node.pos_y);
            }
        });
        
        // Node selected
        this.editor.on('nodeSelected', function(nodeId) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnNodeSelected', nodeId);
            }
        });
        
        // Connection created
        this.editor.on('connectionCreated', function(connection) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnConnectionCreated', 
                    connection.output_id, 
                    connection.input_id,
                    parseInt(connection.output_class.replace('output_', '')),
                    parseInt(connection.input_class.replace('input_', ''))
                );
            }
        });
        
        // Connection removed
        this.editor.on('connectionRemoved', function(connection) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnConnectionRemoved',
                    connection.output_id,
                    connection.input_id
                );
            }
        });
    },
    
    // Add a node to the canvas
    addNode: function(stepType, posX, posY, inputs, outputs, data) {
        const nodeId = this.editor.addNode(
            stepType,           // name (used for template lookup)
            inputs,             // number of inputs
            outputs,            // number of outputs  
            posX,               // x position
            posY,               // y position
            stepType,           // class
            data,               // data object
            stepType            // html template name
        );
        return nodeId;
    },
    
    // Remove a node
    removeNode: function(nodeId) {
        this.editor.removeNodeId('node-' + nodeId);
    },
    
    // Add connection between nodes
    addConnection: function(outputNodeId, inputNodeId, outputIndex, inputIndex) {
        this.editor.addConnection(
            outputNodeId,
            inputNodeId,
            'output_' + outputIndex,
            'input_' + inputIndex
        );
    },
    
    // Remove connection
    removeConnection: function(outputNodeId, inputNodeId, outputIndex, inputIndex) {
        this.editor.removeSingleConnection(
            outputNodeId,
            inputNodeId,
            'output_' + outputIndex,
            'input_' + inputIndex
        );
    },
    
    // Update node data
    updateNodeData: function(nodeId, data) {
        this.editor.updateNodeDataFromId(nodeId, data);
    },
    
    // Get node data
    getNodeData: function(nodeId) {
        return this.editor.getNodeFromId(nodeId);
    },
    
    // Export entire workflow as JSON
    export: function() {
        return JSON.stringify(this.editor.export());
    },
    
    // Import workflow from JSON
    import: function(jsonData) {
        const data = JSON.parse(jsonData);
        this.editor.import(data);
    },
    
    // Clear the canvas
    clear: function() {
        this.editor.clear();
    },
    
    // Zoom controls
    zoomIn: function() {
        this.editor.zoom_in();
    },
    
    zoomOut: function() {
        this.editor.zoom_out();
    },
    
    zoomReset: function() {
        this.editor.zoom_reset();
    },
    
    // Destroy editor
    destroy: function() {
        if (this.editor) {
            // Drawflow doesn't have explicit destroy, just clear references
            this.editor = null;
            this.dotnetHelper = null;
        }
    }
};
```

**Create `/src/LucidAdmin.Web/wwwroot/css/drawflow-custom.css`:**
```css
/* Drawflow custom styles for Lucid Admin */

#drawflow-container {
    width: 100%;
    height: 600px;
    background-color: #1a1a2e;
    background-image: 
        linear-gradient(rgba(255,255,255,.05) 1px, transparent 1px),
        linear-gradient(90deg, rgba(255,255,255,.05) 1px, transparent 1px);
    background-size: 20px 20px;
}

.drawflow .drawflow-node {
    background: #16213e;
    border: 2px solid #0f3460;
    border-radius: 8px;
    min-width: 160px;
    padding: 0;
}

.drawflow .drawflow-node.selected {
    border-color: #e94560;
    box-shadow: 0 0 10px rgba(233, 69, 96, 0.5);
}

.drawflow .drawflow-node .inputs,
.drawflow .drawflow-node .outputs {
    display: flex;
    justify-content: space-around;
}

.drawflow .drawflow-node .input,
.drawflow .drawflow-node .output {
    width: 14px;
    height: 14px;
    background: #0f3460;
    border: 2px solid #e94560;
    border-radius: 50%;
}

.drawflow .drawflow-node .input:hover,
.drawflow .drawflow-node .output:hover {
    background: #e94560;
}

.drawflow .connection .main-path {
    stroke: #e94560;
    stroke-width: 3px;
}

.drawflow .connection .main-path:hover {
    stroke: #ff6b6b;
    stroke-width: 4px;
}

/* Custom node styles */
.workflow-node {
    padding: 10px;
    color: #eee;
}

.workflow-node .node-header {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 5px;
}

.workflow-node .node-icon {
    font-size: 1.2em;
}

.workflow-node .node-title {
    font-weight: 600;
    font-size: 0.9em;
}

.workflow-node .node-body {
    font-size: 0.8em;
    color: #aaa;
}

/* Node type specific colors */
.drawflow-node.Trigger { border-left-color: #4CAF50 !important; }
.drawflow-node.Classify { border-left-color: #2196F3 !important; }
.drawflow-node.Query { border-left-color: #9C27B0 !important; }
.drawflow-node.Validate { border-left-color: #FF9800 !important; }
.drawflow-node.Execute { border-left-color: #F44336 !important; }
.drawflow-node.UpdateTicket { border-left-color: #00BCD4 !important; }
.drawflow-node.Notify { border-left-color: #E91E63 !important; }
.drawflow-node.Escalate { border-left-color: #FF5722 !important; }
.drawflow-node.Condition { border-left-color: #795548 !important; }
.drawflow-node.End { border-left-color: #607D8B 7 !important; }
```

**Update `/src/LucidAdmin.Web/Components/App.razor`:**
Add Drawflow CSS and JS to the head/body:
```razor
@* In <head> section *@
<link rel="stylesheet" href="https://cdn.jsdelivr.net/gh/jerosoler/Drawflow/dist/drawflow.min.css">
<link rel="stylesheet" href="css/drawflow-custom.css">

@* Before closing </body> *@
<script src="https://cdn.jsdelivr.net/gh/jerosoler/Drawflow/dist/drawflow.min.js"></script>
<script src="js/drawflow-interop.js"></script>
```

### 6. API Request/Response Models

**Create `/src/LucidAdmin.Web/Api/Models/Requests/WorkflowRequests.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Models.Requests;

public record CreateWorkflowRequest(
    string Name,
    string? DisplayName,
    string? Description,
    Guid? ExampleSetId,
    bool IsActive = true
);

public record UpdateWorkflowRequest(
    string? DisplayName,
    string? Description,
    string? Version,
    Guid? ExampleSetId,
    bool? IsActive
);

public record SaveWorkflowLayoutRequest(
    string LayoutJson,
    List<WorkflowStepDto> Steps,
    List<StepTransitionDto> Transitions
);

public record WorkflowStepDto(
    Guid? Id,
    string Name,
    string? DisplayName,
    StepType StepType,
    string? ConfigurationJson,
    int PositionX,
    int PositionY,
    int? DrawflowNodeId,
    int SortOrder
);

public record StepTransitionDto(
    Guid? Id,
    string FromStepName,
    string ToStepName,
    string? Label,
    string? Condition,
    int OutputIndex,
    int InputIndex
);

public record AddWorkflowRulesetRequest(
    Guid RulesetId,
    int Priority = 0,
    bool IsEnabled = true
);

public record AddStepRulesetRequest(
    Guid RulesetId,
    int Priority = 0,
    bool IsEnabled = true
);
```

**Create `/src/LucidAdmin.Web/Api/Models/Responses/WorkflowResponses.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Models.Responses;

public record WorkflowListResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    string Version,
    bool IsBuiltIn,
    bool IsActive,
    int StepCount,
    string? ExampleSetName,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record WorkflowDetailResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    string Version,
    bool IsBuiltIn,
    bool IsActive,
    string? LayoutJson,
    Guid? ExampleSetId,
    string? ExampleSetName,
    List<WorkflowStepResponse> Steps,
    List<StepTransitionResponse> Transitions,
    List<WorkflowRulesetMappingResponse> RulesetMappings,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record WorkflowStepResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    StepType StepType,
    string? ConfigurationJson,
    int PositionX,
    int PositionY,
    int? DrawflowNodeId,
    int SortOrder,
    List<StepRulesetMappingResponse> RulesetMappings
);

public record StepTransitionResponse(
    Guid Id,
    Guid FromStepId,
    string FromStepName,
    Guid ToStepId,
    string ToStepName,
    string? Label,
    string? Condition,
    int OutputIndex,
    int InputIndex
);

public record WorkflowRulesetMappingResponse(
    Guid Id,
    Guid RulesetId,
    string RulesetName,
    string? RulesetDisplayName,
    int Priority,
    bool IsEnabled
);

public record StepRulesetMappingResponse(
    Guid Id,
    Guid RulesetId,
    string RulesetName,
    string? RulesetDisplayName,
    int Priority,
    bool IsEnabled
);

public record StepTypeInfo(
    StepType Value,
    string Name,
    string Description,
    string Icon,
    string Color,
    int DefaultInputs,
    int DefaultOutputs
);
```

### 7. API Endpoints

**Create `/src/LucidAdmin.Web/Api/Endpoints/WorkflowEndpoints.cs`:**
```csharp
using Microsoft.AspNetCore.Mvc;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Web.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workflows")
            .WithTags("Workflows");
        
        // Get step types with metadata
        group.MapGet("/step-types", () =>
        {
            var stepTypes = new List<StepTypeInfo>
            {
                new(StepType.Trigger, "Trigger", "Entry point when ticket matches criteria", "🎯", "#4CAF50", 0, 1),
                new(StepType.Classify, "Classify", "Use LLM to classify ticket and extract data", "🏷️", "#2196F3", 1, 2),
                new(StepType.Query, "Query", "Query external systems for context", "🔍", "#9C27B0", 1, 1),
                new(StepType.Validate, "Validate", "Validate data against rules", "✓", "#FF9800", 1, 2),
                new(StepType.Execute, "Execute", "Execute action via tool server", "⚡", "#F44336", 1, 2),
                new(StepType.UpdateTicket, "Update Ticket", "Update the ServiceNow ticket", "📝", "#00BCD4", 1, 1),
                new(StepType.Notify, "Notify", "Send notification", "📧", "#E91E63", 1, 1),
                new(StepType.Escalate, "Escalate", "Escalate to human operator", "🚨", "#FF5722", 1, 1),
                new(StepType.Condition, "Condition", "Branch based on condition", "❓", "#795548", 1, 2),
                new(StepType.End, "End", "End of workflow path", "🏁", "#607D8B", 1, 0)
            };
            return Results.Ok(stepTypes);
        });
        
        // List all workflows
        group.MapGet("/", async (IWorkflowDefinitionRepository repo) =>
        {
            var workflows = await repo.GetAllAsync();
            var response = workflows.Select(w => new WorkflowListResponse(
                w.Id, w.Name, w.DisplayName, w.Description, w.Version,
                w.IsBuiltIn, w.IsActive, w.Steps.Count,
                w.ExampleSet?.DisplayName ?? w.ExampleSet?.Name,
                w.CreatedAt, w.UpdatedAt
            ));
            return Results.Ok(response);
        });
        
        // Get single workflow with full details
        group.MapGet("/{id:guid}", async (Guid id, IWorkflowDefinitionRepository repo) =>
        {
            var workflow = await repo.GetFullWorkflowAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            var response = MapToDetailResponse(workflow);
            return Results.Ok(response);
        });
        
        // Create workflow
        group.MapPost("/", async (CreateWorkflowRequest request, IWorkflowDefinitionRepository repo) =>
        {
            if (await repo.ExistsAsync(request.Name))
                return Results.Conflict(new { error = "WorkflowExists" });
            
            var workflow = new WorkflowDefinition
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                ExampleSetId = request.ExampleSetId,
                IsActive = request.IsActive,
                IsBuiltIn = false,
                Version = "1.0.0"
            };
            
            await repo.AddAsync(workflow);
            
            return Results.Created($"/api/v1/workflows/{workflow.Id}", new WorkflowListResponse(
                workflow.Id, workflow.Name, workflow.DisplayName, workflow.Description, workflow.Version,
                workflow.IsBuiltIn, workflow.IsActive, 0, null,
                workflow.CreatedAt, workflow.UpdatedAt
            ));
        });
        
        // Update workflow metadata
        group.MapPut("/{id:guid}", async (Guid id, UpdateWorkflowRequest request, IWorkflowDefinitionRepository repo) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            if (request.DisplayName is not null) workflow.DisplayName = request.DisplayName;
            if (request.Description is not null) workflow.Description = request.Description;
            if (request.Version is not null) workflow.Version = request.Version;
            if (request.ExampleSetId.HasValue) workflow.ExampleSetId = request.ExampleSetId;
            if (request.IsActive.HasValue) workflow.IsActive = request.IsActive.Value;
            
            await repo.UpdateAsync(workflow);
            
            return Results.Ok(new WorkflowListResponse(
                workflow.Id, workflow.Name, workflow.DisplayName, workflow.Description, workflow.Version,
                workflow.IsBuiltIn, workflow.IsActive, workflow.Steps.Count, null,
                workflow.CreatedAt, workflow.UpdatedAt
            ));
        });
        
        // Save workflow layout (steps and transitions)
        group.MapPut("/{id:guid}/layout", async (
            Guid id,
            SaveWorkflowLayoutRequest request,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var workflow = await repo.GetFullWorkflowAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            // Save Drawflow JSON
            workflow.LayoutJson = request.LayoutJson;
            
            // Clear existing steps and transitions
            db.StepTransitions.RemoveRange(workflow.Steps.SelectMany(s => s.OutgoingTransitions));
            db.WorkflowSteps.RemoveRange(workflow.Steps);
            workflow.Steps.Clear();
            
            // Create new steps
            var stepNameToEntity = new Dictionary<string, WorkflowStep>();
            foreach (var stepDto in request.Steps)
            {
                var step = new WorkflowStep
                {
                    WorkflowDefinitionId = workflow.Id,
                    Name = stepDto.Name,
                    DisplayName = stepDto.DisplayName,
                    StepType = stepDto.StepType,
                    ConfigurationJson = stepDto.ConfigurationJson,
                    PositionX = stepDto.PositionX,
                    PositionY = stepDto.PositionY,
                    DrawflowNodeId = stepDto.DrawflowNodeId,
                    SortOrder = stepDto.SortOrder
                };
                workflow.Steps.Add(step);
                stepNameToEntity[step.Name] = step;
            }
            
            await db.SaveChangesAsync();
            
            // Create transitions (after steps have IDs)
            foreach (var transDto in request.Transitions)
            {
                if (stepNameToEntity.TryGetValue(transDto.FromStepName, out var fromStep) &&
                    stepNameToEntity.TryGetValue(transDto.ToStepName, out var toStep))
                {
                    var transition = new StepTransition
                    {
                        FromStepId = fromStep.Id,
                        ToStepId = toStep.Id,
                        Label = transDto.Label,
                        Condition = transDto.Condition,
                        OutputIndex = transDto.OutputIndex,
                        InputIndex = transDto.InputIndex
                    };
                    db.StepTransitions.Add(transition);
                }
            }
            
            await db.SaveChangesAsync();
            
            // Reload and return
            workflow = await repo.GetFullWorkflowAsync(id);
            return Results.Ok(MapToDetailResponse(workflow!));
        });
        
        // Delete workflow
        group.MapDelete("/{id:guid}", async (Guid id, IWorkflowDefinitionRepository repo) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotDeleteBuiltIn" });
            
            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
        
        // Copy workflow
        group.MapPost("/{id:guid}/copy", async (
            Guid id,
            [FromQuery] string newName,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var source = await repo.GetFullWorkflowAsync(id);
            if (source is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            if (await repo.ExistsAsync(newName))
                return Results.Conflict(new { error = "WorkflowExists" });
            
            var copy = new WorkflowDefinition
            {
                Name = newName,
                DisplayName = $"{source.DisplayName} (Copy)",
                Description = source.Description,
                Version = "1.0.0",
                ExampleSetId = source.ExampleSetId,
                LayoutJson = source.LayoutJson,
                IsActive = true,
                IsBuiltIn = false
            };
            
            // Copy steps
            var oldToNewStep = new Dictionary<Guid, WorkflowStep>();
            foreach (var srcStep in source.Steps)
            {
                var newStep = new WorkflowStep
                {
                    Name = srcStep.Name,
                    DisplayName = srcStep.DisplayName,
                    StepType = srcStep.StepType,
                    ConfigurationJson = srcStep.ConfigurationJson,
                    PositionX = srcStep.PositionX,
                    PositionY = srcStep.PositionY,
                    DrawflowNodeId = srcStep.DrawflowNodeId,
                    SortOrder = srcStep.SortOrder
                };
                copy.Steps.Add(newStep);
                oldToNewStep[srcStep.Id] = newStep;
            }
            
            await repo.AddAsync(copy);
            
            // Copy transitions (after steps have IDs)
            foreach (var srcStep in source.Steps)
            {
                foreach (var trans in srcStep.OutgoingTransitions)
                {
                    if (oldToNewStep.TryGetValue(trans.FromStepId, out var newFrom) &&
                        oldToNewStep.TryGetValue(trans.ToStepId, out var newTo))
                    {
                        db.StepTransitions.Add(new StepTransition
                        {
                            FromStepId = newFrom.Id,
                            ToStepId = newTo.Id,
                            Label = trans.Label,
                            Condition = trans.Condition,
                            OutputIndex = trans.OutputIndex,
                            InputIndex = trans.InputIndex
                        });
                    }
                }
            }
            
            // Copy ruleset mappings
            foreach (var mapping in source.RulesetMappings)
            {
                db.WorkflowRulesetMappings.Add(new WorkflowRulesetMapping
                {
                    WorkflowDefinitionId = copy.Id,
                    RulesetId = mapping.RulesetId,
                    Priority = mapping.Priority,
                    IsEnabled = mapping.IsEnabled
                });
            }
            
            await db.SaveChangesAsync();
            
            return Results.Created($"/api/v1/workflows/{copy.Id}", new WorkflowListResponse(
                copy.Id, copy.Name, copy.DisplayName, copy.Description, copy.Version,
                copy.IsBuiltIn, copy.IsActive, copy.Steps.Count, null,
                copy.CreatedAt, copy.UpdatedAt
            ));
        });
        
        // === Workflow ruleset mappings ===
        
        group.MapPost("/{id:guid}/rulesets", async (
            Guid id,
            AddWorkflowRulesetRequest request,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            var mapping = new WorkflowRulesetMapping
            {
                WorkflowDefinitionId = id,
                RulesetId = request.RulesetId,
                Priority = request.Priority,
                IsEnabled = request.IsEnabled
            };
            
            db.WorkflowRulesetMappings.Add(mapping);
            await db.SaveChangesAsync();
            
            return Results.Created($"/api/v1/workflows/{id}/rulesets/{mapping.Id}", mapping);
        });
        
        group.MapDelete("/{id:guid}/rulesets/{mappingId:guid}", async (
            Guid id,
            Guid mappingId,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });
            
            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            var mapping = await db.WorkflowRulesetMappings.FindAsync(mappingId);
            if (mapping is null || mapping.WorkflowDefinitionId != id)
                return Results.NotFound(new { error = "MappingNotFound" });
            
            db.WorkflowRulesetMappings.Remove(mapping);
            await db.SaveChangesAsync();
            
            return Results.NoContent();
        });
    }
    
    private static WorkflowDetailResponse MapToDetailResponse(WorkflowDefinition w)
    {
        var allTransitions = w.Steps.SelectMany(s => s.OutgoingTransitions).ToList();
        
        return new WorkflowDetailResponse(
            w.Id, w.Name, w.DisplayName, w.Description, w.Version,
            w.IsBuiltIn, w.IsActive, w.LayoutJson,
            w.ExampleSetId, w.ExampleSet?.DisplayName ?? w.ExampleSet?.Name,
            w.Steps.Select(s => new WorkflowStepResponse(
                s.Id, s.Name, s.DisplayName, s.StepType,
                s.ConfigurationJson, s.PositionX, s.PositionY,
                s.DrawflowNodeId, s.SortOrder,
                s.RulesetMappings.Select(m => new StepRulesetMappingResponse(
                    m.Id, m.RulesetId,
                    m.Ruleset?.Name ?? "",
                    m.Ruleset?.DisplayName,
                    m.Priority, m.IsEnabled
                )).ToList()
            )).ToList(),
            allTransitions.Select(t => new StepTransitionResponse(
                t.Id, t.FromStepId, t.FromStep?.Name ?? "",
                t.ToStepId, t.ToStep?.Name ?? "",
                t.Label, t.Condition, t.OutputIndex, t.InputIndex
            )).ToList(),
            w.RulesetMappings.Select(m => new WorkflowRulesetMappingResponse(
                m.Id, m.RulesetId,
                m.Ruleset?.Name ?? "",
                m.Ruleset?.DisplayName,
                m.Priority, m.IsEnabled
            )).ToList(),
            w.CreatedAt, w.UpdatedAt
        );
    }
}
```

**Update `/src/LucidAdmin.Web/Program.cs`:**
```csharp
app.MapWorkflowEndpoints();
```

### 8. Blazor UI Pages

**Create `/src/LucidAdmin.Web/Components/Pages/Workflows/Index.razor`:**
List page with:
- Table: Name, Version, Steps count, Example Set, Status, Actions
- Built-in badge
- Actions: Edit, Copy, Delete
- New Workflow button

**Create `/src/LucidAdmin.Web/Components/Pages/Workflows/Designer.razor`:**
```razor
@page "/workflows/{Id:guid}"
@page "/workflows/create"
@using LucidAdmin.Web.Api.Models.Requests
@using LucidAdmin.Web.Api.Models.Responses
@using LucidAdmin.Core.Enums
@using System.Text.Json
@implements IAsyncDisposable
@inject HttpClient Http
@inject NavigationManager Navigation
@inject ISnackbar Snackbar
@inject IJSRuntime JS

<PageTitle>@(_isNew ? "Create Workflow" : "Edit Workflow") - Lucid Admin</PageTitle>

<MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="mt-4 pa-0" Style="height: calc(100vh - 100px);">
    <MudGrid Style="height: 100%;">
        @* Left Sidebar *@
        <MudItem xs="3" Style="height: 100%; overflow-y: auto;">
            <MudPaper Class="pa-3" Style="height: 100%;">
                @* Workflow Details *@
                <MudExpansionPanels MultiExpansion="true">
                    <MudExpansionPanel Text="Workflow Details" IsInitiallyExpanded="true">
                        <MudTextField @bind-Value="_name" Label="Name" Required="true" 
                                      Disabled="@(!_isNew)" Class="mb-2"
                                      HelperText="Unique identifier" />
                        <MudTextField @bind-Value="_displayName" Label="Display Name" Class="mb-2" />
                        <MudTextField @bind-Value="_description" Label="Description" Lines="2" Class="mb-2" />
                        <MudSelect @bind-Value="_exampleSetId" Label="Example Set" Class="mb-2"
                                   Clearable="true">
                            @foreach (var es in _exampleSets)
                            {
                                <MudSelectItem Value="@es.Id">@(es.DisplayName ?? es.Name)</MudSelectItem>
                            }
                        </MudSelect>
                        <MudSwitch @bind-Value="_isActive" Label="Active" Color="Color.Primary" />
                    </MudExpansionPanel>
                    
                    @* Step Palette *@
                    <MudExpansionPanel Text="Step Palette" IsInitiallyExpanded="true">
                        <MudText Typo="Typo.caption" Class="mb-2">Drag steps onto the canvas</MudText>
                        @foreach (var stepType in _stepTypes)
                        {
                            <MudPaper Class="pa-2 mb-2 step-palette-item" Elevation="1"
                                      Style="@($"border-left: 4px solid {stepType.Color}; cursor: grab;")"
                                      @ondragstart="@(() => OnDragStart(stepType))"
                                      draggable="true">
                                <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                                    <MudText>@stepType.Icon</MudText>
                                    <MudStack Spacing="0">
                                        <MudText Typo="Typo.body2">@stepType.Name</MudText>
                                        <MudText Typo="Typo.caption">@stepType.Description</MudText>
                                    </MudStack>
                                </MudStack>
                            </MudPaper>
                        }
                    </MudExpansionPanel>
                    
                    @* Selected Node Properties *@
                    @if (_selectedStep != null)
                    {
                        <MudExpansionPanel Text="Step Properties" IsInitiallyExpanded="true">
                            <MudTextField @bind-Value="_selectedStep.Name" Label="Step Name" Class="mb-2" />
                            <MudTextField @bind-Value="_selectedStep.DisplayName" Label="Display Name" Class="mb-2" />
                            <MudText Typo="Typo.overline">Type: @_selectedStep.StepType</MudText>
                            
                            @* Type-specific configuration *@
                            @switch (_selectedStep.StepType)
                            {
                                case StepType.Execute:
                                    <MudTextField @bind-Value="_stepConfig_Capability" 
                                                  Label="Capability" Class="mb-2"
                                                  HelperText="e.g., ad-password-reset" />
                                    break;
                                case StepType.Condition:
                                    <MudTextField @bind-Value="_stepConfig_Condition" 
                                                  Label="Condition" Class="mb-2"
                                                  HelperText="e.g., confidence >= 0.8" />
                                    break;
                                case StepType.Notify:
                                    <MudSelect @bind-Value="_stepConfig_NotifyType" Label="Notify Type" Class="mb-2">
                                        <MudSelectItem Value="@("email")">Email</MudSelectItem>
                                        <MudSelectItem Value="@("teams")">Teams</MudSelectItem>
                                        <MudSelectItem Value="@("ticket")">Ticket Comment</MudSelectItem>
                                    </MudSelect>
                                    break;
                            }
                            
                            <MudButton Variant="Variant.Outlined" Color="Color.Error" Size="Size.Small"
                                       OnClick="DeleteSelectedStep" Class="mt-2">
                                Delete Step
                            </MudButton>
                        </MudExpansionPanel>
                    }
                    
                    @* Workflow Rulesets *@
                    <MudExpansionPanel Text="Workflow Rulesets">
                        <MudText Typo="Typo.caption" Class="mb-2">
                            Rules that apply to all steps
                        </MudText>
                        @foreach (var mapping in _workflowRulesets)
                        {
                            <MudChip Size="Size.Small" OnClose="@(() => RemoveWorkflowRuleset(mapping))"
                                     Color="Color.Primary" Variant="Variant.Outlined">
                                @mapping.RulesetDisplayName
                            </MudChip>
                        }
                        <MudButton Variant="Variant.Text" Size="Size.Small" 
                                   StartIcon="@Icons.Material.Filled.Add"
                                   OnClick="ShowAddRulesetDialog">
                            Add Ruleset
                        </MudButton>
                    </MudExpansionPanel>
                </MudExpansionPanels>
                
                @* Action Buttons *@
                <MudStack Row="true" Spacing="2" Class="mt-4">
                    <MudButton Variant="Variant.Filled" Color="Color.Primary"
                               OnClick="SaveWorkflow" Disabled="_saving">
                        @(_saving ? "Saving..." : "Save")
                    </MudButton>
                    <MudButton Variant="Variant.Outlined"
                               OnClick="@(() => Navigation.NavigateTo("/workflows"))">
                        Cancel
                    </MudButton>
                </MudStack>
            </MudPaper>
        </MudItem>
        
        @* Canvas Area *@
        <MudItem xs="9" Style="height: 100%;">
            <MudPaper Style="height: 100%; position: relative;">
                @* Toolbar *@
                <MudStack Row="true" Class="pa-2" Style="position: absolute; top: 0; right: 0; z-index: 10;">
                    <MudIconButton Icon="@Icons.Material.Filled.ZoomIn" OnClick="ZoomIn" Size="Size.Small" />
                    <MudIconButton Icon="@Icons.Material.Filled.ZoomOut" OnClick="ZoomOut" Size="Size.Small" />
                    <MudIconButton Icon="@Icons.Material.Filled.CenterFocusStrong" OnClick="ZoomReset" Size="Size.Small" />
                </MudStack>
                
                @* Drawflow Container *@
                <div id="drawflow-container" 
                     @ondrop="OnDrop" 
                     @ondragover="OnDragOver"
                     @ondragover:preventDefault="true"
                     style="width: 100%; height: 100%;">
                </div>
            </MudPaper>
        </MudItem>
    </MudGrid>
</MudContainer>

@* Add Ruleset Dialog *@
<MudDialog @bind-Visible="_showRulesetDialog">
    <TitleContent>Add Ruleset to Workflow</TitleContent>
    <DialogContent>
        <MudSelect @bind-Value="_selectedRulesetId" Label="Select Ruleset">
            @foreach (var rs in _availableRulesets)
            {
                <MudSelectItem Value="@rs.Id">@(rs.DisplayName ?? rs.Name)</MudSelectItem>
            }
        </MudSelect>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => _showRulesetDialog = false)">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="AddWorkflowRuleset">Add</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [Parameter] public Guid? Id { get; set; }
    
    private bool _isNew => Id is null;
    private bool _saving = false;
    private DotNetObjectReference<Designer>? _dotNetRef;
    
    // Workflow details
    private string _name = "";
    private string? _displayName;
    private string? _description;
    private Guid? _exampleSetId;
    private bool _isActive = true;
    
    // Step types palette
    private List<StepTypeInfo> _stepTypes = new();
    private StepTypeInfo? _draggedStepType;
    
    // Canvas state (synced with Drawflow)
    private List<WorkflowStepDto> _steps = new();
    private List<StepTransitionDto> _transitions = new();
    private WorkflowStepDto? _selectedStep;
    
    // Step config fields (for selected step)
    private string? _stepConfig_Capability;
    private string? _stepConfig_Condition;
    private string? _stepConfig_NotifyType;
    
    // Rulesets
    private List<WorkflowRulesetMappingResponse> _workflowRulesets = new();
    private List<RulesetResponse> _availableRulesets = new();
    private bool _showRulesetDialog = false;
    private Guid? _selectedRulesetId;
    
    // Example sets
    private List<ExampleSetResponse> _exampleSets = new();
    
    // Node ID mapping (Drawflow ID -> step name)
    private Dictionary<int, string> _nodeIdToStepName = new();
    private int _nextStepNumber = 1;
    
    protected override async Task OnInitializedAsync()
    {
        // Load step types
        _stepTypes = await Http.GetFromJsonAsync<List<StepTypeInfo>>("/api/v1/workflows/step-types") ?? new();
        
        // Load example sets
        _exampleSets = await Http.GetFromJsonAsync<List<ExampleSetResponse>>("/api/v1/example-sets") ?? new();
        
        // Load rulesets
        _availableRulesets = await Http.GetFromJsonAsync<List<RulesetResponse>>("/api/v1/rulesets") ?? new();
        
        if (!_isNew)
        {
            await LoadWorkflow();
        }
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("drawflowInterop.initialize", "drawflow-container", _dotNetRef);
            
            // If editing, restore layout
            if (!_isNew && !string.IsNullOrEmpty(_layoutJson))
            {
                await JS.InvokeVoidAsync("drawflowInterop.import", _layoutJson);
            }
        }
    }
    
    private string? _layoutJson;
    
    private async Task LoadWorkflow()
    {
        try
        {
            var response = await Http.GetFromJsonAsync<WorkflowDetailResponse>($"/api/v1/workflows/{Id}");
            if (response != null)
            {
                _name = response.Name;
                _displayName = response.DisplayName;
                _description = response.Description;
                _exampleSetId = response.ExampleSetId;
                _isActive = response.IsActive;
                _layoutJson = response.LayoutJson;
                _workflowRulesets = response.RulesetMappings;
                
                // Convert steps to DTOs
                foreach (var step in response.Steps)
                {
                    _steps.Add(new WorkflowStepDto(
                        step.Id, step.Name, step.DisplayName, step.StepType,
                        step.ConfigurationJson, step.PositionX, step.PositionY,
                        step.DrawflowNodeId, step.SortOrder
                    ));
                    
                    if (step.DrawflowNodeId.HasValue)
                    {
                        _nodeIdToStepName[step.DrawflowNodeId.Value] = step.Name;
                    }
                }
                
                // Convert transitions
                foreach (var trans in response.Transitions)
                {
                    _transitions.Add(new StepTransitionDto(
                        trans.Id, trans.FromStepName, trans.ToStepName,
                        trans.Label, trans.Condition,
                        trans.OutputIndex, trans.InputIndex
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error loading workflow: {ex.Message}", Severity.Error);
        }
    }
    
    // Drag and drop handlers
    private void OnDragStart(StepTypeInfo stepType)
    {
        _draggedStepType = stepType;
    }
    
    private void OnDragOver(DragEventArgs e)
    {
        // Allow drop
    }
    
    private async Task OnDrop(DragEventArgs e)
    {
        if (_draggedStepType is null) return;
        
        // Get container position for calculating drop coordinates
        // Note: In production, you'd want proper coordinate calculation
        var posX = (int)e.ClientX - 200; // Offset for sidebar
        var posY = (int)e.ClientY - 100; // Offset for toolbar
        
        var stepName = $"{_draggedStepType.Value.ToString().ToLower()}-{_nextStepNumber++}";
        
        var nodeId = await JS.InvokeAsync<int>("drawflowInterop.addNode",
            _draggedStepType.Value.ToString(),
            posX, posY,
            _draggedStepType.DefaultInputs,
            _draggedStepType.DefaultOutputs,
            new { name = stepName, stepType = _draggedStepType.Value.ToString() }
        );
        
        _nodeIdToStepName[nodeId] = stepName;
        _steps.Add(new WorkflowStepDto(
            null, stepName, _draggedStepType.Name, _draggedStepType.Value,
            null, posX, posY, nodeId, _steps.Count
        ));
        
        _draggedStepType = null;
        StateHasChanged();
    }
    
    // JS Interop callbacks
    [JSInvokable]
    public void OnNodeCreated(int nodeId, string name, double posX, double posY)
    {
        // Already handled in OnDrop for drag-created nodes
    }
    
    [JSInvokable]
    public void OnNodeRemoved(int nodeId)
    {
        if (_nodeIdToStepName.TryGetValue(nodeId, out var stepName))
        {
            _steps.RemoveAll(s => s.Name == stepName);
            _transitions.RemoveAll(t => t.FromStepName == stepName || t.ToStepName == stepName);
            _nodeIdToStepName.Remove(nodeId);
            
            if (_selectedStep?.Name == stepName)
                _selectedStep = null;
            
            InvokeAsync(StateHasChanged);
        }
    }
    
    [JSInvokable]
    public void OnNodeMoved(int nodeId, double posX, double posY)
    {
        if (_nodeIdToStepName.TryGetValue(nodeId, out var stepName))
        {
            var step = _steps.FirstOrDefault(s => s.Name == stepName);
            if (step != null)
            {
                var index = _steps.IndexOf(step);
                _steps[index] = step with { PositionX = (int)posX, PositionY = (int)posY };
            }
        }
    }
    
    [JSInvokable]
    public void OnNodeSelected(int nodeId)
    {
        if (_nodeIdToStepName.TryGetValue(nodeId, out var stepName))
        {
            _selectedStep = _steps.FirstOrDefault(s => s.Name == stepName);
            LoadStepConfig();
            InvokeAsync(StateHasChanged);
        }
    }
    
    [JSInvokable]
    public void OnConnectionCreated(int outputNodeId, int inputNodeId, int outputIndex, int inputIndex)
    {
        if (_nodeIdToStepName.TryGetValue(outputNodeId, out var fromName) &&
            _nodeIdToStepName.TryGetValue(inputNodeId, out var toName))
        {
            _transitions.Add(new StepTransitionDto(
                null, fromName, toName, null, null, outputIndex, inputIndex
            ));
            InvokeAsync(StateHasChanged);
        }
    }
    
    [JSInvokable]
    public void OnConnectionRemoved(int outputNodeId, int inputNodeId)
    {
        if (_nodeIdToStepName.TryGetValue(outputNodeId, out var fromName) &&
            _nodeIdToStepName.TryGetValue(inputNodeId, out var toName))
        {
            _transitions.RemoveAll(t => t.FromStepName == fromName && t.ToStepName == toName);
            InvokeAsync(StateHasChanged);
        }
    }
    
    private void LoadStepConfig()
    {
        if (_selectedStep?.ConfigurationJson is not null)
        {
            try
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(_selectedStep.ConfigurationJson);
                _stepConfig_Capability = config?.GetValueOrDefault("capability");
                _stepConfig_Condition = config?.GetValueOrDefault("condition");
                _stepConfig_NotifyType = config?.GetValueOrDefault("notifyType");
            }
            catch { }
        }
    }
    
    private async Task DeleteSelectedStep()
    {
        if (_selectedStep?.DrawflowNodeId is not null)
        {
            await JS.InvokeVoidAsync("drawflowInterop.removeNode", _selectedStep.DrawflowNodeId);
        }
    }
    
    // Zoom controls
    private async Task ZoomIn() => await JS.InvokeVoidAsync("drawflowInterop.zoomIn");
    private async Task ZoomOut() => await JS.InvokeVoidAsync("drawflowInterop.zoomOut");
    private async Task ZoomReset() => await JS.InvokeVoidAsync("drawflowInterop.zoomReset");
    
    // Ruleset management
    private void ShowAddRulesetDialog()
    {
        _selectedRulesetId = null;
        _showRulesetDialog = true;
    }
    
    private async Task AddWorkflowRuleset()
    {
        if (_selectedRulesetId is null) return;
        
        // For new workflows, just add to local list
        // For existing, call API
        if (!_isNew)
        {
            var request = new AddWorkflowRulesetRequest(_selectedRulesetId.Value);
            await Http.PostAsJsonAsync($"/api/v1/workflows/{Id}/rulesets", request);
        }
        
        var ruleset = _availableRulesets.First(r => r.Id == _selectedRulesetId);
        _workflowRulesets.Add(new WorkflowRulesetMappingResponse(
            Guid.NewGuid(), ruleset.Id, ruleset.Name, ruleset.DisplayName, 0, true
        ));
        
        _showRulesetDialog = false;
    }
    
    private async Task RemoveWorkflowRuleset(WorkflowRulesetMappingResponse mapping)
    {
        if (!_isNew)
        {
            await Http.DeleteAsync($"/api/v1/workflows/{Id}/rulesets/{mapping.Id}");
        }
        _workflowRulesets.Remove(mapping);
    }
    
    // Save workflow
    private async Task SaveWorkflow()
    {
        _saving = true;
        try
        {
            // Update step configs before saving
            if (_selectedStep is not null)
            {
                UpdateSelectedStepConfig();
            }
            
            var layoutJson = await JS.InvokeAsync<string>("drawflowInterop.export");
            
            if (_isNew)
            {
                // Create workflow first
                var createRequest = new CreateWorkflowRequest(_name, _displayName, _description, _exampleSetId, _isActive);
                var createResponse = await Http.PostAsJsonAsync("/api/v1/workflows", createRequest);
                
                if (!createResponse.IsSuccessStatusCode)
                {
                    var error = await createResponse.Content.ReadAsStringAsync();
                    Snackbar.Add($"Error: {error}", Severity.Error);
                    return;
                }
                
                var created = await createResponse.Content.ReadFromJsonAsync<WorkflowListResponse>();
                Id = created?.Id;
            }
            else
            {
                // Update metadata
                var updateRequest = new UpdateWorkflowRequest(_displayName, _description, null, _exampleSetId, _isActive);
                await Http.PutAsJsonAsync($"/api/v1/workflows/{Id}", updateRequest);
            }
            
            // Save layout (steps and transitions)
            var layoutRequest = new SaveWorkflowLayoutRequest(layoutJson, _steps, _transitions);
            var layoutResponse = await Http.PutAsJsonAsync($"/api/v1/workflows/{Id}/layout", layoutRequest);
            
            if (layoutResponse.IsSuccessStatusCode)
            {
                Snackbar.Add("Workflow saved successfully", Severity.Success);
                if (_isNew)
                {
                    Navigation.NavigateTo($"/workflows/{Id}");
                }
            }
            else
            {
                var error = await layoutResponse.Content.ReadAsStringAsync();
                Snackbar.Add($"Error saving layout: {error}", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }
    
    private void UpdateSelectedStepConfig()
    {
        if (_selectedStep is null) return;
        
        var config = new Dictionary<string, string>();
        
        switch (_selectedStep.StepType)
        {
            case StepType.Execute when !string.IsNullOrEmpty(_stepConfig_Capability):
                config["capability"] = _stepConfig_Capability;
                break;
            case StepType.Condition when !string.IsNullOrEmpty(_stepConfig_Condition):
                config["condition"] = _stepConfig_Condition;
                break;
            case StepType.Notify when !string.IsNullOrEmpty(_stepConfig_NotifyType):
                config["notifyType"] = _stepConfig_NotifyType;
                break;
        }
        
        var index = _steps.FindIndex(s => s.Name == _selectedStep.Name);
        if (index >= 0 && config.Count > 0)
        {
            _steps[index] = _steps[index] with { ConfigurationJson = JsonSerializer.Serialize(config) };
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_dotNetRef != null)
        {
            await JS.InvokeVoidAsync("drawflowInterop.destroy");
            _dotNetRef.Dispose();
        }
    }
}
```

**Add to NavMenu.razor:**
```razor
<MudNavLink Href="/workflows" Icon="@Icons.Material.Filled.AccountTree">
    Workflows
</MudNavLink>
```

### 9. Create Migration

```bash
cd admin/dotnet
dotnet ef migrations add AddWorkflowEntities \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
dotnet ef database update \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
```

### 10. Seed Built-in Workflow

Add a simple "Password Reset Standard" built-in workflow:

```csharp
if (!db.WorkflowDefinitions.Any(w => w.IsBuiltIn))
{
    var passwordResetWorkflow = new WorkflowDefinition
    {
        Name = "password-reset-standard",
        DisplayName = "Password Reset - Standard",
        Description = "Standard workflow for processing password reset requests",
        Version = "1.0.0",
        IsBuiltIn = true,
        IsActive = true,
        Steps = new List<WorkflowStep>
        {
            new WorkflowStep
            {
                Name = "trigger",
                DisplayName = "New Ticket",
                StepType = StepType.Trigger,
                PositionX = 100,
                PositionY = 200,
                SortOrder = 0
            },
            new WorkflowStep
            {
                Name = "classify",
                DisplayName = "Classify Ticket",
                StepType = StepType.Classify,
                PositionX = 300,
                PositionY = 200,
                SortOrder = 1
            },
            new WorkflowStep
            {
                Name = "validate",
                DisplayName = "Validate Request",
                StepType = StepType.Validate,
                PositionX = 500,
                PositionY = 150,
                SortOrder = 2
            },
            new WorkflowStep
            {
                Name = "escalate",
                DisplayName = "Escalate",
                StepType = StepType.Escalate,
                PositionX = 500,
                PositionY = 300,
                SortOrder = 3
            },
            new WorkflowStep
            {
                Name = "execute",
                DisplayName = "Reset Password",
                StepType = StepType.Execute,
                ConfigurationJson = "{\"capability\":\"ad-password-reset\"}",
                PositionX = 700,
                PositionY = 150,
                SortOrder = 4
            },
            new WorkflowStep
            {
                Name = "notify",
                DisplayName = "Notify User",
                StepType = StepType.Notify,
                ConfigurationJson = "{\"notifyType\":\"email\"}",
                PositionX = 900,
                PositionY = 150,
                SortOrder = 5
            },
            new WorkflowStep
            {
                Name = "end-success",
                DisplayName = "Complete",
                StepType = StepType.End,
                PositionX = 1100,
                PositionY = 150,
                SortOrder = 6
            },
            new WorkflowStep
            {
                Name = "end-escalated",
                DisplayName = "Escalated",
                StepType = StepType.End,
                PositionX = 700,
                PositionY = 300,
                SortOrder = 7
            }
        }
    };
    
    db.WorkflowDefinitions.Add(passwordResetWorkflow);
    await db.SaveChangesAsync();
    
    // Add transitions (after steps have IDs)
    var steps = db.WorkflowSteps.Where(s => s.WorkflowDefinitionId == passwordResetWorkflow.Id).ToDictionary(s => s.Name);
    
    db.StepTransitions.AddRange(
        new StepTransition { FromStepId = steps["trigger"].Id, ToStepId = steps["classify"].Id, OutputIndex = 0, InputIndex = 0 },
        new StepTransition { FromStepId = steps["classify"].Id, ToStepId = steps["validate"].Id, Label = "password_reset", OutputIndex = 0, InputIndex = 0, Condition = "ticket_type == 'password_reset'" },
        new StepTransition { FromStepId = steps["classify"].Id, ToStepId = steps["escalate"].Id, Label = "other", OutputIndex = 1, InputIndex = 0 },
        new StepTransition { FromStepId = steps["validate"].Id, ToStepId = steps["execute"].Id, Label = "valid", OutputIndex = 0, InputIndex = 0 },
        new StepTransition { FromStepId = steps["validate"].Id, ToStepId = steps["escalate"].Id, Label = "invalid", OutputIndex = 1, InputIndex = 0 },
        new StepTransition { FromStepId = steps["execute"].Id, ToStepId = steps["notify"].Id, Label = "success", OutputIndex = 0, InputIndex = 0 },
        new StepTransition { FromStepId = steps["execute"].Id, ToStepId = steps["escalate"].Id, Label = "failure", OutputIndex = 1, InputIndex = 0 },
        new StepTransition { FromStepId = steps["notify"].Id, ToStepId = steps["end-success"].Id, OutputIndex = 0, InputIndex = 0 },
        new StepTransition { FromStepId = steps["escalate"].Id, ToStepId = steps["end-escalated"].Id, OutputIndex = 0, InputIndex = 0 }
    );
    
    await db.SaveChangesAsync();
}
```

## Testing

1. Navigate to http://localhost:5000/workflows
2. Verify built-in Password Reset workflow appears
3. Create a new workflow
4. Drag steps onto canvas
5. Connect steps by dragging from output to input ports
6. Configure step properties
7. Save and verify persistence
8. Copy built-in workflow and verify it's editable

## Summary

This prompt creates:
- 6 new entities: WorkflowDefinition, WorkflowStep, StepTransition, WorkflowRulesetMapping, StepRulesetMapping, StepType enum
- Full repository and EF Core configuration
- REST API with 10+ endpoints for workflow CRUD and layout management
- Drawflow.js integration with Blazor via JavaScript interop
- Visual designer with step palette, canvas, and properties panel
- Built-in "Password Reset Standard" workflow as seed data
- Ruleset associations at workflow and step level
