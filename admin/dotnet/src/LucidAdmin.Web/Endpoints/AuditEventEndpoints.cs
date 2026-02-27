using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Services;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class AuditEventEndpoints
{
    public static void MapAuditEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit-events")
            .WithTags("Audit Events")
            .RequireAuthorization();

        group.MapGet("/", async (IAuditEventRepository repository) =>
        {
            var events = await repository.GetAllAsync();
            return Results.Ok(events.Select(MapToResponse));
        });

        group.MapGet("/tool-server/{toolServerId:guid}", async (
            Guid toolServerId,
            [FromQuery] int limit,
            IAuditEventRepository repository) =>
        {
            var events = await repository.GetByToolServerIdAsync(toolServerId, limit > 0 ? limit : 100);
            return Results.Ok(events.Select(MapToResponse));
        });

        group.MapGet("/action/{action}", async (
            string action,
            [FromQuery] int limit,
            IAuditEventRepository repository) =>
        {
            if (!Enum.TryParse<AuditAction>(action, true, out var auditAction))
            {
                return Results.BadRequest($"Invalid audit action: {action}");
            }

            var events = await repository.GetByActionAsync(auditAction, limit > 0 ? limit : 100);
            return Results.Ok(events.Select(MapToResponse));
        });

        group.MapGet("/search", async (
            [FromQuery] string? targetResource,
            [FromQuery] string? action,
            [FromQuery] Guid? toolServerId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int limit,
            IAuditEventRepository repository) =>
        {
            AuditAction? auditAction = null;
            if (!string.IsNullOrEmpty(action) && Enum.TryParse<AuditAction>(action, true, out var parsed))
            {
                auditAction = parsed;
            }

            var events = await repository.SearchAsync(
                targetResource,
                auditAction,
                toolServerId,
                from,
                to,
                limit > 0 ? limit : 100);

            return Results.Ok(events.Select(MapToResponse));
        });

        group.MapGet("/verify", async (
            [FromQuery] long? from,
            [FromQuery] long? to,
            AuditChainService chainService,
            CancellationToken ct) =>
        {
            var report = await chainService.VerifyAsync(from, to, ct);
            return Results.Ok(report);
        })
        .WithSummary("Verify audit chain integrity for a range of sequence numbers");
    }

    private static AuditEventResponse MapToResponse(Core.Entities.AuditEvent evt) => new(
        Id: evt.Id,
        CreatedAt: evt.CreatedAt,
        ToolServerId: evt.ToolServerId,
        Action: evt.Action.ToString(),
        Capability: evt.CapabilityId,
        PerformedBy: evt.PerformedBy,
        TargetResource: evt.TargetResource,
        TicketNumber: evt.TicketNumber,
        Success: evt.Success,
        ErrorMessage: evt.ErrorMessage,
        DetailsJson: evt.DetailsJson
    );
}
