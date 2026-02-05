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
    public Guid? ReferencedWorkflowId { get; set; }
    public string? ReferencedWorkflowName { get; set; }

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

            case StepType.SubWorkflow:
                OutputPorts["completed"] = AddPort(PortAlignment.Right);
                OutputPorts["escalated"] = AddPort(PortAlignment.Right);
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
