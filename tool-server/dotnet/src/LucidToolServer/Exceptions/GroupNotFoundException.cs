namespace LucidToolServer.Exceptions;

/// <summary>
/// Exception thrown when a group is not found in Active Directory.
/// </summary>
public class GroupNotFoundException : Exception
{
    public GroupNotFoundException() : base() { }

    public GroupNotFoundException(string message) : base(message) { }

    public GroupNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
