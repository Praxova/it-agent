using System.Text.Json;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Infrastructure.Providers.Capabilities;

public class ServiceNowConnectorCapabilityProvider : BaseCapabilityProvider
{
    public override string CapabilityId => "snow-connector";
    public override string Version => "1.0.0";
    public override string Category => "connector";
    public override string DisplayName => "ServiceNow Connector";
    public override string Description => "Connect to ServiceNow to read and update tickets";
    public override bool RequiresServiceAccount => true;
    public override IEnumerable<string> RequiredProviders => new[] { "servicenow" };
    public override string? MinToolServerVersion => null; // Runs in Agent, not Tool Server

    public override ValidationResult ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return ValidationResult.Failure("Configuration is required for ServiceNow connector");

        try
        {
            var config = JsonSerializer.Deserialize<SnowConfig>(configurationJson);
            var errors = new List<string>();

            if (config?.PollingIntervalSeconds < 30)
                errors.Add("Polling interval must be at least 30 seconds");

            if (config?.AssignmentGroups == null || !config.AssignmentGroups.Any())
                errors.Add("At least one assignment group is required");

            return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    public override string GetConfigurationSchema() => """
        {
            "type": "object",
            "properties": {
                "pollingIntervalSeconds": { "type": "integer", "minimum": 30, "default": 60 },
                "assignmentGroups": { "type": "array", "items": { "type": "string" }, "minItems": 1 },
                "autoCloseOnSuccess": { "type": "boolean", "default": true },
                "addWorkNotes": { "type": "boolean", "default": true }
            },
            "required": ["assignmentGroups"]
        }
        """;

    public override string GetConfigurationExample() => """
        {
            "pollingIntervalSeconds": 60,
            "assignmentGroups": ["IT Helpdesk", "Password Resets"],
            "autoCloseOnSuccess": true,
            "addWorkNotes": true
        }
        """;

    private class SnowConfig
    {
        public int PollingIntervalSeconds { get; set; } = 60;
        public string[]? AssignmentGroups { get; set; }
    }
}
