using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Models.Requests;

public record CreateWorkflowRequest(
    string Name,
    string? DisplayName,
    string? Description,
    Guid? ExampleSetId,
    bool IsActive = true
);

public record UpdateWorkflowRequest(
    string? DisplayName,
    string? Description,
    string? Version,
    Guid? ExampleSetId,
    bool? IsActive
);

public record SaveWorkflowLayoutRequest(
    string? LayoutJson,
    List<WorkflowStepDto> Steps,
    List<StepTransitionDto> Transitions
);

public record WorkflowStepDto(
    Guid? Id,
    string Name,
    string? DisplayName,
    StepType StepType,
    string? ConfigurationJson,
    int PositionX,
    int PositionY,
    int? DrawflowNodeId,
    int SortOrder
);

public record StepTransitionDto(
    Guid? Id,
    string FromStepName,
    string ToStepName,
    string? Label,
    string? Condition,
    int OutputIndex,
    int InputIndex
);

public record AddWorkflowRulesetRequest(
    Guid RulesetId,
    int Priority = 0,
    bool IsEnabled = true
);

public record AddStepRulesetRequest(
    Guid RulesetId,
    int Priority = 0,
    bool IsEnabled = true
);
