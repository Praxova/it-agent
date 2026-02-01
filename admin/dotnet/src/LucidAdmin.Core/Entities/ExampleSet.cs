using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A collection of few-shot examples for training the LLM classifier.
/// </summary>
public class ExampleSet : BaseEntity
{
    /// <summary>
    /// Unique name for the example set (e.g., "password-reset-examples").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of what ticket types this example set covers.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Primary ticket type this example set is designed for.
    /// </summary>
    public required TicketType TargetTicketType { get; set; }

    /// <summary>
    /// Whether this example set ships with the product.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Whether this example set is active and available for use.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Examples in this set.
    /// </summary>
    public ICollection<Example> Examples { get; set; } = new List<Example>();
}
