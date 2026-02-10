namespace LucidAdmin.Web.Api.Models.Requests;

public record CreateExampleSetRequest(
    string Name,
    string? DisplayName,
    string? Description,
    Guid? TicketCategoryId,
    bool IsActive = true
);

public record UpdateExampleSetRequest(
    string? DisplayName,
    string? Description,
    Guid? TicketCategoryId,
    bool? IsActive
);

public record CreateExampleRequest(
    string Name,
    string TicketShortDescription,
    string? TicketDescription,
    string? CallerName,
    Guid? TicketCategoryId,
    decimal ExpectedConfidence = 0.95m,
    string? ExpectedAffectedUser = null,
    string? ExpectedTargetGroup = null,
    string? ExpectedTargetResource = null,
    string? ExpectedPermissionLevel = null,
    bool ExpectedShouldEscalate = false,
    string? ExpectedEscalationReason = null,
    string? Notes = null,
    bool IsActive = true
);

public record UpdateExampleRequest(
    string? Name,
    string? TicketShortDescription,
    string? TicketDescription,
    string? CallerName,
    Guid? TicketCategoryId,
    decimal? ExpectedConfidence,
    string? ExpectedAffectedUser,
    string? ExpectedTargetGroup,
    string? ExpectedTargetResource,
    string? ExpectedPermissionLevel,
    bool? ExpectedShouldEscalate,
    string? ExpectedEscalationReason,
    string? Notes,
    bool? IsActive
);

public record ReorderExamplesRequest(
    List<Guid> ExampleIds
);
