using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class ClarificationEndpoints
{
    public static void MapClarificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clarifications")
            .WithTags("Clarifications");

        // ================================================================
        // Agent-facing endpoints
        // ================================================================

        // POST /api/clarifications — Agent submits a clarification request
        group.MapPost("/", async (CreateClarificationRequest request, LucidDbContext db) =>
        {
            var now = DateTime.UtcNow;

            var clarification = new Clarification
            {
                AgentName = request.AgentName,
                WorkflowName = request.WorkflowName,
                StepName = request.StepName,
                TicketId = request.TicketId,
                TicketSysId = request.TicketSysId,
                Question = request.Question,
                ContextSnapshotJson = request.ContextSnapshotJson ?? "{}",
                ResumeAfterStep = request.ResumeAfterStep,
                Status = ClarificationStatus.Pending,
                PostedAt = now,
                CreatedAt = now,
            };

            db.Clarifications.Add(clarification);
            await db.SaveChangesAsync();

            return Results.Created($"/api/clarifications/{clarification.Id}", MapToResponse(clarification));
        });

        // GET /api/clarifications/pending?agentName={name} — Agent polls for pending clarifications
        group.MapGet("/pending", async (string agentName, LucidDbContext db) =>
        {
            var pending = await db.Clarifications
                .Where(c => c.AgentName == agentName
                    && c.Status == ClarificationStatus.Pending
                    && !c.IsAcknowledged)
                .OrderBy(c => c.PostedAt)
                .ToListAsync();

            return Results.Ok(pending.Select(MapToDetailResponse));
        });

        // POST /api/clarifications/{id}/resolve — Resolve with user reply
        group.MapPost("/{id:guid}/resolve", async (Guid id, ResolveClarificationRequest request, LucidDbContext db) =>
        {
            var clarification = await db.Clarifications.FindAsync(id);
            if (clarification is null)
                return Results.NotFound(new { error = "ClarificationNotFound" });

            if (clarification.Status != ClarificationStatus.Pending)
                return Results.BadRequest(new { error = "NotPending", message = $"Cannot resolve clarification in {clarification.Status} state" });

            clarification.Status = ClarificationStatus.Resolved;
            clarification.UserReply = request.UserReply;
            clarification.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(MapToResponse(clarification));
        });

        // POST /api/clarifications/{id}/acknowledge — Agent marks as consumed
        group.MapPost("/{id:guid}/acknowledge", async (Guid id, AcknowledgeClarificationRequest request, LucidDbContext db) =>
        {
            var clarification = await db.Clarifications.FindAsync(id);
            if (clarification is null)
                return Results.NotFound(new { error = "ClarificationNotFound" });

            if (clarification.IsAcknowledged)
                return Results.BadRequest(new { error = "AlreadyAcknowledged" });

            clarification.IsAcknowledged = true;
            clarification.AcknowledgedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { id = clarification.Id, acknowledgedAt = clarification.AcknowledgedAt });
        });

        // ================================================================
        // Portal-facing endpoints
        // ================================================================

        // GET /api/clarifications/{id} — Get single clarification
        group.MapGet("/{id:guid}", async (Guid id, LucidDbContext db) =>
        {
            var clarification = await db.Clarifications.FindAsync(id);
            if (clarification is null)
                return Results.NotFound(new { error = "ClarificationNotFound" });

            return Results.Ok(MapToDetailResponse(clarification));
        });

        // GET /api/clarifications?status={status}&agentName={name}&page={page}&pageSize={pageSize}
        group.MapGet("/", async (
            string? status, string? agentName,
            int? page, int? pageSize,
            LucidDbContext db) =>
        {
            var query = db.Clarifications.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ClarificationStatus>(status, true, out var statusEnum))
                query = query.Where(c => c.Status == statusEnum);

            if (!string.IsNullOrEmpty(agentName))
                query = query.Where(c => c.AgentName == agentName);

            var currentPage = Math.Max(page ?? 1, 1);
            var currentPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .ToListAsync();

            return Results.Ok(new
            {
                items = items.Select(MapToResponse),
                totalCount,
                page = currentPage,
                pageSize = currentPageSize,
            });
        });
    }

    private static ClarificationResponse MapToResponse(Clarification c)
    {
        return new ClarificationResponse(
            c.Id, c.AgentName, c.WorkflowName, c.StepName,
            c.TicketId, c.TicketSysId, c.Question,
            c.Status.ToString(), c.PostedAt, c.ResolvedAt,
            c.UserReply, c.ResumeAfterStep,
            c.IsAcknowledged, c.AcknowledgedAt,
            c.CreatedAt, c.UpdatedAt
        );
    }

    private static ClarificationDetailResponse MapToDetailResponse(Clarification c)
    {
        return new ClarificationDetailResponse(
            c.Id, c.AgentName, c.WorkflowName, c.StepName,
            c.TicketId, c.TicketSysId, c.Question,
            c.ContextSnapshotJson,
            c.Status.ToString(), c.PostedAt, c.ResolvedAt,
            c.UserReply, c.ResumeAfterStep,
            c.IsAcknowledged, c.AcknowledgedAt,
            c.CreatedAt, c.UpdatedAt
        );
    }
}

// === Request records ===

public record CreateClarificationRequest(
    string AgentName,
    string WorkflowName,
    string StepName,
    string TicketId,
    string? TicketSysId,
    string Question,
    string? ContextSnapshotJson,
    string? ResumeAfterStep
);

public record ResolveClarificationRequest(string UserReply);

public record AcknowledgeClarificationRequest(string? AgentName);

// === Response records ===

public record ClarificationResponse(
    Guid Id, string AgentName, string WorkflowName, string StepName,
    string TicketId, string? TicketSysId, string Question,
    string Status, DateTime PostedAt, DateTime? ResolvedAt,
    string? UserReply, string? ResumeAfterStep,
    bool IsAcknowledged, DateTime? AcknowledgedAt,
    DateTime CreatedAt, DateTime UpdatedAt
);

public record ClarificationDetailResponse(
    Guid Id, string AgentName, string WorkflowName, string StepName,
    string TicketId, string? TicketSysId, string Question,
    string ContextSnapshotJson,
    string Status, DateTime PostedAt, DateTime? ResolvedAt,
    string? UserReply, string? ResumeAfterStep,
    bool IsAcknowledged, DateTime? AcknowledgedAt,
    DateTime CreatedAt, DateTime UpdatedAt
);
