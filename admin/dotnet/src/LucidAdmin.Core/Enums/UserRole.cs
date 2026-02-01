namespace LucidAdmin.Core.Enums;

/// <summary>
/// User roles for authorization
/// </summary>
public enum UserRole
{
    /// <summary>Full access to all features</summary>
    Admin = 0,

    /// <summary>Can view and perform operations, cannot change settings</summary>
    Operator = 1,

    /// <summary>Read-only access</summary>
    Viewer = 2,

    /// <summary>Programmatic access for agents (read credentials, update status)</summary>
    Agent = 3,

    /// <summary>Programmatic access for tool servers (read credentials)</summary>
    ToolServer = 4
}
