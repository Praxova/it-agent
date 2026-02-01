using LucidAdmin.Core.Enums;
using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;

namespace LucidAdmin.Web.Models;

public class ExampleSetFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public TicketType TargetTicketType { get; set; } = TicketType.PasswordReset;
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ExampleFormModel> Examples { get; set; } = new();

    public CreateExampleSetRequest ToCreateRequest() => new(
        Name: Name,
        DisplayName: DisplayName,
        Description: Description,
        TargetTicketType: TargetTicketType,
        IsActive: IsActive
    );

    public UpdateExampleSetRequest ToUpdateRequest() => new(
        DisplayName: DisplayName,
        Description: Description,
        TargetTicketType: TargetTicketType,
        IsActive: IsActive
    );

    public static ExampleSetFormModel FromResponse(ExampleSetDetailResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        DisplayName = response.DisplayName,
        Description = response.Description,
        TargetTicketType = response.TargetTicketType,
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
    public TicketType ExpectedTicketType { get; set; } = TicketType.PasswordReset;
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

    public static ExampleFormModel FromResponse(ExampleResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        TicketShortDescription = response.TicketShortDescription,
        TicketDescription = response.TicketDescription,
        CallerName = response.CallerName,
        ExpectedTicketType = response.ExpectedTicketType,
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
