# Claude Code Prompt: Phase T3 — Dynamic Agent Configuration UI

## Context

This is Phase T3 of ADR-011 (Composable Workflows & Pluggable Triggers). 
Phases T1, C1, T2, and C2 are complete. The trigger provider abstraction, 
sub-workflow designer, manual trigger, and sub-workflow execution engine all work.

**Project Location**: `/home/alton/Documents/lucid-it-agent`
**Admin Portal**: `/home/alton/Documents/lucid-it-agent/admin/dotnet`
**Python Agent**: `/home/alton/Documents/lucid-it-agent/agent`

## Overview

The agent edit dialog currently shows static sections (LLM, ServiceNow, Workflow) 
regardless of what the selected workflow actually needs. Phase T3 makes the agent 
form dynamic: when you pick a workflow, the form analyzes its steps and shows only 
the relevant service account pickers.

### What Changes

1. **New entity**: `AgentServiceAccountBinding` — flexible service account 
   assignments with Role/Qualifier
2. **Workflow requirements computation**: Server-side analysis of workflow steps 
   to determine what service accounts the workflow needs
3. **Dynamic agent dialog**: Form adapts when workflow selection changes
4. **Requirements API endpoint**: Returns structured requirements for a workflow
5. **Export update**: Include bindings in agent export

### What Stays the Same

- Existing `LlmServiceAccountId` and `ServiceNowAccountId` on Agent entity 
  remain (backward compatibility)
- The bindings system adds flexibility alongside the fixed fields
- All existing agents continue to work unchanged

## Task 1: Create AgentServiceAccountBinding Entity

**CREATE**: `src/LucidAdmin.Core/Entities/AgentServiceAccountBinding.cs`
```csharp
namespace LucidAdmin.Core.Entities;

/// 
/// Links an Agent to a ServiceAccount with a specific role.
/// Allows dynamic service account assignment based on workflow requirements.
/// Supplements (does not replace) the existing fixed FK fields on Agent.
/// 
public class AgentServiceAccountBinding : BaseEntity
{
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public Guid ServiceAccountId { get; set; }
    public ServiceAccount ServiceAccount { get; set; } = null!;

    /// 
    /// The role this service account plays for this agent.
    /// Values: "trigger", "llm", "execution", "notification"
    /// 
    public required string Role { get; set; }

    /// 
    /// Optional qualifier when multiple accounts of the same role exist.
    /// E.g., role="trigger", qualifier="servicenow" or role="trigger", qualifier="jira"
    /// 
    public string? Qualifier { get; set; }
}
```

## Task 2: Update Agent Entity

**MODIFY**: `src/LucidAdmin.Core/Entities/Agent.cs`

Add the bindings navigation property alongside existing fixed FKs:
```csharp
// === Service Account Bindings (Dynamic) ===

/// 
/// Dynamic service account assignments based on workflow requirements.
/// Supplements the fixed LlmServiceAccountId and ServiceNowAccountId fields.
/// 
public ICollection ServiceAccountBindings { get; set; } 
    = new List();
```

## Task 3: Configure EF Core for AgentServiceAccountBinding

**CREATE**: `src/LucidAdmin.Infrastructure/Data/Configurations/AgentServiceAccountBindingConfiguration.cs`
```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class AgentServiceAccountBindingConfiguration 
    : IEntityTypeConfiguration
{
    public void Configure(EntityTypeBuilder builder)
    {
        builder.ToTable("AgentServiceAccountBindings");

        builder.HasOne(b => b.Agent)
            .WithMany(a => a.ServiceAccountBindings)
            .HasForeignKey(b => b.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.ServiceAccount)
            .WithMany()
            .HasForeignKey(b => b.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.Role)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(b => b.Qualifier)
            .HasMaxLength(100);

        // Unique: one role+qualifier per agent
        builder.HasIndex(b => new { b.AgentId, b.Role, b.Qualifier })
            .IsUnique();
    }
}
```

**MODIFY**: `LucidDbContext.cs` — Add:
```csharp
public DbSet AgentServiceAccountBindings { get; set; }
```

## Task 4: Create EF Core Migration

Run:
```bash
cd /home/alton/Documents/lucid-it-agent/admin/dotnet
dotnet ef migrations add AddAgentServiceAccountBindings \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
```

## Task 5: Create Workflow Requirements Computation

**CREATE**: `src/LucidAdmin.Web/Services/WorkflowRequirementsService.cs`

This service analyzes a workflow's steps to determine what service accounts 
the agent needs to operate it.
```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using System.Text.Json;

namespace LucidAdmin.Web.Services;

/// 
/// Analyzes workflow steps to compute what service accounts an agent needs.
/// 
public class WorkflowRequirementsService
{
    /// 
    /// Compute requirements from a workflow's steps.
    /// 
    public WorkflowRequirements ComputeRequirements(WorkflowDefinition workflow)
    {
        var requirements = new WorkflowRequirements();

        if (workflow.Steps == null || !workflow.Steps.Any())
            return requirements;

        foreach (var step in workflow.Steps)
        {
            switch (step.StepType)
            {
                case StepType.Trigger:
                    var triggerConfig = ParseConfig(step.ConfigurationJson);
                    var triggerType = GetConfigValue(triggerConfig, "source") 
                        ?? workflow.TriggerType 
                        ?? "servicenow";
                    requirements.TriggerType = triggerType;
                    requirements.NeedsTriggerAccount = true;
                    // ServiceNow needs assignment group
                    if (triggerType.Equals("servicenow", StringComparison.OrdinalIgnoreCase))
                    {
                        requirements.NeedsAssignmentGroup = true;
                    }
                    break;

                case StepType.Classify:
                    requirements.NeedsLlm = true;
                    break;

                case StepType.Execute:
                    var execConfig = ParseConfig(step.ConfigurationJson);
                    var capability = GetConfigValue(execConfig, "capability");
                    if (!string.IsNullOrEmpty(capability) && 
                        !requirements.RequiredCapabilities.Contains(capability))
                    {
                        requirements.RequiredCapabilities.Add(capability);
                    }
                    break;

                case StepType.Notify:
                    var notifyConfig = ParseConfig(step.ConfigurationJson);
                    var channel = GetConfigValue(notifyConfig, "channel") ?? "ticket";
                    if (!requirements.NotificationChannels.Contains(channel))
                    {
                        requirements.NotificationChannels.Add(channel);
                    }
                    break;

                case StepType.SubWorkflow:
                    // Sub-workflows inherit parent context; their requirements
                    // are resolved at runtime through the parent agent's config.
                    // Mark that sub-workflows exist for informational display.
                    requirements.HasSubWorkflows = true;
                    break;
            }
        }

        return requirements;
    }

    private static Dictionary ParseConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary();

        try
        {
            var doc = JsonDocument.Parse(json);
            var result = new Dictionary();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null 
                    ? null 
                    : prop.Value.ToString();
            }
            return result;
        }
        catch
        {
            return new Dictionary();
        }
    }

    private static string? GetConfigValue(Dictionary config, string key)
    {
        return config.TryGetValue(key, out var value) ? value : null;
    }
}

/// 
/// What an agent needs to run a specific workflow.
/// 
public class WorkflowRequirements
{
    public string? TriggerType { get; set; }
    public bool NeedsTriggerAccount { get; set; }
    public bool NeedsAssignmentGroup { get; set; }
    public bool NeedsLlm { get; set; }
    public List RequiredCapabilities { get; set; } = new();
    public List NotificationChannels { get; set; } = new();
    public bool HasSubWorkflows { get; set; }
}
```

## Task 6: Add Workflow Requirements API Endpoint

**MODIFY**: `src/LucidAdmin.Web/Endpoints/WorkflowEndpoints.cs`

Add an endpoint that returns requirements for a specific workflow:
```csharp
// Add this route inside MapWorkflowEndpoints:
group.MapGet("/{id:guid}/requirements", async (
    Guid id,
    IWorkflowService workflowService,
    WorkflowRequirementsService requirementsService) =>
{
    var workflow = await workflowService.GetByIdAsync(id);
    if (workflow == null)
        return Results.NotFound();

    // Make sure steps are loaded
    // (may need explicit Include if not already eager-loaded)

    var requirements = requirementsService.ComputeRequirements(workflow);
    return Results.Ok(requirements);
});
```

Register `WorkflowRequirementsService` in DI (Program.cs):
```csharp
builder.Services.AddSingleton();
```

## Task 7: Update AgentDialog.razor — Dynamic Form

**MODIFY**: `src/LucidAdmin.Web/Components/Pages/AgentDialog.razor`

The key behavioral change: when the user selects a workflow, the form calls the 
requirements endpoint and dynamically shows/hides sections.

Changes needed:

1. Move "Workflow" section to the TOP of the form (above LLM and ServiceNow)
2. Add an `OnWorkflowChanged` handler that calls `/api/v1/workflows/{id}/requirements`
3. Show/hide LLM section based on `requirements.NeedsLlm`
4. Show/hide ServiceNow section based on `requirements.TriggerType == "servicenow"`
5. Show/hide Assignment Group based on `requirements.NeedsAssignmentGroup`
6. Show a "Trigger: Manual" info box when `requirements.TriggerType == "manual"` 
   (no service account needed)
7. Show capabilities as read-only info (resolved via capability routing at runtime)
8. Show sub-workflow indicator if `requirements.HasSubWorkflows`

### Updated form structure:
```
WORKFLOW CONFIGURATION          ← MOVED TO TOP
  [Workflow picker dropdown]
  "This workflow requires:"     ← Dynamic section header

  (if NeedsLlm)
  🔑 LLM Provider
    [LLM service account dropdown]

  (if TriggerType == "servicenow") 
  🎯 Trigger: ServiceNow
    [ServiceNow service account dropdown]
    [Assignment Group text field]

  (if TriggerType == "manual")
  🎯 Trigger: Manual
    ℹ️ "No service account needed. Work items are submitted manually."

  (if RequiredCapabilities.Any())
  ⚡ Execution Capabilities
    ℹ️ "Requires: ad-password-reset" (read-only, resolved at runtime)

  (if HasSubWorkflows)
  📋 Sub-Workflows
    ℹ️ "This workflow uses sub-workflows. Their requirements are 
        resolved through this agent's configuration."

IDENTITY                        ← Moved below workflow
  [Name, Display Name, Description, Host Name]

STATUS
  [Enabled toggle]
```

### State management additions:
```csharp
private WorkflowRequirements? _requirements;
private bool _loadingRequirements;

private async Task OnWorkflowChanged(Guid? workflowId)
{
    _model.WorkflowDefinitionId = workflowId;
    _requirements = null;

    if (workflowId.HasValue)
    {
        _loadingRequirements = true;
        StateHasChanged();
        try
        {
            _requirements = await Http.GetFromJsonAsync(
                $"/api/v1/workflows/{workflowId}/requirements");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load workflow requirements: {ex.Message}", 
                Severity.Warning);
        }
        finally
        {
            _loadingRequirements = false;
            StateHasChanged();
        }
    }
}
```

### Important: Load requirements when editing existing agent

In `OnInitializedAsync`, after populating the model from `ExistingAgent`, 
if `_model.WorkflowDefinitionId` has a value, call `OnWorkflowChanged` 
to load requirements for the existing workflow.

## Task 8: Update Agent Export to Include Bindings

**MODIFY**: `src/LucidAdmin.Web/Models/AgentExportModels.cs`

Add to `AgentExportInfo`:
```csharp
public List? ServiceAccountBindings { get; init; }
```

**CREATE** new record:
```csharp
public record ServiceAccountBindingExportInfo
{
    public required string Role { get; init; }
    public string? Qualifier { get; init; }
    public required string ServiceAccountName { get; init; }
    public required string ProviderType { get; init; }
}
```

**MODIFY**: `AgentExportService.cs` — include bindings in the agent export:
- Load `ServiceAccountBindings` with Include when building export
- Map each binding to `ServiceAccountBindingExportInfo`

## Task 9: Update Python Agent Models

**MODIFY**: `agent/src/agent/runtime/models.py`

Add to `AgentExportInfo`:
```python
class ServiceAccountBindingInfo(BaseModel):
    """Dynamic service account binding for an agent."""
    role: str
    qualifier: str | None = None
    service_account_name: str
    provider_type: str

class AgentExportInfo(BaseModel):
    """Agent metadata from export."""
    id: str
    name: str
    display_name: str | None = None
    description: str | None = None
    is_enabled: bool = True
    service_account_bindings: list[ServiceAccountBindingInfo] = Field(default_factory=list)
```

## Task 10: Build Verification and Commit

1. Build the .NET Admin Portal: `dotnet build`
2. Apply the migration: `dotnet ef database update`
3. Verify the requirements endpoint works
4. Test the dynamic form behavior
5. Commit with message: `feat: dynamic agent configuration from workflow requirements (T3)`

## Key Design Notes

- The `WorkflowRequirements` model is a RESPONSE-ONLY model — it's computed 
  from the workflow's steps, never stored. Every time the workflow changes, 
  requirements are recomputed.
- The bindings table is an ADDITION, not a replacement. The existing 
  `LlmServiceAccountId` and `ServiceNowAccountId` fields on Agent remain 
  and continue to work. The bindings provide extensibility for future 
  trigger types (Jira, email) that don't map to the fixed fields.
- Assignment group stays on the Agent entity (it's operational config, 
  not a service account). The requirements just indicate whether it's needed.
- For the MVP, the form still saves to the fixed FK fields. The bindings 
  table is infrastructure for when we add Jira/email triggers that need 
  their own service account types.
