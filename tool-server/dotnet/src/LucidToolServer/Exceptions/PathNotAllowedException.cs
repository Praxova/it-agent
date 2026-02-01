namespace LucidToolServer.Exceptions;

/// <summary>
/// Exception thrown when a file path is not allowed by configuration.
/// </summary>
public class PathNotAllowedException : Exception
{
    public PathNotAllowedException() : base() { }

    public PathNotAllowedException(string message) : base(message) { }

    public PathNotAllowedException(string message, Exception innerException)
        : base(message, innerException) { }
}
