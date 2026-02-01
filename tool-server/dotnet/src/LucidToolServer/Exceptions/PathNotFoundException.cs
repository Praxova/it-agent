namespace LucidToolServer.Exceptions;

/// <summary>
/// Exception thrown when a file path does not exist.
/// </summary>
public class PathNotFoundException : Exception
{
    public PathNotFoundException() : base() { }

    public PathNotFoundException(string message) : base(message) { }

    public PathNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
