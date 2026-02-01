namespace LucidToolServer.Exceptions;

/// <summary>
/// Exception thrown when a user is not found in Active Directory.
/// </summary>
public class UserNotFoundException : Exception
{
    public UserNotFoundException() : base() { }

    public UserNotFoundException(string message) : base(message) { }

    public UserNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
