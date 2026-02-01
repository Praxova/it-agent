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
    DateTime UpdatedAt
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
    DateTime UpdatedAt
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
