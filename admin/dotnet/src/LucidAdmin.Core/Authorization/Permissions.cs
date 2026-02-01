using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Authorization;

/// <summary>
/// Authorization permissions for the Admin Portal
/// </summary>
public static class Permissions
{
    // Flat permission strings (for authentication handler)
    public const string CredentialsRead = "credentials:read";
    public const string CredentialsWrite = "credentials:write";
    public const string ServiceAccountsRead = "service_accounts:read";
    public const string ServiceAccountsWrite = "service_accounts:write";
    public const string ServiceAccountsTest = "service_accounts:test";
    public const string AgentsRead = "agents:read";
    public const string AgentsWrite = "agents:write";
    public const string AgentsSelfConfig = "agents:self_config";
    public const string ToolServersRead = "tool_servers:read";
    public const string ToolServersWrite = "tool_servers:write";
    public const string CapabilitiesRead = "capabilities:read";
    public const string CapabilitiesWrite = "capabilities:write";
    public const string CapabilitiesRoute = "capabilities:route";
    public const string AuditRead = "audit:read";
    public const string AuditWrite = "audit:write";
    public const string ApiKeysRead = "api_keys:read";
    public const string ApiKeysWrite = "api_keys:write";
    public const string SystemSettings = "system:settings";

    // Nested permission classes (for structured access)
    public static class ServiceAccounts
    {
        public const string View = "service_accounts.view";
        public const string Create = "service_accounts.create";
        public const string Update = "service_accounts.update";
        public const string Delete = "service_accounts.delete";
        public const string ViewCredentials = "service_accounts.view_credentials";
        public const string UpdateCredentials = "service_accounts.update_credentials";
        public const string DeleteCredentials = "service_accounts.delete_credentials";
    }

    public static class Agents
    {
        public const string View = "agents.view";
        public const string Create = "agents.create";
        public const string Update = "agents.update";
        public const string Delete = "agents.delete";
        public const string UpdateStatus = "agents.update_status";
    }

    public static class CapabilityMappings
    {
        public const string View = "capability_mappings.view";
        public const string Create = "capability_mappings.create";
        public const string Update = "capability_mappings.update";
        public const string Delete = "capability_mappings.delete";
    }

    public static class ApiKeys
    {
        public const string View = "api_keys.view";
        public const string Create = "api_keys.create";
        public const string Revoke = "api_keys.revoke";
        public const string Delete = "api_keys.delete";
    }

    public static class System
    {
        public const string ViewSettings = "system.view_settings";
        public const string UpdateSettings = "system.update_settings";
        public const string ViewLogs = "system.view_logs";
    }

    /// <summary>
    /// Get all permissions for a given role
    /// </summary>
    public static HashSet<string> GetPermissionsForRole(UserRole role)
    {
        return role switch
        {
            UserRole.Admin => new HashSet<string>
            {
                // Admin has all permissions
                ServiceAccounts.View,
                ServiceAccounts.Create,
                ServiceAccounts.Update,
                ServiceAccounts.Delete,
                ServiceAccounts.ViewCredentials,
                ServiceAccounts.UpdateCredentials,
                ServiceAccounts.DeleteCredentials,
                Agents.View,
                Agents.Create,
                Agents.Update,
                Agents.Delete,
                Agents.UpdateStatus,
                CapabilityMappings.View,
                CapabilityMappings.Create,
                CapabilityMappings.Update,
                CapabilityMappings.Delete,
                ApiKeys.View,
                ApiKeys.Create,
                ApiKeys.Revoke,
                ApiKeys.Delete,
                System.ViewSettings,
                System.UpdateSettings,
                System.ViewLogs
            },

            UserRole.Operator => new HashSet<string>
            {
                // Operator can view and perform operations, but not change settings
                ServiceAccounts.View,
                ServiceAccounts.ViewCredentials,
                Agents.View,
                Agents.Create,
                Agents.Update,
                Agents.UpdateStatus,
                CapabilityMappings.View,
                System.ViewLogs
            },

            UserRole.Viewer => new HashSet<string>
            {
                // Viewer has read-only access (no credentials)
                ServiceAccounts.View,
                Agents.View,
                CapabilityMappings.View,
                System.ViewLogs
            },

            UserRole.Agent => new HashSet<string>
            {
                // Agents can read credentials and update their own status
                ServiceAccounts.View,
                ServiceAccounts.ViewCredentials,
                Agents.View,
                Agents.UpdateStatus,
                CapabilityMappings.View
            },

            UserRole.ToolServer => new HashSet<string>
            {
                // Tool servers can only read service accounts and credentials
                ServiceAccounts.View,
                ServiceAccounts.ViewCredentials,
                CapabilityMappings.View
            },

            _ => new HashSet<string>()
        };
    }

    /// <summary>
    /// Check if a role has a specific permission
    /// </summary>
    public static bool HasPermission(UserRole role, string permission)
    {
        var permissions = GetPermissionsForRole(role);
        return permissions.Contains(permission);
    }
}
