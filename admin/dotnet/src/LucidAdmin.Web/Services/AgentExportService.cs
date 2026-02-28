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
                        .ThenInclude(ex => ex.TicketCategory)
            .AsSplitQuery()  // Optimize for multiple includes
            .FirstOrDefaultAsync(a => a.Id == agentId, ct);
    }

    private async Task<AgentExportResponse> BuildExportResponseAsync(Agent agent, CancellationToken ct)
    {
        var rulesets = CollectAllRulesets(agent);
        var requiredCapabilities = CollectRequiredCapabilities(agent);

        // Collect sub-workflow definitions (must happen before example set collection)
        var subWorkflows = new Dictionary<string, WorkflowExportInfo>();
        if (agent.WorkflowDefinition != null)
        {
            var visited = new HashSet<Guid>();
            await CollectSubWorkflowsRecursiveAsync(
                agent.WorkflowDefinition, subWorkflows, visited, rulesets, ct);
        }

        // Collect ALL example sets (main workflow + sub-workflows + step-referenced)
        var exampleSets = await CollectAllExampleSetsAsync(agent, subWorkflows, ct);

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
            ServiceAccountId = account.Id,
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
            ServiceAccountId = account.Id,
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
            ExampleSetName = workflow.ExampleSet?.Name,
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
                    Priority = r.Priority,
                    IsEnabled = r.IsActive
                })
                .ToList()
        };
    }

    private async Task<Dictionary<string, ExampleSetExportInfo>> CollectAllExampleSetsAsync(
        Agent agent,
        Dictionary<string, WorkflowExportInfo> subWorkflows,
        CancellationToken ct)
    {
        var exampleSets = new Dictionary<string, ExampleSetExportInfo>();
        var referencedSetNames = new HashSet<string>();

        // 1. Main workflow's linked example set
        var mainExampleSet = agent.WorkflowDefinition?.ExampleSet;
        if (mainExampleSet != null)
        {
            exampleSets[mainExampleSet.Name] = MapExampleSetInfo(mainExampleSet);
        }

        // 2. Scan main workflow steps for use_example_set references
        if (agent.WorkflowDefinition != null)
        {
            CollectStepExampleSetReferences(agent.WorkflowDefinition.Steps, referencedSetNames);
        }

        // 3. Collect sub-workflow example sets and scan their steps for references
        foreach (var subWfName in subWorkflows.Keys)
        {
            var subWfDef = await _db.WorkflowDefinitions
                .Include(w => w.ExampleSet)
                    .ThenInclude(e => e!.Examples.OrderBy(ex => ex.SortOrder))
                        .ThenInclude(ex => ex.TicketCategory)
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Name == subWfName, ct);

            if (subWfDef?.ExampleSet != null && !exampleSets.ContainsKey(subWfDef.ExampleSet.Name))
            {
                exampleSets[subWfDef.ExampleSet.Name] = MapExampleSetInfo(subWfDef.ExampleSet);
            }

            if (subWfDef != null)
            {
                CollectStepExampleSetReferences(subWfDef.Steps, referencedSetNames);
            }
        }

        // 4. Load any step-referenced example sets not yet collected
        var missingSetNames = referencedSetNames
            .Where(name => !exampleSets.ContainsKey(name))
            .ToList();

        if (missingSetNames.Any())
        {
            _logger.LogInformation("Loading {Count} step-referenced example sets: {Names}",
                missingSetNames.Count, string.Join(", ", missingSetNames));

            var additionalSets = await _db.ExampleSets
                .Include(es => es.Examples.OrderBy(e => e.SortOrder))
                    .ThenInclude(e => e.TicketCategory)
                .Where(es => missingSetNames.Contains(es.Name))
                .ToListAsync(ct);

            foreach (var set in additionalSets)
            {
                exampleSets[set.Name] = MapExampleSetInfo(set);
            }

            var foundNames = additionalSets.Select(s => s.Name).ToHashSet();
            var notFound = missingSetNames.Where(n => !foundNames.Contains(n)).ToList();
            if (notFound.Any())
            {
                _logger.LogWarning("Example sets referenced in step configs but not found in DB: {Names}",
                    string.Join(", ", notFound));
            }
        }

        return exampleSets;
    }

    private void CollectStepExampleSetReferences(
        IEnumerable<WorkflowStep> steps,
        HashSet<string> referencedNames)
    {
        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.ConfigurationJson)) continue;

            var config = ParseConfigJsonAsObject(step.ConfigurationJson);
            if (config == null) continue;

            if (config.TryGetValue("use_example_set", out var setNameObj) && setNameObj != null)
            {
                var setName = setNameObj.ToString();
                if (!string.IsNullOrEmpty(setName))
                {
                    referencedNames.Add(setName);
                }
            }
        }
    }

    private ExampleSetExportInfo MapExampleSetInfo(ExampleSet exampleSet)
    {
        return new ExampleSetExportInfo
        {
            Id = exampleSet.Id,
            Name = exampleSet.Name,
            DisplayName = exampleSet.DisplayName,
            Examples = exampleSet.Examples
                .OrderBy(e => e.SortOrder)
                .Where(e => e.TicketCategory?.IsActive != false)
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
        // Guard: inactive categories should have been filtered upstream,
        // but fall back to "unknown" if one slips through.
        var ticketType = example.TicketCategory is { IsActive: true }
            ? example.TicketCategory.Name
            : "unknown";

        // Build expected output as JSON with ticket category name
        // matching dispatcher transition conditions
        var output = new Dictionary<string, object?>
        {
            ["ticket_type"] = ticketType,
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
