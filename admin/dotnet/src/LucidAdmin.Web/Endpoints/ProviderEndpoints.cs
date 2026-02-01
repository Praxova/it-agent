using LucidAdmin.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class ProviderEndpoints
{
    public static void MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/providers")
            .WithTags("Providers")
            .RequireAuthorization();

        // GET /api/v1/providers - List all providers
        group.MapGet("/", (IProviderRegistry registry, [FromQuery] bool includeStubs = false) =>
        {
            var providers = includeStubs
                ? registry.GetProviderInfos()
                : registry.GetProviderInfos().Where(p => p.IsImplemented);

            return Results.Ok(providers);
        });

        // GET /api/v1/providers/{providerId} - Get provider details
        group.MapGet("/{providerId}", (string providerId, IProviderRegistry registry) =>
        {
            var provider = registry.GetProvider(providerId);
            if (provider == null)
            {
                return Results.NotFound($"Provider '{providerId}' not found");
            }

            return Results.Ok(new
            {
                provider.ProviderId,
                provider.DisplayName,
                provider.Description,
                provider.IsImplemented,
                AccountTypes = provider.SupportedAccountTypes,
                CredentialStorageTypes = provider.SupportedCredentialStorage.Select(c => c.ToString())
            });
        });

        // GET /api/v1/providers/{providerId}/schema/{accountType} - Get config schema
        group.MapGet("/{providerId}/schema/{accountType}", (string providerId, string accountType, IProviderRegistry registry) =>
        {
            var provider = registry.GetProvider(providerId);
            if (provider == null)
            {
                return Results.NotFound($"Provider '{providerId}' not found");
            }

            var schema = provider.GetConfigurationSchema(accountType);
            return Results.Content(schema, "application/json");
        });

        // GET /api/v1/providers/{providerId}/example/{accountType} - Get config example
        group.MapGet("/{providerId}/example/{accountType}", (string providerId, string accountType, IProviderRegistry registry) =>
        {
            var provider = registry.GetProvider(providerId);
            if (provider == null)
            {
                return Results.NotFound($"Provider '{providerId}' not found");
            }

            var example = provider.GetConfigurationExample(accountType);
            return Results.Content(example, "application/json");
        });
    }
}
