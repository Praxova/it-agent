using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Services;

public interface IAgentExportService
{
    Task<AgentExportResponse?> ExportAgentAsync(Guid agentId, CancellationToken ct = default);
    Task<AgentExportResponse?> ExportAgentByNameAsync(string agentName, CancellationToken ct = default);
}

public class AgentExportService : IAgentExportService
{
    private readonly LucidDbContext _db;
    private readonly ILogger<AgentExportService> _logger;
    private const string ExportVersion = "1.0";

    public AgentExportService(LucidDbContext db, ILogger<AgentExportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AgentExportResponse?> ExportAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        var agent = await LoadAgentWithAllRelationsAsync(agentId, ct);
        if (agent == null)
        {
            _logger.LogWarning("Agent {AgentId} not found for export", agentId);
            return null;
        }

        return await BuildExportResponseAsync(agent, ct);
    }

    public async Task<AgentExportResponse?> ExportAgentByNameAsync(string agentName, CancellationToken ct = default)
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

    private async Task<Agent?> LoadAgentWithAllRelationsAsync(Guid agentId, CancellationToken ct)
    {
        // Load agent with all navigation properties needed for export
        // Note: FromStep is automatically populated by EF Core's fix-up when loading OutgoingTransitions
        return await _db.Agents
            .Include(a => a.LlmServiceAccount)
            .Include(a => a.ServiceNowAccount)
            .Include(a => a.ServiceAccountBindings)
                .ThenInclude(b => b.ServiceAccount)
            .Include(a => a.WorkflowDefinition)
                .ThenInclude(w => w!.Steps.OrderBy(s => s.SortOrder))
                    .ThenInclude(s => s.OutgoingTransitions.OrderBy(t => t.SortOrder))
                        .ThenInclude(t => t.ToStep)
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

    private async Task<AgentExportResponse> BuildExportResponseAsync(Agent agent, CancellationToken ct)
    {
        var rulesets = CollectAllRulesets(agent);
        var exampleSets = CollectExampleSets(agent);
        var requiredCapabilities = CollectRequiredCapabilities(agent);

        // Collect sub-workflow definitions referenced by SubWorkflow steps
        var subWorkflows = new Dictionary<string, WorkflowExportInfo>();
        if (agent.WorkflowDefinition != null)
        {
            var visited = new HashSet<Guid>();
            await CollectSubWorkflowsRecursiveAsync(
                agent.WorkflowDefinition, subWorkflows, visited, rulesets, ct);
        }

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
            RequiredCapabilities = requiredCapabilities,
            SubWorkflows = subWorkflows
        };
    }

    private AgentExportInfo MapAgentInfo(Agent agent)
    {
        var bindings = agent.ServiceAccountBindings?
            .Select(b => new ServiceAccountBindingExportInfo
            {
                Role = b.Role,
                Qualifier = b.Qualifier,
                ServiceAccountName = b.ServiceAccount?.Name ?? "",
                ProviderType = b.ServiceAccount?.Provider ?? ""
            })
            .ToList();

        return new AgentExportInfo
        {
            Id = agent.Id,
            Name = agent.Name,
            DisplayName = agent.DisplayName,
            Description = agent.Description,
            IsEnabled = agent.IsEnabled,
            ServiceAccountBindings = bindings?.Any() == true ? bindings : null
        };
    }

    private ProviderExportInfo? MapProviderInfo(ServiceAccount? account)
    {
        if (account == null) return null;

        return new ProviderExportInfo
        {
            ServiceAccountName = account.Name,
            ProviderType = account.Provider,
            Config = ParseConfigJson(account.Configuration),
            Credentials = new CredentialReference
            {
                Storage = account.CredentialStorage.ToString().ToLowerInvariant(),
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
            ProviderType = account.Provider,
            Config = ParseConfigJson(account.Configuration),
            Credentials = new CredentialReference
            {
                Storage = account.CredentialStorage.ToString().ToLowerInvariant(),
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

        // Collect all transitions from all steps
        var allTransitions = workflow.Steps
            .SelectMany(s => s.OutgoingTransitions)
            .OrderBy(t => t.SortOrder)
            .Select(t => new WorkflowTransitionExportInfo
            {
                FromStepName = t.FromStep?.Name ?? workflow.Steps.FirstOrDefault(s => s.Id == t.FromStepId)?.Name ?? "unknown",
                ToStepName = t.ToStep?.Name ?? workflow.Steps.FirstOrDefault(s => s.Id == t.ToStepId)?.Name ?? "unknown",
                Condition = t.Condition,
                Label = t.Label,
                OutputIndex = t.OutputIndex,
                InputIndex = t.InputIndex
            })
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
            Transitions = allTransitions,
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

    private Dictionary<string, RulesetExportInfo> CollectAllRulesets(Agent agent)
    {
        var rulesets = new Dictionary<string, RulesetExportInfo>();

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

    private Dictionary<string, ExampleSetExportInfo> CollectExampleSets(Agent agent)
    {
        var exampleSets = new Dictionary<string, ExampleSetExportInfo>();

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
                        InputText = BuildInputText(e),
                        ExpectedOutputJson = BuildExpectedOutputJson(e),
                        Notes = e.Notes
                    })
                    .ToList()
            };
        }

        // TODO: If steps can reference their own example sets, collect those too

        return exampleSets;
    }

    private List<string> CollectRequiredCapabilities(Agent agent)
    {
        var capabilities = new HashSet<string>();

        if (agent.WorkflowDefinition == null) return capabilities.ToList();

        foreach (var step in agent.WorkflowDefinition.Steps)
        {
            if (step.StepType == StepType.Execute && !string.IsNullOrEmpty(step.ConfigurationJson))
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

    private async Task CollectSubWorkflowsRecursiveAsync(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowExportInfo> collected,
        HashSet<Guid> visited,
        Dictionary<string, RulesetExportInfo> rulesets,
        CancellationToken ct)
    {
        if (visited.Contains(workflow.Id)) return;
        visited.Add(workflow.Id);

        foreach (var step in workflow.Steps.Where(s => s.StepType == StepType.SubWorkflow))
        {
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

            // Load the referenced workflow with full includes
            var subWorkflow = await LoadWorkflowWithRelationsAsync(workflowId.Value, ct);
            if (subWorkflow == null)
            {
                _logger.LogWarning("Sub-workflow {WorkflowId} referenced by step '{StepName}' not found",
                    workflowId.Value, step.Name);
                continue;
            }

            // Skip if already collected (by name)
            if (collected.ContainsKey(subWorkflow.Name)) continue;

            var mapped = MapWorkflowInfo(subWorkflow);
            if (mapped != null)
            {
                collected[subWorkflow.Name] = mapped;

                // Include sub-workflow rulesets in the main rulesets dict
                CollectWorkflowRulesets(subWorkflow, rulesets);

                // Recursively collect nested sub-workflows
                await CollectSubWorkflowsRecursiveAsync(subWorkflow, collected, visited, rulesets, ct);
            }
        }
    }

    private async Task<WorkflowDefinition?> LoadWorkflowWithRelationsAsync(Guid workflowId, CancellationToken ct)
    {
        return await _db.WorkflowDefinitions
            .Include(w => w.Steps.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.OutgoingTransitions.OrderBy(t => t.SortOrder))
                    .ThenInclude(t => t.ToStep)
            .Include(w => w.Steps)
                .ThenInclude(s => s.RulesetMappings)
                    .ThenInclude(m => m.Ruleset)
                        .ThenInclude(r => r!.Rules.OrderBy(rule => rule.Priority))
            .Include(w => w.RulesetMappings)
                .ThenInclude(m => m.Ruleset)
                    .ThenInclude(r => r!.Rules.OrderBy(rule => rule.Priority))
            .Include(w => w.ExampleSet)
                .ThenInclude(e => e!.Examples.OrderBy(ex => ex.SortOrder))
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct);
    }

    private void CollectWorkflowRulesets(WorkflowDefinition workflow, Dictionary<string, RulesetExportInfo> rulesets)
    {
        // Collect workflow-level rulesets
        foreach (var mapping in workflow.RulesetMappings)
        {
            if (mapping.Ruleset != null && !rulesets.ContainsKey(mapping.Ruleset.Name))
            {
                rulesets[mapping.Ruleset.Name] = MapRulesetInfo(mapping.Ruleset);
            }
        }

        // Collect step-level rulesets
        foreach (var step in workflow.Steps)
        {
            foreach (var mapping in step.RulesetMappings)
            {
                if (mapping.Ruleset != null && !rulesets.ContainsKey(mapping.Ruleset.Name))
                {
                    rulesets[mapping.Ruleset.Name] = MapRulesetInfo(mapping.Ruleset);
                }
            }
        }
    }

    private Dictionary<string, object?> ParseConfigJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object?>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse config JSON: {Json}", json);
            return new Dictionary<string, object?>();
        }
    }

    private Dictionary<string, object?>? ParseConfigJsonAsObject(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse config JSON as object: {Json}", json);
            return null;
        }
    }

    private string BuildInputText(Example example)
    {
        // Combine ticket description fields into input text
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(example.TicketShortDescription))
        {
            parts.Add(example.TicketShortDescription);
        }

        if (!string.IsNullOrEmpty(example.TicketDescription))
        {
            parts.Add(example.TicketDescription);
        }

        return string.Join("\n", parts);
    }

    private string BuildExpectedOutputJson(Example example)
    {
        // Build expected output as JSON
        var output = new Dictionary<string, object?>
        {
            ["ticket_type"] = example.ExpectedTicketType.ToString(),
            ["confidence"] = example.ExpectedConfidence,
            ["affected_user"] = example.ExpectedAffectedUser,
            ["target_group"] = example.ExpectedTargetGroup,
            ["target_resource"] = example.ExpectedTargetResource,
            ["permission_level"] = example.ExpectedPermissionLevel,
            ["should_escalate"] = example.ExpectedShouldEscalate,
            ["escalation_reason"] = example.ExpectedEscalationReason
        };

        return JsonSerializer.Serialize(output);
    }
}
