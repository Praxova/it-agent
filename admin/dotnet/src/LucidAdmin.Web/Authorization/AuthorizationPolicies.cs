using LucidAdmin.Core.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace LucidAdmin.Web.Authorization;

/// <summary>
/// Defines authorization policies for the Admin Portal
/// </summary>
public static class AuthorizationPolicies
{
    // Policy names
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireOperator = "RequireOperator";
    public const string RequireViewer = "RequireViewer";
    public const string RequireAgent = "RequireAgent";
    public const string RequireToolServer = "RequireToolServer";

    // Resource-specific policies
    public const string CanReadCredentials = "CanReadCredentials";
    public const string CanWriteCredentials = "CanWriteCredentials";
    public const string CanReadServiceAccounts = "CanReadServiceAccounts";
    public const string CanWriteServiceAccounts = "CanWriteServiceAccounts";
    public const string CanReadAgents = "CanReadAgents";
    public const string CanWriteAgents = "CanWriteAgents";
    public const string CanReadToolServers = "CanReadToolServers";
    public const string CanWriteToolServers = "CanWriteToolServers";
    public const string CanReadCapabilities = "CanReadCapabilities";
    public const string CanWriteCapabilities = "CanWriteCapabilities";
    public const string CanRouteCapabilities = "CanRouteCapabilities";
    public const string CanReadAuditLogs = "CanReadAuditLogs";
    public const string CanAccessServiceAccountCredentials = "CanAccessServiceAccountCredentials";
    public const string CanManageApiKeys = "CanManageApiKeys";
    public const string AgentSelfAccess = "AgentSelfAccess";

    /// <summary>
    /// Configure all authorization policies
    /// </summary>
    public static void ConfigurePolicies(AuthorizationOptions options)
    {
        // Role-based policies
        options.AddPolicy(RequireAdmin, policy =>
            policy.RequireRole("Admin"));

        options.AddPolicy(RequireOperator, policy =>
            policy.RequireRole("Admin", "Operator"));

        options.AddPolicy(RequireViewer, policy =>
            policy.RequireRole("Admin", "Operator", "Viewer"));

        options.AddPolicy(RequireAgent, policy =>
            policy.RequireRole("Agent"));

        options.AddPolicy(RequireToolServer, policy =>
            policy.RequireRole("ToolServer"));

        // Permission-based policies
        options.AddPolicy(CanReadCredentials, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.CredentialsRead)));

        options.AddPolicy(CanWriteCredentials, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.CredentialsWrite)));

        options.AddPolicy(CanReadServiceAccounts, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.ServiceAccountsRead)));

        options.AddPolicy(CanWriteServiceAccounts, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.ServiceAccountsWrite)));

        options.AddPolicy(CanReadAgents, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.AgentsRead)));

        options.AddPolicy(CanWriteAgents, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.AgentsWrite)));

        options.AddPolicy(CanReadToolServers, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.ToolServersRead)));

        options.AddPolicy(CanWriteToolServers, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.ToolServersWrite)));

        options.AddPolicy(CanReadCapabilities, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.CapabilitiesRead)));

        options.AddPolicy(CanWriteCapabilities, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.CapabilitiesWrite)));

        options.AddPolicy(CanRouteCapabilities, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.CapabilitiesRoute)));

        options.AddPolicy(CanReadAuditLogs, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.AuditRead)));

        // Special credential access policy (allows both admins and agents to read, but agents are scoped)
        options.AddPolicy(CanAccessServiceAccountCredentials, policy =>
            policy.Requirements.Add(new PermissionRequirement(Permissions.CredentialsRead)));

        // API key management (admin and operator only)
        options.AddPolicy(CanManageApiKeys, policy =>
            policy.RequireRole("Admin", "Operator"));

        // Agent self-access (for agents to get their own configuration)
        options.AddPolicy(AgentSelfAccess, policy =>
            policy.RequireRole("Agent", "Admin"));
    }
}
