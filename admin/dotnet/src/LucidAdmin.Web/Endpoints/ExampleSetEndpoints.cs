using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;
using LucidAdmin.Web.Authorization;

namespace LucidAdmin.Web.Endpoints;

public static class ExampleSetEndpoints
{
    public static void MapExampleSetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/example-sets")
            .WithTags("Example Sets")
            .RequireAuthorization();

        // List all example sets
        group.MapGet("/", async (
            [FromQuery] Guid? categoryId,
            IExampleSetRepository repo) =>
        {
            var sets = categoryId.HasValue
                ? await repo.GetByCategoryIdAsync(categoryId.Value)
                : await repo.GetAllAsync();

            var response = sets.Select(e => new ExampleSetResponse(
                e.Id, e.Name, e.DisplayName, e.Description, e.TicketCategoryId, e.TicketCategory?.Name,
                e.IsBuiltIn, e.IsActive, e.Examples.Count,
                e.CreatedAt, e.UpdatedAt
            ));

            return Results.Ok(response);
        });

        // Get single example set with examples
        group.MapGet("/{id:guid}", async (
            Guid id,
            IExampleSetRepository repo) =>
        {
            var set = await repo.GetWithExamplesAsync(id);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            var response = new ExampleSetDetailResponse(
                set.Id, set.Name, set.DisplayName, set.Description, set.TicketCategoryId, set.TicketCategory?.Name,
                set.IsBuiltIn, set.IsActive,
                set.Examples.Select(e => new ExampleResponse(
                    e.Id, e.Name,
                    e.TicketShortDescription, e.TicketDescription, e.CallerName,
                    e.TicketCategoryId, e.TicketCategory?.Name, e.ExpectedConfidence,
                    e.ExpectedAffectedUser, e.ExpectedTargetGroup,
                    e.ExpectedTargetResource, e.ExpectedPermissionLevel,
                    e.ExpectedShouldEscalate, e.ExpectedEscalationReason,
                    e.Notes, e.SortOrder, e.IsActive
                )).ToList(),
                set.CreatedAt, set.UpdatedAt
            );

            return Results.Ok(response);
        });

        // Get examples in training format (for LLM prompts)
        group.MapGet("/{id:guid}/training-format", async (
            Guid id,
            IExampleSetRepository repo) =>
        {
            var set = await repo.GetWithExamplesAsync(id);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            var trainingExamples = set.Examples
                .Where(e => e.IsActive)
                .OrderBy(e => e.SortOrder)
                .Select(e => new ExampleTrainingFormat(
                    Input: FormatExampleInput(e),
                    ExpectedOutput: FormatExampleOutput(e)
                ));

            return Results.Ok(trainingExamples);
        });

        // Create example set
        group.MapPost("/", async (
            CreateExampleSetRequest request,
            IExampleSetRepository repo) =>
        {
            if (await repo.ExistsAsync(request.Name))
                return Results.Conflict(new { error = "ExampleSetExists", message = $"Example set '{request.Name}' already exists" });

            var set = new ExampleSet
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                TicketCategoryId = request.TicketCategoryId,
                IsActive = request.IsActive,
                IsBuiltIn = false
            };

            await repo.AddAsync(set);

            return Results.Created($"/api/v1/example-sets/{set.Id}", new ExampleSetResponse(
                set.Id, set.Name, set.DisplayName, set.Description, set.TicketCategoryId, set.TicketCategory?.Name,
                set.IsBuiltIn, set.IsActive, 0,
                set.CreatedAt, set.UpdatedAt
            ));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Update example set
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateExampleSetRequest request,
            IExampleSetRepository repo) =>
        {
            var set = await repo.GetByIdAsync(id);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (set.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn", message = "Built-in example sets cannot be modified. Copy it to create a custom version." });

            if (request.DisplayName is not null) set.DisplayName = request.DisplayName;
            if (request.Description is not null) set.Description = request.Description;
            if (request.TicketCategoryId.HasValue) set.TicketCategoryId = request.TicketCategoryId.Value;
            if (request.IsActive.HasValue) set.IsActive = request.IsActive.Value;

            await repo.UpdateAsync(set);

            return Results.Ok(new ExampleSetResponse(
                set.Id, set.Name, set.DisplayName, set.Description, set.TicketCategoryId, set.TicketCategory?.Name,
                set.IsBuiltIn, set.IsActive, set.Examples.Count,
                set.CreatedAt, set.UpdatedAt
            ));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Delete example set
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IExampleSetRepository repo) =>
        {
            var set = await repo.GetByIdAsync(id);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (set.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotDeleteBuiltIn", message = "Built-in example sets cannot be deleted." });

            await repo.DeleteAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Copy example set
        group.MapPost("/{id:guid}/copy", async (
            Guid id,
            [FromQuery] string newName,
            IExampleSetRepository repo) =>
        {
            var source = await repo.GetWithExamplesAsync(id);
            if (source is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (await repo.ExistsAsync(newName))
                return Results.Conflict(new { error = "ExampleSetExists", message = $"Example set '{newName}' already exists" });

            var copy = new ExampleSet
            {
                Name = newName,
                DisplayName = $"{source.DisplayName} (Copy)",
                Description = source.Description,
                TicketCategoryId = source.TicketCategoryId,
                IsActive = true,
                IsBuiltIn = false,
                Examples = source.Examples.Select(e => new Example
                {
                    Name = e.Name,
                    TicketShortDescription = e.TicketShortDescription,
                    TicketDescription = e.TicketDescription,
                    CallerName = e.CallerName,
                    TicketCategoryId = e.TicketCategoryId,
                    ExpectedConfidence = e.ExpectedConfidence,
                    ExpectedAffectedUser = e.ExpectedAffectedUser,
                    ExpectedTargetGroup = e.ExpectedTargetGroup,
                    ExpectedTargetResource = e.ExpectedTargetResource,
                    ExpectedPermissionLevel = e.ExpectedPermissionLevel,
                    ExpectedShouldEscalate = e.ExpectedShouldEscalate,
                    ExpectedEscalationReason = e.ExpectedEscalationReason,
                    Notes = e.Notes,
                    SortOrder = e.SortOrder,
                    IsActive = e.IsActive
                }).ToList()
            };

            await repo.AddAsync(copy);

            return Results.Created($"/api/v1/example-sets/{copy.Id}", new ExampleSetResponse(
                copy.Id, copy.Name, copy.DisplayName, copy.Description, copy.TicketCategoryId, copy.TicketCategory?.Name,
                copy.IsBuiltIn, copy.IsActive, copy.Examples.Count,
                copy.CreatedAt, copy.UpdatedAt
            ));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // === Example endpoints (nested under example set) ===

        // Add example to set
        group.MapPost("/{setId:guid}/examples", async (
            Guid setId,
            CreateExampleRequest request,
            IExampleSetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var set = await repo.GetWithExamplesAsync(setId);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (set.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });

            if (set.Examples.Any(e => e.Name == request.Name))
                return Results.Conflict(new { error = "ExampleExists", message = $"Example '{request.Name}' already exists in this set" });

            var maxOrder = set.Examples.Any() ? set.Examples.Max(e => e.SortOrder) : -1;

            var example = new Example
            {
                ExampleSetId = setId,
                Name = request.Name,
                TicketShortDescription = request.TicketShortDescription,
                TicketDescription = request.TicketDescription,
                CallerName = request.CallerName,
                TicketCategoryId = request.TicketCategoryId,
                ExpectedConfidence = request.ExpectedConfidence,
                ExpectedAffectedUser = request.ExpectedAffectedUser,
                ExpectedTargetGroup = request.ExpectedTargetGroup,
                ExpectedTargetResource = request.ExpectedTargetResource,
                ExpectedPermissionLevel = request.ExpectedPermissionLevel,
                ExpectedShouldEscalate = request.ExpectedShouldEscalate,
                ExpectedEscalationReason = request.ExpectedEscalationReason,
                Notes = request.Notes,
                SortOrder = maxOrder + 1,
                IsActive = request.IsActive
            };

            db.Examples.Add(example);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/v1/example-sets/{setId}/examples/{example.Id}",
                MapToResponse(example)
            );
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Update example
        group.MapPut("/{setId:guid}/examples/{exampleId:guid}", async (
            Guid setId,
            Guid exampleId,
            UpdateExampleRequest request,
            IExampleSetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var set = await repo.GetByIdAsync(setId);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (set.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });

            var example = await db.Examples.FindAsync(exampleId);
            if (example is null || example.ExampleSetId != setId)
                return Results.NotFound(new { error = "ExampleNotFound" });

            // Update fields if provided
            if (request.Name is not null) example.Name = request.Name;
            if (request.TicketShortDescription is not null) example.TicketShortDescription = request.TicketShortDescription;
            if (request.TicketDescription is not null) example.TicketDescription = request.TicketDescription;
            if (request.CallerName is not null) example.CallerName = request.CallerName;
            if (request.TicketCategoryId.HasValue) example.TicketCategoryId = request.TicketCategoryId.Value;
            if (request.ExpectedConfidence.HasValue) example.ExpectedConfidence = request.ExpectedConfidence.Value;
            if (request.ExpectedAffectedUser is not null) example.ExpectedAffectedUser = request.ExpectedAffectedUser;
            if (request.ExpectedTargetGroup is not null) example.ExpectedTargetGroup = request.ExpectedTargetGroup;
            if (request.ExpectedTargetResource is not null) example.ExpectedTargetResource = request.ExpectedTargetResource;
            if (request.ExpectedPermissionLevel is not null) example.ExpectedPermissionLevel = request.ExpectedPermissionLevel;
            if (request.ExpectedShouldEscalate.HasValue) example.ExpectedShouldEscalate = request.ExpectedShouldEscalate.Value;
            if (request.ExpectedEscalationReason is not null) example.ExpectedEscalationReason = request.ExpectedEscalationReason;
            if (request.Notes is not null) example.Notes = request.Notes;
            if (request.IsActive.HasValue) example.IsActive = request.IsActive.Value;

            await db.SaveChangesAsync();

            return Results.Ok(MapToResponse(example));
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Delete example
        group.MapDelete("/{setId:guid}/examples/{exampleId:guid}", async (
            Guid setId,
            Guid exampleId,
            IExampleSetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var set = await repo.GetByIdAsync(setId);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (set.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });

            var example = await db.Examples.FindAsync(exampleId);
            if (example is null || example.ExampleSetId != setId)
                return Results.NotFound(new { error = "ExampleNotFound" });

            db.Examples.Remove(example);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Reorder examples
        group.MapPost("/{setId:guid}/examples/reorder", async (
            Guid setId,
            ReorderExamplesRequest request,
            IExampleSetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var set = await repo.GetWithExamplesAsync(setId);
            if (set is null)
                return Results.NotFound(new { error = "ExampleSetNotFound" });

            if (set.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });

            for (int i = 0; i < request.ExampleIds.Count; i++)
            {
                var example = set.Examples.FirstOrDefault(e => e.Id == request.ExampleIds[i]);
                if (example != null)
                {
                    example.SortOrder = i;
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok(new { message = "Examples reordered successfully" });
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);
    }

    private static ExampleResponse MapToResponse(Example e) => new(
        e.Id, e.Name,
        e.TicketShortDescription, e.TicketDescription, e.CallerName,
        e.TicketCategoryId, e.TicketCategory?.Name, e.ExpectedConfidence,
        e.ExpectedAffectedUser, e.ExpectedTargetGroup,
        e.ExpectedTargetResource, e.ExpectedPermissionLevel,
        e.ExpectedShouldEscalate, e.ExpectedEscalationReason,
        e.Notes, e.SortOrder, e.IsActive
    );

    private static string FormatExampleInput(Example e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(e.CallerName))
            parts.Add($"Caller: {e.CallerName}");
        parts.Add($"Short Description: {e.TicketShortDescription}");
        if (!string.IsNullOrEmpty(e.TicketDescription))
            parts.Add($"Description: {e.TicketDescription}");
        return string.Join("\n", parts);
    }

    private static string FormatExampleOutput(Example e)
    {
        var output = new Dictionary<string, object>
        {
            ["ticket_type"] = e.TicketCategory?.Name ?? "unknown",
            ["confidence"] = e.ExpectedConfidence
        };

        if (!string.IsNullOrEmpty(e.ExpectedAffectedUser))
            output["affected_user"] = e.ExpectedAffectedUser;
        if (!string.IsNullOrEmpty(e.ExpectedTargetGroup))
            output["target_group"] = e.ExpectedTargetGroup;
        if (!string.IsNullOrEmpty(e.ExpectedTargetResource))
            output["target_resource"] = e.ExpectedTargetResource;
        if (!string.IsNullOrEmpty(e.ExpectedPermissionLevel))
            output["permission_level"] = e.ExpectedPermissionLevel;
        if (e.ExpectedShouldEscalate)
        {
            output["should_escalate"] = true;
            if (!string.IsNullOrEmpty(e.ExpectedEscalationReason))
                output["escalation_reason"] = e.ExpectedEscalationReason;
        }

        return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = false });
    }
}
