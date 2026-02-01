using Microsoft.AspNetCore.Authorization;

namespace LucidAdmin.Web.Authorization;

/// <summary>
/// Authorization requirement that checks if an agent key can access a specific service account
/// </summary>
public class ServiceAccountAccessRequirement : IAuthorizationRequirement
{
    public Guid ServiceAccountId { get; }

    public ServiceAccountAccessRequirement(Guid serviceAccountId)
    {
        ServiceAccountId = serviceAccountId;
    }
}
