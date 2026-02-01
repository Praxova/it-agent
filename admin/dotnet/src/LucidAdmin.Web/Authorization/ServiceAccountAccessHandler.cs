using System.Text.Json;
using LucidAdmin.Core.Enums;
using Microsoft.AspNetCore.Authorization;

namespace LucidAdmin.Web.Authorization;

/// <summary>
/// Authorization handler that validates service account access for agent keys
/// </summary>
public class ServiceAccountAccessHandler : AuthorizationHandler<ServiceAccountAccessRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ServiceAccountAccessRequirement requirement)
    {
        // Non-agent roles (Admin, Operator, Viewer, ToolServer) have full access
        var roleClaim = context.User.FindFirst(c => c.Type == System.Security.Claims.ClaimTypes.Role);
        if (roleClaim != null && Enum.TryParse<UserRole>(roleClaim.Value, out var role))
        {
            if (role != UserRole.Agent)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        // For Agent role, check allowed_service_accounts claim
        var allowedAccountsClaim = context.User.FindFirst(c => c.Type == "allowed_service_accounts");
        if (allowedAccountsClaim != null && !string.IsNullOrEmpty(allowedAccountsClaim.Value))
        {
            try
            {
                var allowedIds = JsonSerializer.Deserialize<List<Guid>>(allowedAccountsClaim.Value);
                if (allowedIds?.Contains(requirement.ServiceAccountId) == true)
                {
                    context.Succeed(requirement);
                }
            }
            catch (JsonException)
            {
                // Invalid JSON in claim, deny access
            }
        }

        return Task.CompletedTask;
    }
}
