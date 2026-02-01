namespace LucidAdmin.Core.Models;

/// <summary>
/// Result of a validation operation
/// </summary>
public record ValidationResult(
    bool IsValid,
    IEnumerable<string> Errors
)
{
    public static ValidationResult Success() => new(true, Array.Empty<string>());
    public static ValidationResult Failure(params string[] errors) => new(false, errors);
    public static ValidationResult Failure(IEnumerable<string> errors) => new(false, errors);
}
