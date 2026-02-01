namespace LucidToolServer.Exceptions;

/// <summary>
/// Exception thrown when an Active Directory operation fails.
/// </summary>
public class AdOperationException : Exception
{
    public AdOperationException() : base() { }

    public AdOperationException(string message) : base(message) { }

    public AdOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}
