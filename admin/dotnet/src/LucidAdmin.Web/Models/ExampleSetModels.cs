using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;

namespace LucidAdmin.Web.Models;

public class ExampleSetFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public Guid? TicketCategoryId { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ExampleFormModel> Examples { get; set; } = new();

    public CreateExampleSetRequest ToCreateRequest() => new(
        Name: Name,
        DisplayName: DisplayName,
        Description: Description,
        TicketCategoryId: TicketCategoryId,
        IsActive: IsActive
    );

    public UpdateExampleSetRequest ToUpdateRequest() => new(
        DisplayName: DisplayName,
        Description: Description,
        TicketCategoryId: TicketCategoryId,
        IsActive: IsActive
    );

    public static ExampleSetFormModel FromResponse(ExampleSetDetailResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        DisplayName = response.DisplayName,
        Description = response.Description,
        TicketCategoryId = response.TicketCategoryId,
        IsBuiltIn = response.IsBuiltIn,
        IsActive = response.IsActive,
        Examples = response.Examples?.Select(ExampleFormModel.FromResponse).ToList() ?? new()
    };
}

public class ExampleFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string TicketShortDescription { get; set; } = "";
    public string? TicketDescription { get; set; }
    public string? CallerName { get; set; }
    public Guid? TicketCategoryId { get; set; }
    public decimal ExpectedConfidence { get; set; } = 0.95m;
    public string? ExpectedAffectedUser { get; set; }
    public string? ExpectedTargetGroup { get; set; }
    public string? ExpectedTargetResource { get; set; }
    public string? ExpectedPermissionLevel { get; set; }
    public bool ExpectedShouldEscalate { get; set; }
    public string? ExpectedEscalationReason { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public CreateExampleRequest ToCreateRequest() => new(
        Name: Name,
        TicketShortDescription: TicketShortDescription,
        TicketDescription: TicketDescription,
        CallerName: CallerName,
        TicketCategoryId: TicketCategoryId,
        ExpectedConfidence: ExpectedConfidence,
        ExpectedAffectedUser: ExpectedAffectedUser,
        ExpectedTargetGroup: ExpectedTargetGroup,
        ExpectedTargetResource: ExpectedTargetResource,
        ExpectedPermissionLevel: ExpectedPermissionLevel,
        ExpectedShouldEscalate: ExpectedShouldEscalate,
        ExpectedEscalationReason: ExpectedEscalationReason,
        Notes: Notes,
        IsActive: IsActive
    );

    public UpdateExampleRequest ToUpdateRequest() => new(
        Name: Name,
        TicketShortDescription: TicketShortDescription,
        TicketDescription: TicketDescription,
        CallerName: CallerName,
        TicketCategoryId: TicketCategoryId,
        ExpectedConfidence: ExpectedConfidence,
        ExpectedAffectedUser: ExpectedAffectedUser,
        ExpectedTargetGroup: ExpectedTargetGroup,
        ExpectedTargetResource: ExpectedTargetResource,
        ExpectedPermissionLevel: ExpectedPermissionLevel,
        ExpectedShouldEscalate: ExpectedShouldEscalate,
        ExpectedEscalationReason: ExpectedEscalationReason,
        Notes: Notes,
        IsActive: IsActive
    );

    public static ExampleFormModel FromResponse(ExampleResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        TicketShortDescription = response.TicketShortDescription,
        TicketDescription = response.TicketDescription,
        CallerName = response.CallerName,
        TicketCategoryId = response.TicketCategoryId,
        ExpectedConfidence = response.ExpectedConfidence,
        ExpectedAffectedUser = response.ExpectedAffectedUser,
        ExpectedTargetGroup = response.ExpectedTargetGroup,
        ExpectedTargetResource = response.ExpectedTargetResource,
        ExpectedPermissionLevel = response.ExpectedPermissionLevel,
        ExpectedShouldEscalate = response.ExpectedShouldEscalate,
        ExpectedEscalationReason = response.ExpectedEscalationReason,
        Notes = response.Notes,
        SortOrder = response.SortOrder,
        IsActive = response.IsActive
    };
}
