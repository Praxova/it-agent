using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class CapabilityMappingEndpoints
{
    public static void MapCapabilityMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/capability-mappings")
            .WithTags("Capability Mappings")
            .RequireAuthorization();

        group.MapGet("/", async (ICapabilityMappingRepository repository) =>
        {
            var mappings = await repository.GetAllAsync();
            return Results.Ok(mappings.Select(MapToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, ICapabilityMappingRepository repository) =>
        {
            var mapping = await repository.GetByIdAsync(id);
            if (mapping == null)
            {
                throw new EntityNotFoundException("CapabilityMapping", id);
            }
            return Results.Ok(MapToResponse(mapping));
        });

        group.MapGet("/service-account/{serviceAccountId:guid}", async (
            Guid serviceAccountId,
            ICapabilityMappingRepository repository) =>
        {
            var mappings = await repository.GetByServiceAccountIdAsync(serviceAccountId);
            return Results.Ok(mappings.Select(MapToResponse));
        });

        group.MapGet("/tool-server/{toolServerId:guid}", async (
            Guid toolServerId,
            ICapabilityMappingRepository repository) =>
        {
            var mappings = await repository.GetByToolServerIdAsync(toolServerId);
            return Results.Ok(mappings.Select(MapToResponse));
        });

        group.MapPost("/", async (
            [FromBody] CreateCapabilityMappingRequest request,
            ICapabilityMappingRepository repository,
            IServiceAccountRepository serviceAccountRepository,
            IToolServerRepository toolServerRepository,
            ICapabilityRepository capabilityRepository,
            ICapabilityRegistry capabilityRegistry,
            IAuditEventRepository auditRepository) =>
        {
            var serviceAccount = await serviceAccountRepository.GetByIdAsync(request.ServiceAccountId);
            if (serviceAccount == null)
            {
                throw new EntityNotFoundException("ServiceAccount", request.ServiceAccountId);
            }

            var toolServer = await toolServerRepository.GetByIdAsync(request.ToolServerId);
            if (toolServer == null)
            {
                throw new EntityNotFoundException("ToolServer", request.ToolServerId);
            }

            // Verify capability exists
            var capability = await capabilityRepository.GetByIdAsync(request.CapabilityId);
            if (capability == null)
            {
                return Results.BadRequest(new { error = $"Unknown capability: {request.CapabilityId}" });
            }

            // Validate service account provider is compatible with capability requirements
            if (!string.IsNullOrEmpty(capability.RequiredProvidersJson))
            {
                var requiredProviders = System.Text.Json.JsonSerializer.Deserialize<string[]>(capability.RequiredProvidersJson);
                if (requiredProviders != null && requiredProviders.Length > 0)
                {
                    if (!requiredProviders.Contains(serviceAccount.Provider, StringComparer.OrdinalIgnoreCase))
                    {
                        return Results.BadRequest(new
                        {
                            error = $"Capability '{request.CapabilityId}' requires a service account with provider: {string.Join(" or ", requiredProviders)}. " +
                                    $"The selected service account '{serviceAccount.Name}' uses provider '{serviceAccount.Provider}'."
                        });
                    }
                }
            }

            // Validate capability dependencies are satisfied (all dependencies must have mappings on same tool server)
            if (!string.IsNullOrEmpty(capability.DependenciesJson))
            {
                var dependencies = System.Text.Json.JsonSerializer.Deserialize<string[]>(capability.DependenciesJson);
                if (dependencies != null && dependencies.Length > 0)
                {
                    var existingMappings = await repository.GetByToolServerIdAsync(request.ToolServerId);
                    var mappedCapabilities = existingMappings
                        .Where(m => m.IsEnabled)
                        .Select(m => m.CapabilityId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var missingDeps = dependencies.Where(d => !mappedCapabilities.Contains(d)).ToList();
                    if (missingDeps.Any())
                    {
                        return Results.BadRequest(new
                        {
                            error = $"Capability '{request.CapabilityId}' depends on: {string.Join(", ", missingDeps)}. " +
                                    $"Please create mappings for these capabilities on this tool server first."
                        });
                    }
                }
            }

            // Validate configuration if provided
            if (!string.IsNullOrEmpty(request.Configuration))
            {
                var provider = capabilityRegistry.GetProvider(request.CapabilityId);
                if (provider != null)
                {
                    var validation = provider.ValidateConfiguration(request.Configuration);
                    if (!validation.IsValid)
                    {
                        return Results.BadRequest(new { errors = validation.Errors });
                    }
                }
            }

            var mapping = new CapabilityMapping
            {
                ServiceAccountId = request.ServiceAccountId,
                ToolServerId = request.ToolServerId,
                CapabilityId = request.CapabilityId,
                CapabilityVersion = request.CapabilityVersion,
                Configuration = request.Configuration,
                AllowedScopesJson = request.AllowedScopesJson,
                DeniedScopesJson = request.DeniedScopesJson,
                IsEnabled = true
            };

            await repository.AddAsync(mapping);

            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = toolServer.Id,
                Action = AuditAction.CapabilityMappingCreated,
                PerformedBy = "System",
                TargetResource = $"{serviceAccount.Name} - {request.CapabilityId}",
                Success = true
            });

            return Results.Created($"/api/capability-mappings/{mapping.Id}", MapToResponse(mapping));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateCapabilityMappingRequest request,
            ICapabilityMappingRepository repository,
            ICapabilityRegistry capabilityRegistry,
            IAuditEventRepository auditRepository) =>
        {
            var mapping = await repository.GetByIdAsync(id);
            if (mapping == null)
            {
                throw new EntityNotFoundException("CapabilityMapping", id);
            }

            // Validate configuration if being updated
            if (request.Configuration != null)
            {
                var provider = capabilityRegistry.GetProvider(mapping.CapabilityId);
                if (provider != null)
                {
                    var validation = provider.ValidateConfiguration(request.Configuration);
                    if (!validation.IsValid)
                    {
                        return Results.BadRequest(new { errors = validation.Errors });
                    }
                }
            }

            if (request.CapabilityVersion != null) mapping.CapabilityVersion = request.CapabilityVersion;
            if (request.Configuration != null) mapping.Configuration = request.Configuration;
            if (request.AllowedScopesJson != null) mapping.AllowedScopesJson = request.AllowedScopesJson;
            if (request.DeniedScopesJson != null) mapping.DeniedScopesJson = request.DeniedScopesJson;
            if (request.IsEnabled.HasValue) mapping.IsEnabled = request.IsEnabled.Value;

            await repository.UpdateAsync(mapping);

            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = mapping.ToolServerId,
                Action = AuditAction.CapabilityMappingUpdated,
                PerformedBy = "System",
                TargetResource = mapping.Id.ToString(),
                Success = true
            });

            return Results.Ok(MapToResponse(mapping));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICapabilityMappingRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var mapping = await repository.GetByIdAsync(id);
            if (mapping == null)
            {
                throw new EntityNotFoundException("CapabilityMapping", id);
            }

            await repository.DeleteAsync(id);

            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = mapping.ToolServerId,
                Action = AuditAction.CapabilityMappingDeleted,
                PerformedBy = "System",
                TargetResource = mapping.Id.ToString(),
                Success = true
            });

            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);
    }

    private static CapabilityMappingResponse MapToResponse(CapabilityMapping mapping) => new(
        Id: mapping.Id,
        ServiceAccountId: mapping.ServiceAccountId,
        ToolServerId: mapping.ToolServerId,
        CapabilityId: mapping.CapabilityId,
        CapabilityVersion: mapping.CapabilityVersion,
        Configuration: mapping.Configuration,
        AllowedScopesJson: mapping.AllowedScopesJson,
        DeniedScopesJson: mapping.DeniedScopesJson,
        IsEnabled: mapping.IsEnabled,
        HealthStatus: mapping.HealthStatus.ToString(),
        LastHealthCheck: mapping.LastHealthCheck,
        LastHealthMessage: mapping.LastHealthMessage,
        CreatedAt: mapping.CreatedAt,
        UpdatedAt: mapping.UpdatedAt,
        ServiceAccountName: mapping.ServiceAccount?.Name,
        ToolServerName: mapping.ToolServer?.Name,
        CapabilityDisplayName: mapping.Capability?.DisplayName,
        CapabilityCategory: mapping.Capability?.Category
    );
}
