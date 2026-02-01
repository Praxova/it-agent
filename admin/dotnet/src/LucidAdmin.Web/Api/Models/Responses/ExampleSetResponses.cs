using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Models.Responses;

public record ExampleSetResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    TicketType TargetTicketType,
    bool IsBuiltIn,
    bool IsActive,
    int ExampleCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ExampleSetDetailResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    TicketType TargetTicketType,
    bool IsBuiltIn,
    bool IsActive,
    List<ExampleResponse> Examples,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ExampleResponse(
    Guid Id,
    string Name,
    // Input
    string TicketShortDescription,
    string? TicketDescription,
    string? CallerName,
    // Output
    TicketType ExpectedTicketType,
    decimal ExpectedConfidence,
    string? ExpectedAffectedUser,
    string? ExpectedTargetGroup,
    string? ExpectedTargetResource,
    string? ExpectedPermissionLevel,
    bool ExpectedShouldEscalate,
    string? ExpectedEscalationReason,
    // Metadata
    string? Notes,
    int SortOrder,
    bool IsActive
);

/// <summary>
/// Response format used when exporting examples for LLM training prompts.
/// </summary>
public record ExampleTrainingFormat(
    string Input,
    string ExpectedOutput
);
