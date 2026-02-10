namespace LucidAdmin.Core.Entities;

/// <summary>
/// Admin-manageable ticket classification category.
/// Replaces the former hard-coded TicketType enum.
/// The Name field (kebab-case slug) is used directly in workflow
/// transition conditions and classification output.
/// </summary>
public class TicketCategory : BaseEntity
{
    /// <summary>Kebab-case slug used in classification and routing (e.g., "password-reset", "gis-map-request").</summary>
    public required string Name { get; set; }

    /// <summary>Human-readable display name (e.g., "Password Reset", "GIS Map Request").</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional description of what this category covers.</summary>
    public string? Description { get; set; }

    /// <summary>Hex color for UI chips (e.g., "#4CAF50").</summary>
    public string? Color { get; set; }

    /// <summary>Built-in categories are seeded and cannot be deleted.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Inactive categories are excluded from classification.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Display order in UI.</summary>
    public int SortOrder { get; set; }

    // Navigation properties
    public ICollection<Example> Examples { get; set; } = new List<Example>();
    public ICollection<ExampleSet> ExampleSets { get; set; } = new List<ExampleSet>();
}
