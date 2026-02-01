using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// Generates Drawflow-compatible JSON layout from WorkflowSteps and StepTransitions.
/// </summary>
public class DrawflowLayoutGenerator
{
    /// <summary>
    /// Generates Drawflow-compatible JSON from WorkflowSteps and StepTransitions.
    /// </summary>
    public string GenerateLayout(
        IEnumerable<WorkflowStep> steps,
        IEnumerable<StepTransition> transitions)
    {
        var nodeData = new Dictionary<string, object>();
        var stepToNodeId = new Dictionary<Guid, int>();
        int nodeId = 1;

        // First pass: create nodes
        foreach (var step in steps.OrderBy(s => s.SortOrder))
        {
            stepToNodeId[step.Id] = nodeId;

            // Determine number of outputs based on outgoing transitions
            var outgoingCount = transitions.Count(t => t.FromStepId == step.Id);
            var outputs = new Dictionary<string, object>();
            for (int i = 0; i < Math.Max(1, outgoingCount); i++)
            {
                outputs[$"output_{i + 1}"] = new { connections = new List<object>() };
            }

            // Inputs (all nodes except trigger have 1 input)
            var inputs = step.StepType == StepType.Trigger
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>
                  {
                      ["input_1"] = new { connections = new List<object>() }
                  };

            nodeData[nodeId.ToString()] = new Dictionary<string, object>
            {
                ["id"] = nodeId,
                ["name"] = step.StepType.ToString().ToLower(),
                ["data"] = new Dictionary<string, object>
                {
                    ["stepId"] = step.Id.ToString(),
                    ["stepName"] = step.Name,
                    ["stepType"] = step.StepType.ToString(),
                    ["displayName"] = step.DisplayName ?? step.Name,
                    ["configuration"] = step.ConfigurationJson ?? "{}"
                },
                ["class"] = step.StepType.ToString().ToLower(),
                ["html"] = GenerateNodeHtml(step),
                ["typenode"] = false,
                ["inputs"] = inputs,
                ["outputs"] = outputs,
                ["pos_x"] = step.PositionX,
                ["pos_y"] = step.PositionY
            };

            nodeId++;
        }

        // Second pass: add connections
        foreach (var transition in transitions)
        {
            if (!stepToNodeId.TryGetValue(transition.FromStepId, out var fromNodeId) ||
                !stepToNodeId.TryGetValue(transition.ToStepId, out var toNodeId))
                continue;

            var fromNodeKey = fromNodeId.ToString();
            var toNodeKey = toNodeId.ToString();

            // Add to output connections of source node
            if (nodeData[fromNodeKey] is Dictionary<string, object> fromNode &&
                fromNode["outputs"] is Dictionary<string, object> outputs)
            {
                var outputKey = $"output_{transition.OutputIndex + 1}";
                if (!outputs.ContainsKey(outputKey))
                    outputKey = "output_1";

                if (outputs[outputKey] is Dictionary<string, object> output &&
                    output["connections"] is List<object> outConnections)
                {
                    outConnections.Add(new Dictionary<string, object>
                    {
                        ["node"] = toNodeKey,
                        ["output"] = $"input_{transition.InputIndex + 1}"
                    });
                }
            }

            // Add to input connections of target node
            if (nodeData[toNodeKey] is Dictionary<string, object> toNode &&
                toNode["inputs"] is Dictionary<string, object> inputs)
            {
                var inputKey = $"input_{transition.InputIndex + 1}";
                if (!inputs.ContainsKey(inputKey))
                    inputKey = "input_1";

                if (inputs.ContainsKey(inputKey) &&
                    inputs[inputKey] is Dictionary<string, object> input &&
                    input["connections"] is List<object> inConnections)
                {
                    inConnections.Add(new Dictionary<string, object>
                    {
                        ["node"] = fromNodeKey,
                        ["input"] = $"output_{transition.OutputIndex + 1}"
                    });
                }
            }
        }

        var layout = new Dictionary<string, object>
        {
            ["drawflow"] = new Dictionary<string, object>
            {
                ["Home"] = new Dictionary<string, object>
                {
                    ["data"] = nodeData
                }
            }
        };

        return JsonSerializer.Serialize(layout, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    private string GenerateNodeHtml(WorkflowStep step)
    {
        var icon = step.StepType switch
        {
            StepType.Trigger => "🎯",
            StepType.Classify => "🏷️",
            StepType.Query => "🔍",
            StepType.Validate => "✓",
            StepType.Execute => "⚡",
            StepType.UpdateTicket => "📝",
            StepType.Notify => "📢",
            StepType.Escalate => "🚨",
            StepType.Condition => "❓",
            StepType.End => "🏁",
            _ => "📦"
        };

        var displayName = step.DisplayName ?? step.Name;
        return $"{icon} {displayName}";
    }
}
