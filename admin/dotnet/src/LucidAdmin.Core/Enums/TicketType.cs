namespace LucidAdmin.Core.Enums;

/// <summary>
/// Types of tickets the agent can classify and handle.
/// </summary>
public enum TicketType
{
    /// <summary>User needs password reset or account unlock.</summary>
    PasswordReset,

    /// <summary>Add user to an AD group.</summary>
    GroupAccessAdd,

    /// <summary>Remove user from an AD group.</summary>
    GroupAccessRemove,

    /// <summary>Grant file/folder permissions.</summary>
    FilePermissionGrant,

    /// <summary>Revoke file/folder permissions.</summary>
    FilePermissionRevoke,

    /// <summary>Ticket type could not be determined.</summary>
    Unknown,

    /// <summary>Multiple request types in one ticket.</summary>
    MultipleRequests,

    /// <summary>Request is outside agent's capabilities.</summary>
    OutOfScope,

    /// <summary>Request to install software on a workstation.</summary>
    SoftwareInstall
}
