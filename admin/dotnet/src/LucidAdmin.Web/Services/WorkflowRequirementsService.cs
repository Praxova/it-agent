using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using System.Text.Json;

namespace LucidAdmin.Web.Services;

/// <summary>
/// Analyzes workflow steps to compute what service accounts an agent needs.
/// </summary>
public class WorkflowRequirementsService
{
    /// <summary>
    /// Compute requirements from a workflow's steps.
    /// </summary>
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
                    requirements.HasSubWorkflows = true;
                    break;
            }
        }

        return requirements;
    }

    private static Dictionary<string, string?> ParseConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<string, string?>();

        try
        {
            var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, string?>();
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
            return new Dictionary<string, string?>();
        }
    }

    private static string? GetConfigValue(Dictionary<string, string?> config, string key)
    {
        return config.TryGetValue(key, out var value) ? value : null;
    }
}

/// <summary>
/// What an agent needs to run a specific workflow.
/// </summary>
public class WorkflowRequirements
{
    public string? TriggerType { get; set; }
    public bool NeedsTriggerAccount { get; set; }
    public bool NeedsAssignmentGroup { get; set; }
    public bool NeedsLlm { get; set; }
    public List<string> RequiredCapabilities { get; set; } = new();
    public List<string> NotificationChannels { get; set; } = new();
    public bool HasSubWorkflows { get; set; }
}
