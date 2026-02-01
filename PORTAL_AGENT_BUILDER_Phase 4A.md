# Claude Code Prompt: Agent Definition Export (Phase 4A)

## Context

This is Phase 4A of the Portal-Agent feature set. We're building an export endpoint that 
collects all linked data for an agent (workflow, rulesets, examples, service accounts) 
and returns a complete JSON agent definition. This JSON will later be used by a Python 
runtime (Phase 4B) to execute the agent dynamically.

**Project Location**: `/home/alton/Documents/lucid-it-agent/admin/dotnet`

**Key Design Decisions**:
1. **Capabilities**: Export only capability NAMES, not resolved URLs. The Python runtime 
   will query the capability router at execution time for dynamic tool server resolution.
2. **Credentials**: Export credential REFERENCES (storage type + reference key), not 
   actual secrets. Python runtime resolves credentials at startup.

**Existing Entities** (already implemented in Phases 1-3):
- Agent (with WorkflowDefinitionId, LlmServiceAccountId, ServiceNowAccountId)
- WorkflowDefinition (with Steps, RulesetMappings, ExampleSetId)
- WorkflowStep (with StepType, ConfigurationJson, Transitions)
- StepTransition (with FromStepId, ToStepId, Condition, Label)
- Ruleset / Rule
- ExampleSet / Example
- ServiceAccount (with ProviderType, ProviderConfigJson, CredentialStorage, CredentialReference)
- ToolServer, Capability, CapabilityMapping

## Goal

Create a new API endpoint `GET /api/agents/{id}/export` that:
1. Fetches the agent and all related entities
2. Assembles them into a self-contained JSON structure
3. Returns everything needed to configure the agent (credentials resolved at runtime)

## Target JSON Structure
```json
{
  "version": "1.0",
  "exportedAt": "2026-02-01T12:00:00Z",
  
  "agent": {
    "id": "guid",
    "name": "lucid-agent-01",
    "displayName": "Lucid Helpdesk Agent",
    "description": "Automates Level 1 IT tickets",
    "isEnabled": true
  },
  
  "llmProvider": {
    "serviceAccountName": "ollama-local",
    "providerType": "llm-ollama",
    "config": {
      "base_url": "http://localhost:11434",
      "model": "llama3.1",
      "temperature": "0.1"
    },
    "credentials": {
      "storage": "none"
    }
  },
  
  "serviceNow": {
    "serviceAccountName": "servicenow-pdi",
    "providerType": "servicenow-basic",
    "config": {
      "instance_url": "https://dev12345.service-now.com",
      "username": "admin"
    },
    "credentials": {
      "storage": "environment",
      "reference": "SERVICENOW_PASSWORD"
    },
    "assignmentGroup": "Helpdesk"
  },
  
  "workflow": {
    "id": "guid",
    "name": "helpdesk-workflow",
    "version": "1.0.0",
    "triggerType": "ServiceNow",
    "triggerConfig": {
      "pollIntervalSeconds": 30
    },
    
    "steps": [
      {
        "id": "step-guid-1",
        "name": "classify-ticket",
        "stepType": "Classify",
        "sortOrder": 1,
        "positionX": 100,
        "positionY": 200,
        "configuration": {
          "useExampleSet": "password-reset-examples"
        },
        "rulesets": ["classification-rules"],
        "transitions": [
          {
            "toStepId": "step-guid-2",
            "condition": "confidence >= 0.8",
            "label": "high-confidence"
          },
          {
            "toStepId": "step-guid-3",
            "condition": "confidence < 0.8",
            "label": "escalate"
          }
        ]
      },
      {
        "id": "step-guid-2",
        "name": "execute-password-reset",
        "stepType": "Execute",
        "sortOrder": 2,
        "configuration": {
          "capability": "ad-password-reset"
        },
        "rulesets": ["security-rules"],
        "transitions": [
          {
            "toStepId": "step-guid-4",
            "label": "success"
          },
          {
            "toStepId": "step-guid-5",
            "condition": "error != null",
            "label": "failure"
          }
        ]
      },
      {
        "id": "step-guid-3",
        "name": "escalate-to-human",
        "stepType": "Escalate",
        "sortOrder": 3,
        "configuration": {
          "targetGroup": "Level 2 Support",
          "reason": "Low confidence classification"
        },
        "rulesets": [],
        "transitions": [
          {
            "toStepId": "step-guid-6",
            "label": "done"
          }
        ]
      },
      {
        "id": "step-guid-4",
        "name": "notify-user",
        "stepType": "Notify",
        "sortOrder": 4,
        "configuration": {
          "template": "password-reset-success",
          "channel": "ticket-comment"
        },
        "rulesets": ["communication-rules"],
        "transitions": [
          {
            "toStepId": "step-guid-6",
            "label": "done"
          }
        ]
      },
      {
        "id": "step-guid-5",
        "name": "notify-failure",
        "stepType": "Notify",
        "sortOrder": 5,
        "configuration": {
          "template": "action-failed",
          "channel": "ticket-comment"
        },
        "rulesets": [],
        "transitions": [
          {
            "toStepId": "step-guid-3",
            "label": "escalate"
          }
        ]
      },
      {
        "id": "step-guid-6",
        "name": "close-ticket",
        "stepType": "UpdateTicket",
        "sortOrder": 6,
        "configuration": {
          "state": "resolved",
          "closeCode": "automated"
        },
        "rulesets": [],
        "transitions": []
      }
    ],
    
    "workflowRulesets": ["security-rules", "audit-rules"]
  },
  
  "rulesets": {
    "classification-rules": {
      "id": "guid",
      "name": "classification-rules",
      "displayName": "Classification Rules",
      "category": "Classification",
      "rules": [
        {
          "id": "rule-guid-1",
          "name": "classify-by-intent",
          "ruleText": "Classify tickets based on user intent, not exact keywords",
          "priority": 100
        },
        {
          "id": "rule-guid-2",
          "name": "extract-username",
          "ruleText": "Extract the affected username from the ticket description",
          "priority": 200
        }
      ]
    },
    "security-rules": {
      "id": "guid",
      "name": "security-rules",
      "displayName": "Security Rules",
      "category": "Security",
      "rules": [
        {
          "id": "rule-guid-3",
          "name": "no-admin-passwords",
          "ruleText": "Never reset passwords for accounts in Domain Admins or Enterprise Admins groups",
          "priority": 100
        },
        {
          "id": "rule-guid-4",
          "name": "verify-requester",
          "ruleText": "Verify the ticket requester has authority to request actions for the affected user",
          "priority": 200
        }
      ]
    },
    "communication-rules": {
      "id": "guid",
      "name": "communication-rules",
      "displayName": "Communication Rules",
      "category": "Communication",
      "rules": [
        {
          "id": "rule-guid-5",
          "name": "professional-tone",
          "ruleText": "Use professional, friendly tone in all customer communications",
          "priority": 100
        }
      ]
    },
    "audit-rules": {
      "id": "guid",
      "name": "audit-rules",
      "displayName": "Audit Rules",
      "category": "Audit",
      "rules": [
        {
          "id": "rule-guid-6",
          "name": "log-all-actions",
          "ruleText": "Log all actions taken with ticket number, username, and timestamp",
          "priority": 100
        }
      ]
    }
  },
  
  "exampleSets": {
    "password-reset-examples": {
      "id": "guid",
      "name": "password-reset-examples",
      "displayName": "Password Reset Classification Examples",
      "examples": [
        {
          "id": "example-guid-1",
          "inputText": "I forgot my password and can't log in to my computer",
          "expectedOutputJson": "{\"ticket_type\": \"password_reset\", \"confidence\": 0.95, \"affected_user\": null, \"reasoning\": \"User explicitly states forgot password\"}",
          "notes": "Clear password reset request without specific user"
        },
        {
          "id": "example-guid-2",
          "inputText": "Please reset the password for jsmith@montanifarms.com, he called and said he's locked out",
          "expectedOutputJson": "{\"ticket_type\": \"password_reset\", \"confidence\": 0.98, \"affected_user\": \"jsmith\", \"reasoning\": \"Explicit password reset request with target user\"}",
          "notes": "Password reset with explicit user mention"
        },
        {
          "id": "example-guid-3",
          "inputText": "Add me to the Marketing shared drive",
          "expectedOutputJson": "{\"ticket_type\": \"file_permission\", \"confidence\": 0.90, \"affected_user\": null, \"target_resource\": \"Marketing shared drive\", \"reasoning\": \"Request for file share access\"}",
          "notes": "File permission request - not password reset"
        }
      ]
    }
  },
  
  "requiredCapabilities": [
    "ad-password-reset",
    "ad-group-add",
    "ad-group-remove"
  ]
}
```

## Implementation Tasks

### Task 1: Create Export Response Models

Create new file `src/LucidAdmin.Web/Api/Models/Responses/AgentExportResponse.cs`:
```csharp
namespace LucidAdmin.Web.Api.Models.Responses;

/// 
/// Complete agent definition for export/runtime execution.
/// 
public record AgentExportResponse
{
    public required string Version { get; init; }
    public required DateTime ExportedAt { get; init; }
    public required AgentExportInfo Agent { get; init; }
    public ProviderExportInfo? LlmProvider { get; init; }
    public ServiceNowExportInfo? ServiceNow { get; init; }
    public WorkflowExportInfo? Workflow { get; init; }
    public required Dictionary Rulesets { get; init; }
    public required Dictionary ExampleSets { get; init; }
    public required List RequiredCapabilities { get; init; }
}

public record AgentExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public required bool IsEnabled { get; init; }
}

public record CredentialReference
{
    /// 
    /// Storage type: "none", "environment", "vault", "gmsa"
    /// 
    public required string Storage { get; init; }
    
    /// 
    /// Reference key for the credential (e.g., env var name, vault path)
    /// 
    public string? Reference { get; init; }
}

public record ProviderExportInfo
{
    public required string ServiceAccountName { get; init; }
    public required string ProviderType { get; init; }
    public required Dictionary Config { get; init; }
    public required CredentialReference Credentials { get; init; }
}

public record ServiceNowExportInfo
{
    public required string ServiceAccountName { get; init; }
    public required string ProviderType { get; init; }
    public required Dictionary Config { get; init; }
    public required CredentialReference Credentials { get; init; }
    public string? AssignmentGroup { get; init; }
}

public record WorkflowExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? TriggerType { get; init; }
    public Dictionary? TriggerConfig { get; init; }
    public required List Steps { get; init; }
    public required List WorkflowRulesets { get; init; }
}

public record WorkflowStepExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string StepType { get; init; }
    public int SortOrder { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public Dictionary? Configuration { get; init; }
    public required List Rulesets { get; init; }
    public required List Transitions { get; init; }
}

public record StepTransitionExportInfo
{
    public required Guid ToStepId { get; init; }
    public string? Condition { get; init; }
    public string? Label { get; init; }
}

public record RulesetExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Category { get; init; }
    public required List Rules { get; init; }
}

public record RuleExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string RuleText { get; init; }
    public int Priority { get; init; }
}

public record ExampleSetExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public required List Examples { get; init; }
}

public record ExampleExportInfo
{
    public required Guid Id { get; init; }
    public required string InputText { get; init; }
    public required string ExpectedOutputJson { get; init; }
    public string? Notes { get; init; }
}
```

### Task 2: Create Export Service

Create new file `src/LucidAdmin.Web/Services/AgentExportService.cs`:
```csharp
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Api.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Services;

public interface IAgentExportService
{
    Task ExportAgentAsync(Guid agentId, CancellationToken ct = default);
    Task ExportAgentByNameAsync(string agentName, CancellationToken ct = default);
}

public class AgentExportService : IAgentExportService
{
    private readonly LucidDbContext _db;
    private readonly ILogger _logger;
    private const string ExportVersion = "1.0";
    
    public AgentExportService(LucidDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task ExportAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        var agent = await LoadAgentWithAllRelationsAsync(agentId, ct);
        if (agent == null)
        {
            _logger.LogWarning("Agent {AgentId} not found for export", agentId);
            return null;
        }
        
        return BuildExportResponse(agent);
    }
    
    public async Task ExportAgentByNameAsync(string agentName, CancellationToken ct = default)
    {
        var agentId = await _db.Agents
            .Where(a => a.Name == agentName)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);
            
        if (agentId == Guid.Empty)
        {
            _logger.LogWarning("Agent with name '{AgentName}' not found for export", agentName);
            return null;
        }
        
        return await ExportAgentAsync(agentId, ct);
    }
    
    private async Task LoadAgentWithAllRelationsAsync(Guid agentId, CancellationToken ct)
    {
        // Load agent with all navigation properties needed for export
        return await _db.Agents
            .Include(a => a.LlmServiceAccount)
            .Include(a => a.ServiceNowAccount)
            .Include(a => a.WorkflowDefinition)
                .ThenInclude(w => w!.Steps.OrderBy(s => s.SortOrder))
                    .ThenInclude(s => s.OutgoingTransitions.OrderBy(t => t.SortOrder))
            .Include(a => a.WorkflowDefinition)
                .ThenInclude(w => w!.Steps)
                    .ThenInclude(s => s.RulesetMappings)
                        .ThenInclude(m => m.Ruleset)
                            .ThenInclude(r => r!.Rules.OrderBy(rule => rule.Priority))
            .Include(a => a.WorkflowDefinition)
                .ThenInclude(w => w!.RulesetMappings)
                    .ThenInclude(m => m.Ruleset)
                        .ThenInclude(r => r!.Rules.OrderBy(rule => rule.Priority))
            .Include(a => a.WorkflowDefinition)
                .ThenInclude(w => w!.ExampleSet)
                    .ThenInclude(e => e!.Examples.OrderBy(ex => ex.SortOrder))
            .AsSplitQuery()  // Optimize for multiple includes
            .FirstOrDefaultAsync(a => a.Id == agentId, ct);
    }
    
    private AgentExportResponse BuildExportResponse(Agent agent)
    {
        var rulesets = CollectAllRulesets(agent);
        var exampleSets = CollectExampleSets(agent);
        var requiredCapabilities = CollectRequiredCapabilities(agent);
        
        return new AgentExportResponse
        {
            Version = ExportVersion,
            ExportedAt = DateTime.UtcNow,
            Agent = MapAgentInfo(agent),
            LlmProvider = MapProviderInfo(agent.LlmServiceAccount),
            ServiceNow = MapServiceNowInfo(agent.ServiceNowAccount, agent.AssignmentGroup),
            Workflow = MapWorkflowInfo(agent.WorkflowDefinition),
            Rulesets = rulesets,
            ExampleSets = exampleSets,
            RequiredCapabilities = requiredCapabilities
        };
    }
    
    private AgentExportInfo MapAgentInfo(Agent agent)
    {
        return new AgentExportInfo
        {
            Id = agent.Id,
            Name = agent.Name,
            DisplayName = agent.DisplayName,
            Description = agent.Description,
            IsEnabled = agent.IsEnabled
        };
    }
    
    private ProviderExportInfo? MapProviderInfo(ServiceAccount? account)
    {
        if (account == null) return null;
        
        return new ProviderExportInfo
        {
            ServiceAccountName = account.Name,
            ProviderType = account.ProviderType,
            Config = ParseConfigJson(account.ProviderConfigJson),
            Credentials = new CredentialReference
            {
                Storage = account.CredentialStorage ?? "none",
                Reference = account.CredentialReference
            }
        };
    }
    
    private ServiceNowExportInfo? MapServiceNowInfo(ServiceAccount? account, string? assignmentGroup)
    {
        if (account == null) return null;
        
        return new ServiceNowExportInfo
        {
            ServiceAccountName = account.Name,
            ProviderType = account.ProviderType,
            Config = ParseConfigJson(account.ProviderConfigJson),
            Credentials = new CredentialReference
            {
                Storage = account.CredentialStorage ?? "none",
                Reference = account.CredentialReference
            },
            AssignmentGroup = assignmentGroup
        };
    }
    
    private WorkflowExportInfo? MapWorkflowInfo(WorkflowDefinition? workflow)
    {
        if (workflow == null) return null;
        
        var workflowRulesets = workflow.RulesetMappings
            .OrderBy(m => m.Priority)
            .Select(m => m.Ruleset?.Name ?? "")
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
        
        return new WorkflowExportInfo
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Version = workflow.Version,
            TriggerType = workflow.TriggerType,
            TriggerConfig = ParseConfigJsonAsObject(workflow.TriggerConfigJson),
            Steps = workflow.Steps
                .OrderBy(s => s.SortOrder)
                .Select(MapStepInfo)
                .ToList(),
            WorkflowRulesets = workflowRulesets
        };
    }
    
    private WorkflowStepExportInfo MapStepInfo(WorkflowStep step)
    {
        var stepRulesets = step.RulesetMappings
            .OrderBy(m => m.Priority)
            .Select(m => m.Ruleset?.Name ?? "")
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
        
        return new WorkflowStepExportInfo
        {
            Id = step.Id,
            Name = step.Name,
            StepType = step.StepType.ToString(),
            SortOrder = step.SortOrder,
            PositionX = step.PositionX,
            PositionY = step.PositionY,
            Configuration = ParseConfigJsonAsObject(step.ConfigurationJson),
            Rulesets = stepRulesets,
            Transitions = step.OutgoingTransitions
                .OrderBy(t => t.SortOrder)
                .Select(t => new StepTransitionExportInfo
                {
                    ToStepId = t.ToStepId,
                    Condition = t.Condition,
                    Label = t.Label
                })
                .ToList()
        };
    }
    
    private Dictionary CollectAllRulesets(Agent agent)
    {
        var rulesets = new Dictionary();
        
        if (agent.WorkflowDefinition == null) return rulesets;
        
        // Collect workflow-level rulesets
        foreach (var mapping in agent.WorkflowDefinition.RulesetMappings)
        {
            if (mapping.Ruleset != null && !rulesets.ContainsKey(mapping.Ruleset.Name))
            {
                rulesets[mapping.Ruleset.Name] = MapRulesetInfo(mapping.Ruleset);
            }
        }
        
        // Collect step-level rulesets
        foreach (var step in agent.WorkflowDefinition.Steps)
        {
            foreach (var mapping in step.RulesetMappings)
            {
                if (mapping.Ruleset != null && !rulesets.ContainsKey(mapping.Ruleset.Name))
                {
                    rulesets[mapping.Ruleset.Name] = MapRulesetInfo(mapping.Ruleset);
                }
            }
        }
        
        return rulesets;
    }
    
    private RulesetExportInfo MapRulesetInfo(Ruleset ruleset)
    {
        return new RulesetExportInfo
        {
            Id = ruleset.Id,
            Name = ruleset.Name,
            DisplayName = ruleset.DisplayName,
            Category = ruleset.Category,
            Rules = ruleset.Rules
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Name)
                .Select(r => new RuleExportInfo
                {
                    Id = r.Id,
                    Name = r.Name,
                    RuleText = r.RuleText,
                    Priority = r.Priority
                })
                .ToList()
        };
    }
    
    private Dictionary CollectExampleSets(Agent agent)
    {
        var exampleSets = new Dictionary();
        
        // Add workflow's example set
        var workflowExampleSet = agent.WorkflowDefinition?.ExampleSet;
        if (workflowExampleSet != null)
        {
            exampleSets[workflowExampleSet.Name] = new ExampleSetExportInfo
            {
                Id = workflowExampleSet.Id,
                Name = workflowExampleSet.Name,
                DisplayName = workflowExampleSet.DisplayName,
                Examples = workflowExampleSet.Examples
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new ExampleExportInfo
                    {
                        Id = e.Id,
                        InputText = e.InputText,
                        ExpectedOutputJson = e.ExpectedOutputJson,
                        Notes = e.Notes
                    })
                    .ToList()
            };
        }
        
        // TODO: If steps can reference their own example sets, collect those too
        
        return exampleSets;
    }
    
    private List CollectRequiredCapabilities(Agent agent)
    {
        var capabilities = new HashSet();
        
        if (agent.WorkflowDefinition == null) return capabilities.ToList();
        
        foreach (var step in agent.WorkflowDefinition.Steps)
        {
            if (step.StepType == WorkflowStepType.Execute && !string.IsNullOrEmpty(step.ConfigurationJson))
            {
                var config = ParseConfigJsonAsObject(step.ConfigurationJson);
                if (config != null && config.TryGetValue("capability", out var capObj))
                {
                    var capName = capObj?.ToString();
                    if (!string.IsNullOrEmpty(capName))
                    {
                        capabilities.Add(capName);
                    }
                }
            }
        }
        
        return capabilities.OrderBy(c => c).ToList();
    }
    
    private Dictionary ParseConfigJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary();
        
        try
        {
            return JsonSerializer.Deserialize<Dictionary>(json) 
                ?? new Dictionary();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse config JSON: {Json}", json);
            return new Dictionary();
        }
    }
    
    private Dictionary? ParseConfigJsonAsObject(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        
        try
        {
            return JsonSerializer.Deserialize<Dictionary>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse config JSON as object: {Json}", json);
            return null;
        }
    }
}
```

### Task 3: Add Export Endpoints

Update `src/LucidAdmin.Web/Api/Endpoints/AgentEndpoints.cs` to add export endpoints:
```csharp
// Add these endpoints to the existing MapAgentEndpoints method

// Export agent by ID
group.MapGet("/{id:guid}/export", async (
    Guid id,
    IAgentExportService exportService,
    CancellationToken ct) =>
{
    var export = await exportService.ExportAgentAsync(id, ct);
    return export is null 
        ? Results.NotFound(new { error = $"Agent with ID {id} not found" }) 
        : Results.Ok(export);
})
.WithName("ExportAgent")
.WithSummary("Export complete agent definition")
.WithDescription("Returns all linked data (workflow, rulesets, examples) as a self-contained JSON document. Credentials are exported as references only.")
.Produces(200)
.Produces(404);

// Export agent by name (more convenient for CLI/scripts)
group.MapGet("/by-name/{name}/export", async (
    string name,
    IAgentExportService exportService,
    CancellationToken ct) =>
{
    var export = await exportService.ExportAgentByNameAsync(name, ct);
    return export is null 
        ? Results.NotFound(new { error = $"Agent with name '{name}' not found" }) 
        : Results.Ok(export);
})
.WithName("ExportAgentByName")
.WithSummary("Export complete agent definition by name")
.Produces(200)
.Produces(404);
```

Don't forget to add the using statement at the top:
```csharp
using LucidAdmin.Web.Services;
```

### Task 4: Register Service in DI

Update `src/LucidAdmin.Web/Program.cs` to register the export service:
```csharp
// Add with other service registrations (after AddInfrastructure)
builder.Services.AddScoped();
```

### Task 5: Add UI Export Button (Blazor)

Update the Agent details/edit page to include an Export button.

Create or update `src/LucidAdmin.Web/Components/Pages/Agents/Details.razor`:
```razor
@page "/agents/{Id:guid}"
@using LucidAdmin.Web.Api.Models.Responses
@inject HttpClient Http
@inject IJSRuntime JS
@inject NavigationManager Navigation

Agent Details


    @(_agent?.DisplayName ?? _agent?.Name ?? "Agent Details")
    
        
            @if (_exporting)
            {
                
            }
             Export
        
        
             Edit
        
    


@if (_loading)
{
    
        
            Loading...
        
    
}
else if (_agent == null)
{
    Agent not found.
}
else
{
    
    
        
            Agent Information
        
        
            
                Name
                @_agent.Name
                
                Display Name
                @(_agent.DisplayName ?? "-")
                
                Description
                @(_agent.Description ?? "-")
                
                Status
                
                    @if (_agent.IsEnabled)
                    {
                        Enabled
                    }
                    else
                    {
                        Disabled
                    }
                
            
        
    
    
    
}

@if (!string.IsNullOrEmpty(_errorMessage))
{
    
        @_errorMessage
        
    
}

@code {
    [Parameter]
    public Guid Id { get; set; }
    
    private AgentResponse? _agent;
    private bool _loading = true;
    private bool _exporting = false;
    private string? _errorMessage;
    
    protected override async Task OnInitializedAsync()
    {
        await LoadAgent();
    }
    
    private async Task LoadAgent()
    {
        _loading = true;
        try
        {
            _agent = await Http.GetFromJsonAsync($"/api/agents/{Id}");
        }
        catch (Exception ex)
        {
            _errorMessage = $"Failed to load agent: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }
    
    private async Task ExportAgent()
    {
        _exporting = true;
        _errorMessage = null;
        
        try
        {
            var response = await Http.GetAsync($"/api/agents/{Id}/export");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var fileName = $"{_agent?.Name ?? "agent"}_export_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                
                // Trigger browser download
                await JS.InvokeVoidAsync("downloadFile", fileName, json, "application/json");
            }
            else
            {
                _errorMessage = $"Export failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            _exporting = false;
        }
    }
}
```

Add the JavaScript helper function in `wwwroot/js/app.js`:
```javascript
// Download file helper for Blazor
function downloadFile(fileName, content, mimeType) {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
```

Make sure the script is included in `App.razor` or `_Host.cshtml`:
```html

```

### Task 6: Create Integration Test

Create `tests/LucidAdmin.Web.Tests/Api/AgentExportEndpointsTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using LucidAdmin.Web.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LucidAdmin.Web.Tests.Api;

public class AgentExportEndpointsTests : IClassFixture<WebApplicationFactory>
{
    private readonly HttpClient _client;
    
    public AgentExportEndpointsTests(WebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task ExportAgent_NonExistentId_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/api/agents/{Guid.NewGuid()}/export");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    
    [Fact]
    public async Task ExportAgentByName_NonExistentName_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/agents/by-name/non-existent-agent/export");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    
    // Note: Full integration tests require seeding test data
    // These would be added once we have a test database seeder
}
```

## Verification Steps

After implementation, verify with these commands:
```bash
# Build the project
cd /home/alton/Documents/lucid-it-agent/admin/dotnet
dotnet build

# Run tests
dotnet test

# Start the portal
cd src/LucidAdmin.Web
dotnet run

# In another terminal, test the export endpoint
# First, get an agent ID from the UI or database

# Test export by ID
curl http://localhost:5000/api/agents/{agent-guid}/export | jq

# Test export by name (assuming you have an agent named "test-agent")
curl http://localhost:5000/api/agents/by-name/test-agent/export | jq

# Save to file for inspection
curl http://localhost:5000/api/agents/by-name/test-agent/export > agent_export.json
cat agent_export.json | jq
```

## Expected Output Validation

The exported JSON should:
- [ ] Have `version` set to "1.0"
- [ ] Have `exportedAt` as a valid ISO 8601 timestamp
- [ ] Have `agent` with id, name, and isEnabled
- [ ] Have `llmProvider` with credentials.storage (NOT the actual credential)
- [ ] Have `serviceNow` with credentials.storage (NOT the actual credential)
- [ ] Have `workflow` with steps ordered by sortOrder
- [ ] Have `rulesets` dictionary with all referenced rulesets (both workflow and step level)
- [ ] Have `exampleSets` dictionary with the workflow's example set
- [ ] Have `requiredCapabilities` as a list of capability names (NOT resolved URLs)

## Notes

- Credentials are intentionally NOT exported - only storage type and reference
- Capabilities are exported as names only - Python runtime will query capability router
- The export is deterministic (same input = same output, except timestamp)
- Steps are ordered by SortOrder for consistent execution order
- Rules within rulesets are ordered by Priority
