using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class ToolServerEndpoints
{
    public static void MapToolServerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tool-servers")
            .WithTags("Tool Servers")
            .RequireAuthorization();

        group.MapGet("/", async (IToolServerRepository repository) =>
        {
            var servers = await repository.GetAllAsync();
            return Results.Ok(servers.Select(MapToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, IToolServerRepository repository) =>
        {
            var server = await repository.GetByIdAsync(id);
            if (server == null)
            {
                throw new EntityNotFoundException("ToolServer", id);
            }
            return Results.Ok(MapToResponse(server));
        });

        group.MapGet("/name/{name}", async (string name, IToolServerRepository repository) =>
        {
            var server = await repository.GetByNameAsync(name);
            if (server == null)
            {
                return Results.NotFound();
            }
            return Results.Ok(MapToResponse(server));
        });

        group.MapPost("/", async (
            [FromBody] CreateToolServerRequest request,
            IToolServerRepository repository,
            IPasswordHasher passwordHasher,
            IAuditEventRepository auditRepository) =>
        {
            var existingByName = await repository.GetByNameAsync(request.Name);
            if (existingByName != null)
            {
                throw new DuplicateEntityException("ToolServer", request.Name);
            }

            var existingByEndpoint = await repository.GetByEndpointAsync(request.Endpoint);
            if (existingByEndpoint != null)
            {
                throw new ValidationException("Endpoint", "Endpoint already registered");
            }

            var server = new ToolServer
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Endpoint = request.Endpoint,
                Domain = request.Domain,
                Description = request.Description,
                ApiKeyHash = request.ApiKey != null ? passwordHasher.HashPassword(request.ApiKey) : null,
                IsEnabled = true,
                Status = HealthStatus.Unknown
            };

            await repository.AddAsync(server);

            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = server.Id,
                Action = AuditAction.ToolServerRegistered,
                PerformedBy = "System",
                TargetResource = server.Name,
                Success = true
            });

            return Results.Created($"/api/tool-servers/{server.Id}", MapToResponse(server));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateToolServerRequest request,
            IToolServerRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var server = await repository.GetByIdAsync(id);
            if (server == null)
            {
                throw new EntityNotFoundException("ToolServer", id);
            }

            if (request.DisplayName != null) server.DisplayName = request.DisplayName;
            if (request.Description != null) server.Description = request.Description;
            if (request.IsEnabled.HasValue) server.IsEnabled = request.IsEnabled.Value;

            await repository.UpdateAsync(server);

            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = server.Id,
                Action = AuditAction.ToolServerRegistered,
                PerformedBy = "System",
                TargetResource = server.Name,
                Success = true
            });

            return Results.Ok(MapToResponse(server));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IToolServerRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var server = await repository.GetByIdAsync(id);
            if (server == null)
            {
                throw new EntityNotFoundException("ToolServer", id);
            }

            await repository.DeleteAsync(id);

            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = server.Id,
                Action = AuditAction.ToolServerDeregistered,
                PerformedBy = "System",
                TargetResource = server.Name,
                Success = true
            });

            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/heartbeat", async (
            Guid id,
            [FromBody] HeartbeatRequest request,
            IToolServerRepository repository,
            ICapabilityMappingRepository mappingRepo) =>
        {
            var server = await repository.GetByIdAsync(id);
            if (server == null)
            {
                throw new EntityNotFoundException("ToolServer", id);
            }

            // Update server info
            server.Version = request.Version;
            server.Status = Enum.Parse<HealthStatus>(request.Status, true);
            await repository.UpdateHeartbeatAsync(id, server.Status);

            // Update capability mappings based on reported statuses
            if (request.Capabilities != null)
            {
                var mappings = await mappingRepo.GetByToolServerIdAsync(id);
                var reportedCapabilities = request.Capabilities.ToDictionary(c => c.CapabilityId, StringComparer.OrdinalIgnoreCase);

                foreach (var mapping in mappings)
                {
                    if (reportedCapabilities.TryGetValue(mapping.CapabilityId, out var report))
                    {
                        mapping.HealthStatus = Enum.Parse<HealthStatus>(report.Status, true);
                        mapping.LastHealthCheck = DateTime.UtcNow;
                        mapping.LastHealthMessage = report.Message;
                    }
                    else
                    {
                        // Capability not reported - mark as unknown
                        mapping.HealthStatus = HealthStatus.Unknown;
                        mapping.LastHealthCheck = DateTime.UtcNow;
                        mapping.LastHealthMessage = "Capability not reported in heartbeat";
                    }
                    await mappingRepo.UpdateAsync(mapping);
                }
            }

            return Results.Ok(MapToResponse(server));
        });

        group.MapPost("/{id:guid}/test-connectivity", async (
            Guid id,
            IToolServerRepository repository,
            IAuditEventRepository auditRepository,
            IHttpClientFactory httpClientFactory) =>
        {
            var server = await repository.GetByIdAsync(id);
            if (server == null)
            {
                throw new EntityNotFoundException("ToolServer", id);
            }

            HealthStatus status;
            string message;
            DateTime checkedAt = DateTime.UtcNow;

            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                // Try to hit the tool server's health endpoint
                var healthUrl = $"{server.Endpoint.TrimEnd('/')}/api/health";
                var response = await client.GetAsync(healthUrl);

                if (response.IsSuccessStatusCode)
                {
                    status = HealthStatus.Healthy;
                    message = $"Successfully connected to {server.Endpoint}";
                }
                else
                {
                    status = HealthStatus.Unhealthy;
                    message = $"Tool server returned {response.StatusCode}";
                }
            }
            catch (HttpRequestException ex)
            {
                status = HealthStatus.Unhealthy;
                message = $"Connection failed: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                status = HealthStatus.Unhealthy;
                message = "Connection timed out after 10 seconds";
            }

            // Update server health status
            server.Status = status;
            server.LastHeartbeat = checkedAt;
            await repository.UpdateAsync(server);

            // Audit the test
            await auditRepository.AddAsync(new AuditEvent
            {
                ToolServerId = server.Id,
                Action = AuditAction.ToolServerConnectivityTest,
                PerformedBy = "System",
                TargetResource = server.Name,
                Success = status == HealthStatus.Healthy,
                ErrorMessage = status != HealthStatus.Healthy ? message : null,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { action = "test-connectivity", result = status.ToString() })
            });

            return Results.Ok(new TestConnectivityResponse(
                Status: status.ToString(),
                Message: message,
                CheckedAt: checkedAt,
                Details: null
            ));
        });
    }

    private static ToolServerResponse MapToResponse(ToolServer server) => new(
        Id: server.Id,
        Name: server.Name,
        DisplayName: server.DisplayName,
        Endpoint: server.Endpoint,
        Domain: server.Domain,
        Description: server.Description,
        Version: server.Version,
        IsEnabled: server.IsEnabled,
        Status: server.Status.ToString(),
        LastHeartbeat: server.LastHeartbeat,
        CreatedAt: server.CreatedAt,
        UpdatedAt: server.UpdatedAt
    );
}
