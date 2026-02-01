using Microsoft.AspNetCore.Authorization;

namespace LucidAdmin.Web.Authorization;

/// <summary>
/// Authorization requirement that checks for a specific permission
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
    }
}
