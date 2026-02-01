namespace LucidAdmin.Core.Exceptions;

public class ValidationException : LucidException
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : base($"Validation error: {error}")
    {
        Errors = new Dictionary<string, string[]> { { field, new[] { error } } };
    }
}
