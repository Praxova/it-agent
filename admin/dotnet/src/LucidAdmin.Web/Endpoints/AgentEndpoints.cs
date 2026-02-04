using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Models;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

/// <summary>
/// API endpoints for managing AI agents.
/// </summary>
public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents")
            .WithTags("Agents")
            .AllowAnonymous();  // For development and Python agent access

        // GET /api/agents - List all agents
        group.MapGet("/", async (IAgentRepository repository) =>
        {
            var agents = await repository.GetAllAsync();
            return Results.Ok(agents.Select(MapToResponse));
        });

        // GET /api/agents/{id} - Get single agent
        group.MapGet("/{id:guid}", async (Guid id, IAgentRepository repository) =>
        {
            var agent = await repository.GetByIdAsync(id);
            if (agent == null)
            {
                throw new EntityNotFoundException("Agent", id);
            }
            return Results.Ok(MapToResponse(agent));
        });

        // GET /api/agents/name/{name} - Get agent by name
        group.MapGet("/name/{name}", async (string name, IAgentRepository repository) =>
        {
            var agent = await repository.GetByNameAsync(name);
            if (agent == null)
            {
                return Results.NotFound();
            }
            return Results.Ok(MapToResponse(agent));
        });

        // GET /api/agents/status/{status} - Get agents by status
        group.MapGet("/status/{status}", async (string status, IAgentRepository repository) =>
        {
            if (!Enum.TryParse<AgentStatus>(status, true, out var agentStatus))
            {
                return Results.BadRequest($"Invalid agent status: {status}");
            }

            var agents = await repository.GetByStatusAsync(agentStatus);
            return Results.Ok(agents.Select(MapToResponse));
        });

        // POST /api/agents - Create agent
        group.MapPost("/", async (
            [FromBody] CreateAgentRequest request,
            IAgentRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var existingByName = await repository.GetByNameAsync(request.Name);
            if (existingByName != null)
            {
                throw new DuplicateEntityException("Agent", request.Name);
            }

            var agent = new Agent
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                HostName = request.HostName,
                Description = request.Description,
                LlmServiceAccountId = request.LlmServiceAccountId,
                ServiceNowAccountId = request.ServiceNowAccountId,
                WorkflowDefinitionId = request.WorkflowDefinitionId,
                AssignmentGroup = request.AssignmentGroup,
                IsEnabled = true,
                Status = AgentStatus.Unknown
            };

            await repository.AddAsync(agent);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.AgentCreated,
                TargetResource = agent.Name,
                Success = true,
                PerformedBy = "Admin"
            });

            return Results.Created($"/api/agents/{agent.Id}", MapToResponse(agent));
        });

        // PUT /api/agents/{id} - Update agent
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateAgentRequest request,
            IAgentRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var agent = await repository.GetByIdAsync(id);
            if (agent == null)
            {
                throw new EntityNotFoundException("Agent", id);
            }

            if (request.DisplayName != null) agent.DisplayName = request.DisplayName;
            if (request.Description != null) agent.Description = request.Description;
            if (request.LlmServiceAccountId.HasValue) agent.LlmServiceAccountId = request.LlmServiceAccountId;
            if (request.ServiceNowAccountId.HasValue) agent.ServiceNowAccountId = request.ServiceNowAccountId;
            agent.WorkflowDefinitionId = request.WorkflowDefinitionId;
            if (request.AssignmentGroup != null) agent.AssignmentGroup = request.AssignmentGroup;
            if (request.IsEnabled.HasValue) agent.IsEnabled = request.IsEnabled.Value;

            await repository.UpdateAsync(agent);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.AgentUpdated,
                TargetResource = agent.Name,
                Success = true,
                PerformedBy = "Admin"
            });

            return Results.Ok(MapToResponse(agent));
        });

        // DELETE /api/agents/{id} - Delete agent
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IAgentRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var agent = await repository.GetByIdAsync(id);
            if (agent == null)
            {
                throw new EntityNotFoundException("Agent", id);
            }

            await repository.DeleteAsync(id);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.AgentDeleted,
                TargetResource = agent.Name,
                Success = true,
                PerformedBy = "Admin"
            });

            return Results.NoContent();
        });

        // POST /api/agents/{id}/heartbeat - Update heartbeat
        group.MapPost("/{id:guid}/heartbeat", async (
            Guid id,
            [FromBody] AgentHeartbeatRequest request,
            IAgentRepository repository) =>
        {
            var agent = await repository.GetByIdAsync(id);
            if (agent == null)
            {
                throw new EntityNotFoundException("Agent", id);
            }

            if (Enum.TryParse<AgentStatus>(request.Status, true, out var status))
            {
                await repository.UpdateStatusAsync(id, status);
            }

            await repository.UpdateHeartbeatAsync(id);

            return Results.Ok();
        });

        // POST /api/agents/{id}/status - Update status
        group.MapPost("/{id:guid}/status", async (
            Guid id,
            [FromBody] AgentStatusUpdateRequest request,
            IAgentRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var agent = await repository.GetByIdAsync(id);
            if (agent == null)
            {
                throw new EntityNotFoundException("Agent", id);
            }

            if (!Enum.TryParse<AgentStatus>(request.Status, true, out var status))
            {
                return Results.BadRequest($"Invalid status: {request.Status}");
            }

            await repository.UpdateStatusAsync(id, status);

            var auditAction = status switch
            {
                AgentStatus.Running => AuditAction.AgentStarted,
                AgentStatus.Stopped => AuditAction.AgentStopped,
                _ => AuditAction.AgentUpdated
            };

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = auditAction,
                TargetResource = agent.Name,
                Success = true,
                PerformedBy = "System",
                DetailsJson = $"{{\"new_status\": \"{status}\"}}"
            });

            return Results.Ok();
        });

        // GET /api/agents/{id}/export - Export agent definition
        group.MapGet("/{id:guid}/export", async (
            Guid id,
            IAgentExportService exportService,
            CancellationToken ct) =>
        {
            var export = await exportService.ExportAgentAsync(id, ct);
            return export is null
                ? Results.NotFound(new { error = $"Agent with ID {id} not found" })
                : Results.Ok(export);
        })
        .AllowAnonymous()
        .WithName("ExportAgent")
        .WithSummary("Export complete agent definition")
        .WithDescription("Returns all linked data (workflow, rulesets, examples) as a self-contained JSON document. Credentials are exported as references only.")
        .Produces<AgentExportResponse>(200)
        .Produces(404);

        // GET /api/agents/by-name/{name}/export - Export agent by name
        group.MapGet("/by-name/{name}/export", async (
            string name,
            IAgentExportService exportService,
            CancellationToken ct) =>
        {
            var export = await exportService.ExportAgentByNameAsync(name, ct);
            return export is null
                ? Results.NotFound(new { error = $"Agent with name '{name}' not found" })
                : Results.Ok(export);
        })
        .AllowAnonymous()
        .WithName("ExportAgentByName")
        .WithSummary("Export complete agent definition by name")
        .Produces<AgentExportResponse>(200)
        .Produces(404);
    }

    private static AgentResponse MapToResponse(Agent agent) => new(
        Id: agent.Id,
        Name: agent.Name,
        DisplayName: agent.DisplayName,
        HostName: agent.HostName,
        Description: agent.Description,
        Status: agent.Status.ToString(),
        LlmServiceAccountId: agent.LlmServiceAccountId,
        ServiceNowAccountId: agent.ServiceNowAccountId,
        WorkflowDefinitionId: agent.WorkflowDefinitionId,
        WorkflowName: agent.WorkflowDefinition?.Name,
        AssignmentGroup: agent.AssignmentGroup,
        LastActivity: agent.LastActivity,
        LastHeartbeat: agent.LastHeartbeat,
        TicketsProcessed: agent.TicketsProcessed,
        IsEnabled: agent.IsEnabled,
        CreatedAt: agent.CreatedAt,
        UpdatedAt: agent.UpdatedAt
    );
}
