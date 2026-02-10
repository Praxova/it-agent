namespace LucidAdmin.Web.Api.Models.Responses;

public record TicketCategoryResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public bool IsBuiltIn { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public int ExampleCount { get; init; }
}
