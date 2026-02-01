namespace LucidToolServer.Exceptions;

/// <summary>
/// Exception thrown when an operation is not allowed due to permission restrictions.
/// </summary>
public class PermissionDeniedException : Exception
{
    public PermissionDeniedException() : base() { }

    public PermissionDeniedException(string message) : base(message) { }

    public PermissionDeniedException(string message, Exception innerException)
        : base(message, innerException) { }
}
