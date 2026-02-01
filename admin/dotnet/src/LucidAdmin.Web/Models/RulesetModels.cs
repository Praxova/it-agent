using System.Text.Json.Serialization;

namespace LucidAdmin.Web.Models;

// Ruleset Request/Response Models
public record CreateRulesetRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("is_active")] bool IsActive = true
);

public record UpdateRulesetRequest(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("is_active")] bool? IsActive
);

public record RulesetResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("is_built_in")] bool IsBuiltIn,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("rules")] List<RuleResponse>? Rules,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

// Rule Request/Response Models
public record CreateRuleRequest(
    [property: JsonPropertyName("ruleset_id")] Guid RulesetId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rule_text")] string RuleText,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] int Priority = 100,
    [property: JsonPropertyName("is_active")] bool IsActive = true
);

public record UpdateRuleRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("rule_text")] string? RuleText,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] int? Priority,
    [property: JsonPropertyName("is_active")] bool? IsActive
);

public record RuleResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ruleset_id")] Guid RulesetId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rule_text")] string RuleText,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

// Blazor Form Models
public class RulesetFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string Category { get; set; } = "Custom";
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; } = true;
    public List<RuleFormModel> Rules { get; set; } = new();

    public CreateRulesetRequest ToCreateRequest() => new(
        Name: Name,
        DisplayName: DisplayName,
        Description: Description,
        Category: Category,
        IsActive: IsActive
    );

    public UpdateRulesetRequest ToUpdateRequest() => new(
        DisplayName: DisplayName,
        Description: Description,
        Category: Category,
        IsActive: IsActive
    );

    public static RulesetFormModel FromResponse(RulesetResponse response) => new()
    {
        Id = response.Id,
        Name = response.Name,
        DisplayName = response.DisplayName,
        Description = response.Description,
        Category = response.Category,
        IsBuiltIn = response.IsBuiltIn,
        IsActive = response.IsActive,
        Rules = response.Rules?.Select(RuleFormModel.FromResponse).ToList() ?? new()
    };
}

public class RuleFormModel
{
    public Guid? Id { get; set; }
    public Guid RulesetId { get; set; }
    public string Name { get; set; } = "";
    public string RuleText { get; set; } = "";
    public string? Description { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;

    public CreateRuleRequest ToCreateRequest() => new(
        RulesetId: RulesetId,
        Name: Name,
        RuleText: RuleText,
        Description: Description,
        Priority: Priority,
        IsActive: IsActive
    );

    public UpdateRuleRequest ToUpdateRequest() => new(
        Name: Name,
        RuleText: RuleText,
        Description: Description,
        Priority: Priority,
        IsActive: IsActive
    );

    public static RuleFormModel FromResponse(RuleResponse response) => new()
    {
        Id = response.Id,
        RulesetId = response.RulesetId,
        Name = response.Name,
        RuleText = response.RuleText,
        Description = response.Description,
        Priority = response.Priority,
        IsActive = response.IsActive
    };
}
