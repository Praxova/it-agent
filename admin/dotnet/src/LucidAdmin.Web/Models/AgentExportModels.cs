namespace LucidAdmin.Web.Models;

/// <summary>
/// Complete agent definition for export/runtime execution.
/// </summary>
public record AgentExportResponse
{
    public required string Version { get; init; }
    public required DateTime ExportedAt { get; init; }
    public required AgentExportInfo Agent { get; init; }
    public ProviderExportInfo? LlmProvider { get; init; }
    public ServiceNowExportInfo? ServiceNow { get; init; }
    public WorkflowExportInfo? Workflow { get; init; }
    public required Dictionary<string, RulesetExportInfo> Rulesets { get; init; }
    public required Dictionary<string, ExampleSetExportInfo> ExampleSets { get; init; }
    public required List<string> RequiredCapabilities { get; init; }
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
    /// <summary>
    /// Storage type: "none", "environment", "vault", "database"
    /// </summary>
    public required string Storage { get; init; }

    /// <summary>
    /// Reference key for the credential (e.g., env var name, vault path)
    /// </summary>
    public string? Reference { get; init; }
}

public record ProviderExportInfo
{
    public required string ServiceAccountName { get; init; }
    public required string ProviderType { get; init; }
    public required Dictionary<string, object?> Config { get; init; }
    public required CredentialReference Credentials { get; init; }
}

public record ServiceNowExportInfo
{
    public required string ServiceAccountName { get; init; }
    public required string ProviderType { get; init; }
    public required Dictionary<string, object?> Config { get; init; }
    public required CredentialReference Credentials { get; init; }
    public string? AssignmentGroup { get; init; }
}

public record WorkflowExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? TriggerType { get; init; }
    public Dictionary<string, object?>? TriggerConfig { get; init; }
    public required List<WorkflowStepExportInfo> Steps { get; init; }
    public required List<string> WorkflowRulesets { get; init; }
}

public record WorkflowStepExportInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string StepType { get; init; }
    public int SortOrder { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public Dictionary<string, object?>? Configuration { get; init; }
    public required List<string> Rulesets { get; init; }
    public required List<StepTransitionExportInfo> Transitions { get; init; }
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
    public required List<RuleExportInfo> Rules { get; init; }
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
    public required List<ExampleExportInfo> Examples { get; init; }
}

public record ExampleExportInfo
{
    public required Guid Id { get; init; }
    public required string InputText { get; init; }
    public required string ExpectedOutputJson { get; init; }
    public string? Notes { get; init; }
}
