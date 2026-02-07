using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approvals")
            .WithTags("Approvals");

        // ================================================================
        // Agent-facing endpoints
        // ================================================================

        // POST /api/approvals — Agent submits an approval request
        group.MapPost("/", async (SubmitApprovalRequest request, LucidDbContext db) =>
        {
            var now = DateTime.UtcNow;

            var approval = new ApprovalRequest
            {
                WorkflowName = request.WorkflowName,
                StepName = request.StepName,
                AgentName = request.AgentName,
                TicketId = request.TicketId,
                TicketShortDescription = request.TicketShortDescription,
                ProposedAction = request.ProposedAction,
                ContextSnapshotJson = request.ContextSnapshot != null
                    ? JsonSerializer.Serialize(request.ContextSnapshot)
                    : "{}",
                ResumeAfterStep = request.ResumeAfterStep,
                WorkflowDefinitionId = request.WorkflowDefinitionId,
                AutoApproveThreshold = request.AutoApproveThreshold,
                Confidence = request.Confidence,
                TimeoutMinutes = request.TimeoutMinutes,
                CreatedAt = now,
            };

            // Compute expiration
            if (request.TimeoutMinutes.HasValue && request.TimeoutMinutes.Value > 0)
            {
                approval.ExpiresAt = now.AddMinutes(request.TimeoutMinutes.Value);
            }

            // Auto-approve if confidence meets threshold
            if (request.Confidence.HasValue && request.AutoApproveThreshold.HasValue
                && request.Confidence.Value >= request.AutoApproveThreshold.Value)
            {
                approval.Status = ApprovalStatus.AutoApproved;
                approval.WasAutoApproved = true;
                approval.DecidedBy = "system";
                approval.DecidedAt = now;
            }

            db.ApprovalRequests.Add(approval);
            await db.SaveChangesAsync();

            return Results.Created($"/api/approvals/{approval.Id}", MapToResponse(approval));
        });

        // GET /api/approvals/actionable?agentName={name} — Agent polls for decisions
        group.MapGet("/actionable", async (string agentName, LucidDbContext db) =>
        {
            // First, expire any timed-out pending approvals
            await ExpireTimedOutApprovals(db);

            var actionable = await db.ApprovalRequests
                .Where(a => a.AgentName == agentName
                    && a.AcknowledgedAt == null
                    && (a.Status == ApprovalStatus.Approved
                        || a.Status == ApprovalStatus.Rejected
                        || a.Status == ApprovalStatus.AutoApproved
                        || a.Status == ApprovalStatus.TimedOut))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();

            return Results.Ok(actionable.Select(MapToDetailResponse));
        });

        // POST /api/approvals/{id}/acknowledge — Agent marks approval as consumed
        group.MapPost("/{id:guid}/acknowledge", async (
            Guid id, AcknowledgeRequest request, LucidDbContext db) =>
        {
            var approval = await db.ApprovalRequests.FindAsync(id);
            if (approval is null)
                return Results.NotFound(new { error = "ApprovalNotFound" });

            if (!string.Equals(approval.AgentName, request.AgentName, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "AgentMismatch", message = "Agent name does not match" });

            if (approval.AcknowledgedAt.HasValue)
                return Results.BadRequest(new { error = "AlreadyAcknowledged" });

            approval.AcknowledgedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { id = approval.Id, acknowledgedAt = approval.AcknowledgedAt });
        });

        // ================================================================
        // Portal-facing endpoints
        // ================================================================

        // GET /api/approvals?status={status}&agentName={name}&page={page}&pageSize={pageSize}
        group.MapGet("/", async (
            string? status, string? agentName,
            int? page, int? pageSize,
            LucidDbContext db) =>
        {
            // Expire timed-out approvals before listing
            await ExpireTimedOutApprovals(db);

            var query = db.ApprovalRequests.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ApprovalStatus>(status, true, out var statusEnum))
                query = query.Where(a => a.Status == statusEnum);

            if (!string.IsNullOrEmpty(agentName))
                query = query.Where(a => a.AgentName == agentName);

            var currentPage = Math.Max(page ?? 1, 1);
            var currentPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(a => a.CreatedAt)
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

        // GET /api/approvals/{id} — Get full approval detail
        group.MapGet("/{id:guid}", async (Guid id, LucidDbContext db) =>
        {
            var approval = await db.ApprovalRequests.FindAsync(id);
            if (approval is null)
                return Results.NotFound(new { error = "ApprovalNotFound" });

            return Results.Ok(MapToDetailResponse(approval));
        });

        // PUT /api/approvals/{id}/decide — Human approves or rejects
        group.MapPut("/{id:guid}/decide", async (Guid id, DecideRequest request, LucidDbContext db) =>
        {
            var approval = await db.ApprovalRequests.FindAsync(id);
            if (approval is null)
                return Results.NotFound(new { error = "ApprovalNotFound" });

            if (approval.Status != ApprovalStatus.Pending)
                return Results.BadRequest(new { error = "NotPending", message = $"Cannot decide on approval in {approval.Status} state" });

            if (!Enum.TryParse<ApprovalStatus>(request.Status, true, out var newStatus)
                || (newStatus != ApprovalStatus.Approved && newStatus != ApprovalStatus.Rejected))
            {
                return Results.BadRequest(new { error = "InvalidStatus", message = "Status must be 'Approved' or 'Rejected'" });
            }

            approval.Status = newStatus;
            approval.Decision = request.Decision;
            approval.DecidedBy = request.DecidedBy;
            approval.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(MapToResponse(approval));
        });

        // GET /api/approvals/stats — Dashboard metrics
        group.MapGet("/stats", async (LucidDbContext db) =>
        {
            var today = DateTime.UtcNow.Date;

            var pendingCount = await db.ApprovalRequests
                .CountAsync(a => a.Status == ApprovalStatus.Pending);

            var autoApprovedToday = await db.ApprovalRequests
                .CountAsync(a => a.Status == ApprovalStatus.AutoApproved && a.DecidedAt >= today);

            var humanApprovedToday = await db.ApprovalRequests
                .CountAsync(a => a.Status == ApprovalStatus.Approved && a.DecidedAt >= today);

            var rejectedToday = await db.ApprovalRequests
                .CountAsync(a => a.Status == ApprovalStatus.Rejected && a.DecidedAt >= today);

            var timedOutToday = await db.ApprovalRequests
                .CountAsync(a => a.Status == ApprovalStatus.TimedOut && a.UpdatedAt >= today);

            // Average wait time for decided items today (in minutes)
            var decidedToday = await db.ApprovalRequests
                .Where(a => a.DecidedAt >= today && a.DecidedAt != null)
                .Select(a => new { a.CreatedAt, a.DecidedAt })
                .ToListAsync();

            double? avgWaitMinutes = decidedToday.Count > 0
                ? decidedToday.Average(a => (a.DecidedAt!.Value - a.CreatedAt).TotalMinutes)
                : null;

            return Results.Ok(new
            {
                pendingCount,
                autoApprovedToday,
                humanApprovedToday,
                rejectedToday,
                timedOutToday,
                avgWaitMinutes = avgWaitMinutes.HasValue ? Math.Round(avgWaitMinutes.Value, 1) : (double?)null,
            });
        });
    }

    /// <summary>
    /// Expire any pending approvals that have passed their ExpiresAt time.
    /// </summary>
    private static async Task ExpireTimedOutApprovals(LucidDbContext db)
    {
        var now = DateTime.UtcNow;
        var expired = await db.ApprovalRequests
            .Where(a => a.Status == ApprovalStatus.Pending
                && a.ExpiresAt != null
                && a.ExpiresAt <= now)
            .ToListAsync();

        if (expired.Count > 0)
        {
            foreach (var a in expired)
            {
                a.Status = ApprovalStatus.TimedOut;
            }
            await db.SaveChangesAsync();
        }
    }

    private static ApprovalResponse MapToResponse(ApprovalRequest a)
    {
        return new ApprovalResponse(
            a.Id, a.WorkflowName, a.StepName, a.AgentName,
            a.TicketId, a.TicketShortDescription, a.ProposedAction,
            a.ResumeAfterStep, a.WorkflowDefinitionId,
            a.AutoApproveThreshold, a.Confidence, a.WasAutoApproved,
            a.Status.ToString(), a.Decision, a.DecidedBy, a.DecidedAt,
            a.TimeoutMinutes, a.ExpiresAt, a.AcknowledgedAt,
            a.CreatedAt, a.UpdatedAt
        );
    }

    private static ApprovalDetailResponse MapToDetailResponse(ApprovalRequest a)
    {
        Dictionary<string, object>? contextSnapshot = null;
        if (!string.IsNullOrEmpty(a.ContextSnapshotJson))
        {
            try
            {
                contextSnapshot = JsonSerializer.Deserialize<Dictionary<string, object>>(a.ContextSnapshotJson);
            }
            catch (JsonException) { /* Leave null if malformed */ }
        }

        return new ApprovalDetailResponse(
            a.Id, a.WorkflowName, a.StepName, a.AgentName,
            a.TicketId, a.TicketShortDescription, a.ProposedAction,
            contextSnapshot, a.ResumeAfterStep, a.WorkflowDefinitionId,
            a.AutoApproveThreshold, a.Confidence, a.WasAutoApproved,
            a.Status.ToString(), a.Decision, a.DecidedBy, a.DecidedAt,
            a.TimeoutMinutes, a.ExpiresAt, a.AcknowledgedAt,
            a.CreatedAt, a.UpdatedAt
        );
    }
}

// === Request records ===

public record SubmitApprovalRequest(
    string WorkflowName,
    string StepName,
    string AgentName,
    string TicketId,
    string? TicketShortDescription,
    string ProposedAction,
    Dictionary<string, object>? ContextSnapshot,
    string ResumeAfterStep,
    Guid? WorkflowDefinitionId,
    decimal? AutoApproveThreshold,
    decimal? Confidence,
    int? TimeoutMinutes
);

public record AcknowledgeRequest(string AgentName);

public record DecideRequest(string Status, string? Decision, string? DecidedBy);

// === Response records ===

public record ApprovalResponse(
    Guid Id, string WorkflowName, string StepName, string AgentName,
    string TicketId, string? TicketShortDescription, string ProposedAction,
    string ResumeAfterStep, Guid? WorkflowDefinitionId,
    decimal? AutoApproveThreshold, decimal? Confidence, bool WasAutoApproved,
    string Status, string? Decision, string? DecidedBy, DateTime? DecidedAt,
    int? TimeoutMinutes, DateTime? ExpiresAt, DateTime? AcknowledgedAt,
    DateTime CreatedAt, DateTime UpdatedAt
);

public record ApprovalDetailResponse(
    Guid Id, string WorkflowName, string StepName, string AgentName,
    string TicketId, string? TicketShortDescription, string ProposedAction,
    Dictionary<string, object>? ContextSnapshot,
    string ResumeAfterStep, Guid? WorkflowDefinitionId,
    decimal? AutoApproveThreshold, decimal? Confidence, bool WasAutoApproved,
    string Status, string? Decision, string? DecidedBy, DateTime? DecidedAt,
    int? TimeoutMinutes, DateTime? ExpiresAt, DateTime? AcknowledgedAt,
    DateTime CreatedAt, DateTime UpdatedAt
);
