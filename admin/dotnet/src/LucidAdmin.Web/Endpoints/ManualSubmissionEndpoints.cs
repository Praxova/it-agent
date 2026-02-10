using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class ManualSubmissionEndpoints
{
    public static void MapManualSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/manual-submissions")
            .WithTags("ManualSubmissions")
            .RequireAuthorization();

        // POST /api/manual-submissions — Submit a new manual work item
        group.MapPost("/", async (SubmitManualRequest request, LucidDbContext db, IAgentRepository agentRepo) =>
        {
            // Resolve agent by ID or name
            Agent? agent = null;
            if (request.AgentId.HasValue)
            {
                agent = await agentRepo.GetByIdAsync(request.AgentId.Value);
            }
            else if (!string.IsNullOrEmpty(request.AgentName))
            {
                agent = await agentRepo.GetByNameAsync(request.AgentName);
            }

            if (agent is null)
                return Results.BadRequest(new { error = "AgentNotFound", message = "Agent not found by ID or name" });

            var submission = new ManualSubmission
            {
                AgentId = agent.Id,
                Title = request.Title,
                Description = request.Description ?? "",
                Requester = request.Requester,
                ExtraDataJson = request.ExtraData != null ? JsonSerializer.Serialize(request.ExtraData) : null,
                Status = ManualSubmissionStatus.Pending,
                SubmittedAt = DateTime.UtcNow,
            };

            db.ManualSubmissions.Add(submission);
            await db.SaveChangesAsync();

            return Results.Created($"/api/manual-submissions/{submission.Id}", MapToResponse(submission, agent.Name));
        }).RequireAuthorization(AuthorizationPolicies.RequireOperator);

        // GET /api/manual-submissions — List all submissions (with filters)
        group.MapGet("/", async (
            Guid? agentId,
            string? agentName,
            string? status,
            int? limit,
            LucidDbContext db,
            IAgentRepository agentRepo) =>
        {
            var query = db.ManualSubmissions.Include(s => s.Agent).AsQueryable();

            if (agentId.HasValue)
                query = query.Where(s => s.AgentId == agentId.Value);
            else if (!string.IsNullOrEmpty(agentName))
            {
                var agent = await agentRepo.GetByNameAsync(agentName);
                if (agent != null)
                    query = query.Where(s => s.AgentId == agent.Id);
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ManualSubmissionStatus>(status, true, out var statusEnum))
                query = query.Where(s => s.Status == statusEnum);

            var maxResults = Math.Min(limit ?? 50, 200);
            var submissions = await query
                .OrderByDescending(s => s.SubmittedAt)
                .Take(maxResults)
                .ToListAsync();

            return Results.Ok(submissions.Select(s => MapToResponse(s, s.Agent?.Name ?? "")));
        });

        // GET /api/manual-submissions/{id} — Get a specific submission
        group.MapGet("/{id:guid}", async (Guid id, LucidDbContext db) =>
        {
            var submission = await db.ManualSubmissions
                .Include(s => s.Agent)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (submission is null)
                return Results.NotFound(new { error = "SubmissionNotFound" });

            return Results.Ok(MapToResponse(submission, submission.Agent?.Name ?? ""));
        });

        // PATCH /api/manual-submissions/{id}/acknowledge — Mark as picked up
        group.MapPatch("/{id:guid}/acknowledge", async (Guid id, LucidDbContext db) =>
        {
            var submission = await db.ManualSubmissions.FindAsync(id);
            if (submission is null)
                return Results.NotFound(new { error = "SubmissionNotFound" });

            if (submission.Status != ManualSubmissionStatus.Pending)
                return Results.BadRequest(new { error = "InvalidState", message = $"Cannot acknowledge submission in {submission.Status} state" });

            submission.Status = ManualSubmissionStatus.InProgress;
            submission.PickedUpAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { id = submission.Id, status = "InProgress" });
        }).RequireAuthorization(AuthorizationPolicies.RequireOperator);

        // PATCH /api/manual-submissions/{id}/result — Report execution result
        group.MapPatch("/{id:guid}/result", async (Guid id, SubmitResultRequest request, LucidDbContext db) =>
        {
            var submission = await db.ManualSubmissions.FindAsync(id);
            if (submission is null)
                return Results.NotFound(new { error = "SubmissionNotFound" });

            submission.ResultStatus = request.Status;
            submission.ResultMessage = request.Message;
            submission.ResultDetailsJson = request.Details != null ? JsonSerializer.Serialize(request.Details) : null;
            submission.CompletedAt = DateTime.UtcNow;

            submission.Status = request.Status?.ToLowerInvariant() switch
            {
                "completed" => ManualSubmissionStatus.Completed,
                "escalated" => ManualSubmissionStatus.Escalated,
                "failed" => ManualSubmissionStatus.Failed,
                _ => ManualSubmissionStatus.Failed
            };

            await db.SaveChangesAsync();

            return Results.Ok(new { id = submission.Id, status = submission.Status.ToString() });
        }).RequireAuthorization(AuthorizationPolicies.RequireOperator);

        // Agent-scoped pending endpoint
        // GET /api/agents/{agentId}/manual-submissions/pending
        app.MapGet("/api/agents/{agentId:guid}/manual-submissions/pending", async (Guid agentId, LucidDbContext db) =>
        {
            var submissions = await db.ManualSubmissions
                .Where(s => s.AgentId == agentId && s.Status == ManualSubmissionStatus.Pending)
                .OrderBy(s => s.SubmittedAt)
                .ToListAsync();

            return Results.Ok(submissions.Select(MapToPendingResponse));
        }).WithTags("ManualSubmissions").RequireAuthorization();

        // GET /api/agents/by-name/{name}/manual-submissions/pending
        app.MapGet("/api/agents/by-name/{name}/manual-submissions/pending", async (
            string name, LucidDbContext db, IAgentRepository agentRepo) =>
        {
            var agent = await agentRepo.GetByNameAsync(name);
            if (agent is null)
                return Results.NotFound(new { error = "AgentNotFound" });

            var submissions = await db.ManualSubmissions
                .Where(s => s.AgentId == agent.Id && s.Status == ManualSubmissionStatus.Pending)
                .OrderBy(s => s.SubmittedAt)
                .ToListAsync();

            return Results.Ok(submissions.Select(MapToPendingResponse));
        }).WithTags("ManualSubmissions").RequireAuthorization();
    }

    private static ManualSubmissionResponse MapToResponse(ManualSubmission s, string agentName)
    {
        return new ManualSubmissionResponse(
            s.Id, s.AgentId, agentName,
            s.Title, s.Description, s.Requester,
            s.ExtraDataJson != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(s.ExtraDataJson) : null,
            s.Status.ToString(), s.SubmittedAt, s.PickedUpAt, s.CompletedAt,
            s.ResultStatus, s.ResultMessage, s.ResultDetailsJson
        );
    }

    private static PendingSubmissionResponse MapToPendingResponse(ManualSubmission s)
    {
        return new PendingSubmissionResponse(
            s.Id.ToString(), s.Title, s.Description, s.Requester,
            s.ExtraDataJson != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(s.ExtraDataJson) : null,
            s.SubmittedAt
        );
    }
}

// Request/response records
public record SubmitManualRequest(
    Guid? AgentId,
    string? AgentName,
    string Title,
    string? Description,
    string? Requester,
    Dictionary<string, object>? ExtraData
);

public record SubmitResultRequest(
    string? Status,
    string? Message,
    Dictionary<string, object>? Details
);

public record ManualSubmissionResponse(
    Guid Id, Guid AgentId, string AgentName,
    string Title, string Description, string? Requester,
    Dictionary<string, object>? ExtraData,
    string Status, DateTime SubmittedAt, DateTime? PickedUpAt, DateTime? CompletedAt,
    string? ResultStatus, string? ResultMessage, string? ResultDetailsJson
);

public record PendingSubmissionResponse(
    string Id, string Title, string Description, string? Requester,
    Dictionary<string, object>? ExtraData,
    DateTime SubmittedAt
);
