using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class CapabilityEndpoints
{
    public static void MapCapabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/capabilities")
            .WithTags("Capabilities")
            .RequireAuthorization();

        // GET /api/v1/capabilities - List all capabilities
        group.MapGet("/", async (
            ICapabilityRepository repo,
            [FromQuery] string? category = null,
            [FromQuery] bool enabledOnly = false) =>
        {
            var capabilities = enabledOnly
                ? await repo.GetEnabledAsync()
                : category != null
                    ? await repo.GetByCategoryAsync(category)
                    : await repo.GetAllAsync();

            // Project to DTOs to avoid circular reference from Mappings navigation property
            var response = capabilities.Select(c => new
            {
                c.Id,
                c.CapabilityId,
                c.Version,
                c.Category,
                c.DisplayName,
                c.Description,
                c.RequiresServiceAccount,
                RequiredProviders = string.IsNullOrEmpty(c.RequiredProvidersJson)
                    ? Array.Empty<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<string[]>(c.RequiredProvidersJson) ?? Array.Empty<string>(),
                c.IsBuiltIn,
                c.IsEnabled,
                c.CreatedAt,
                c.UpdatedAt
            });

            return Results.Ok(response);
        });

        // GET /api/v1/capabilities/categories - List all categories
        group.MapGet("/categories", (ICapabilityRegistry registry) =>
        {
            return Results.Ok(registry.GetCategories());
        });

        // GET /api/v1/capabilities/{capabilityId} - Get capability details
        group.MapGet("/{capabilityId}", async (string capabilityId, ICapabilityRepository repo) =>
        {
            var capability = await repo.GetByIdAsync(capabilityId);
            if (capability == null) return Results.NotFound();

            return Results.Ok(new
            {
                capability.Id,
                capability.CapabilityId,
                capability.Version,
                capability.Category,
                capability.DisplayName,
                capability.Description,
                capability.RequiresServiceAccount,
                RequiredProviders = string.IsNullOrEmpty(capability.RequiredProvidersJson)
                    ? Array.Empty<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<string[]>(capability.RequiredProvidersJson) ?? Array.Empty<string>(),
                capability.IsBuiltIn,
                capability.IsEnabled,
                capability.CreatedAt,
                capability.UpdatedAt
            });
        });

        // GET /api/v1/capabilities/{capabilityId}/details - Get capability with resolved dependencies
        group.MapGet("/{capabilityId}/details", async (
            string capabilityId,
            ICapabilityRepository repo) =>
        {
            var capability = await repo.GetByIdAsync(capabilityId);
            if (capability == null) return Results.NotFound();

            // Resolve dependency names
            var dependencyDetails = new List<object>();
            if (!string.IsNullOrEmpty(capability.DependenciesJson))
            {
                var depIds = System.Text.Json.JsonSerializer.Deserialize<string[]>(capability.DependenciesJson);
                if (depIds != null)
                {
                    foreach (var depId in depIds)
                    {
                        var dep = await repo.GetByIdAsync(depId);
                        dependencyDetails.Add(new
                        {
                            capabilityId = depId,
                            displayName = dep?.DisplayName ?? depId,
                            exists = dep != null
                        });
                    }
                }
            }

            // Parse required providers
            var requiredProviders = string.IsNullOrEmpty(capability.RequiredProvidersJson)
                ? Array.Empty<string>()
                : System.Text.Json.JsonSerializer.Deserialize<string[]>(capability.RequiredProvidersJson) ?? Array.Empty<string>();

            return Results.Ok(new
            {
                capability.Id,
                capability.CapabilityId,
                capability.Version,
                capability.Category,
                capability.DisplayName,
                capability.Description,
                capability.RequiresServiceAccount,
                RequiredProviders = requiredProviders,
                Dependencies = dependencyDetails,
                capability.MinToolServerVersion,
                capability.ConfigurationSchema,
                capability.ConfigurationExample,
                capability.IsBuiltIn,
                capability.IsEnabled,
                capability.DocumentationUrl,
                capability.CreatedAt,
                capability.UpdatedAt
            });
        });

        // GET /api/v1/capabilities/{capabilityId}/schema - Get configuration schema
        group.MapGet("/{capabilityId}/schema", (string capabilityId, ICapabilityRegistry registry) =>
        {
            var provider = registry.GetProvider(capabilityId);
            if (provider == null) return Results.NotFound();
            return Results.Content(provider.GetConfigurationSchema(), "application/json");
        });

        // GET /api/v1/capabilities/{capabilityId}/example - Get configuration example
        group.MapGet("/{capabilityId}/example", (string capabilityId, ICapabilityRegistry registry) =>
        {
            var provider = registry.GetProvider(capabilityId);
            if (provider == null) return Results.NotFound();
            return Results.Content(provider.GetConfigurationExample(), "application/json");
        });

        // POST /api/v1/capabilities/{capabilityId}/validate - Validate configuration
        group.MapPost("/{capabilityId}/validate", (
            string capabilityId,
            [FromBody] ValidateConfigRequest request,
            ICapabilityRegistry registry) =>
        {
            var provider = registry.GetProvider(capabilityId);
            if (provider == null) return Results.NotFound();

            var result = provider.ValidateConfiguration(request.Configuration);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);
    }

    private record ValidateConfigRequest(string? Configuration);
}
