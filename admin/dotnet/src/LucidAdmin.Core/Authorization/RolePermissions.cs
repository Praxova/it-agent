using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Authorization;

/// <summary>
/// Maps roles to their flat permission strings
/// </summary>
public static class RolePermissions
{
    public static readonly HashSet<string> AdminPermissions = new()
    {
        Permissions.CredentialsRead,
        Permissions.CredentialsWrite,
        Permissions.ServiceAccountsRead,
        Permissions.ServiceAccountsWrite,
        Permissions.ServiceAccountsTest,
        Permissions.AgentsRead,
        Permissions.AgentsWrite,
        Permissions.AgentsSelfConfig,
        Permissions.ToolServersRead,
        Permissions.ToolServersWrite,
        Permissions.CapabilitiesRead,
        Permissions.CapabilitiesWrite,
        Permissions.CapabilitiesRoute,
        Permissions.AuditRead,
        Permissions.AuditWrite,
        Permissions.ApiKeysRead,
        Permissions.ApiKeysWrite,
        Permissions.SystemSettings
    };

    public static readonly HashSet<string> OperatorPermissions = new()
    {
        Permissions.CredentialsRead,
        Permissions.ServiceAccountsRead,
        Permissions.ServiceAccountsTest,
        Permissions.AgentsRead,
        Permissions.AgentsWrite,
        Permissions.AgentsSelfConfig,
        Permissions.ToolServersRead,
        Permissions.CapabilitiesRead,
        Permissions.CapabilitiesRoute,
        Permissions.AuditRead,
        Permissions.ApiKeysRead
    };

    public static readonly HashSet<string> ViewerPermissions = new()
    {
        Permissions.ServiceAccountsRead,
        Permissions.AgentsRead,
        Permissions.ToolServersRead,
        Permissions.CapabilitiesRead,
        Permissions.AuditRead,
        Permissions.ApiKeysRead
    };

    public static readonly HashSet<string> AgentPermissions = new()
    {
        Permissions.CredentialsRead,
        Permissions.ServiceAccountsRead,
        Permissions.AgentsRead,
        Permissions.AgentsSelfConfig,
        Permissions.CapabilitiesRead,
        Permissions.CapabilitiesRoute
    };

    public static readonly HashSet<string> ToolServerPermissions = new()
    {
        Permissions.CredentialsRead,
        Permissions.ServiceAccountsRead,
        Permissions.CapabilitiesRead
    };

    public static HashSet<string> GetPermissions(UserRole role)
    {
        return role switch
        {
            UserRole.Admin => AdminPermissions,
            UserRole.Operator => OperatorPermissions,
            UserRole.Viewer => ViewerPermissions,
            UserRole.Agent => AgentPermissions,
            UserRole.ToolServer => ToolServerPermissions,
            _ => new HashSet<string>()
        };
    }
}
