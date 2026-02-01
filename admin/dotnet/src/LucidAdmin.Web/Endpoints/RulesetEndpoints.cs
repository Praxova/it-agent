using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

/// <summary>
/// API endpoints for managing rulesets and rules.
/// </summary>
public static class RulesetEndpoints
{
    public static void MapRulesetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rulesets")
            .WithTags("Rulesets")
            .RequireAuthorization();

        // ===== RULESET ENDPOINTS =====

        // GET /api/rulesets - List all rulesets
        group.MapGet("/", async (IRulesetRepository repository) =>
        {
            var rulesets = await repository.GetAllAsync();
            return Results.Ok(rulesets.Select(MapToResponse));
        });

        // GET /api/rulesets/category/{category} - Get rulesets by category
        group.MapGet("/category/{category}", async (string category, IRulesetRepository repository) =>
        {
            var rulesets = await repository.GetByCategoryAsync(category);
            return Results.Ok(rulesets.Select(MapToResponse));
        });

        // GET /api/rulesets/active - Get all active rulesets with their rules
        group.MapGet("/active", async (IRulesetRepository repository) =>
        {
            var rulesets = await repository.GetAllActiveWithRulesAsync();
            return Results.Ok(rulesets.Select(MapToResponseWithRules));
        });

        // GET /api/rulesets/{id} - Get single ruleset
        group.MapGet("/{id:guid}", async (Guid id, IRulesetRepository repository) =>
        {
            var ruleset = await repository.GetByIdAsync(id);
            if (ruleset == null)
            {
                throw new EntityNotFoundException("Ruleset", id);
            }
            return Results.Ok(MapToResponse(ruleset));
        });

        // GET /api/rulesets/{id}/with-rules - Get ruleset with all its rules
        group.MapGet("/{id:guid}/with-rules", async (Guid id, IRulesetRepository repository) =>
        {
            var ruleset = await repository.GetWithRulesAsync(id);
            if (ruleset == null)
            {
                throw new EntityNotFoundException("Ruleset", id);
            }
            return Results.Ok(MapToResponseWithRules(ruleset));
        });

        // POST /api/rulesets - Create ruleset
        group.MapPost("/", async (
            [FromBody] CreateRulesetRequest request,
            IRulesetRepository repository) =>
        {
            // Check if name already exists
            if (await repository.ExistsAsync(request.Name))
            {
                throw new DuplicateEntityException("Ruleset", request.Name);
            }

            var ruleset = new Ruleset
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Category = request.Category,
                IsBuiltIn = false, // Only created via seeding
                IsActive = request.IsActive
            };

            await repository.AddAsync(ruleset);

            return Results.Created($"/api/rulesets/{ruleset.Id}", MapToResponse(ruleset));
        });

        // PUT /api/rulesets/{id} - Update ruleset
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateRulesetRequest request,
            IRulesetRepository repository) =>
        {
            var ruleset = await repository.GetByIdAsync(id);
            if (ruleset == null)
            {
                throw new EntityNotFoundException("Ruleset", id);
            }

            // Prevent modification of built-in rulesets
            if (ruleset.IsBuiltIn)
            {
                return Results.BadRequest(new { error = "Cannot modify built-in rulesets" });
            }

            if (request.DisplayName != null) ruleset.DisplayName = request.DisplayName;
            if (request.Description != null) ruleset.Description = request.Description;
            if (request.Category != null) ruleset.Category = request.Category;
            if (request.IsActive.HasValue) ruleset.IsActive = request.IsActive.Value;

            await repository.UpdateAsync(ruleset);

            return Results.Ok(MapToResponse(ruleset));
        });

        // DELETE /api/rulesets/{id} - Delete ruleset
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IRulesetRepository repository) =>
        {
            var ruleset = await repository.GetByIdAsync(id);
            if (ruleset == null)
            {
                throw new EntityNotFoundException("Ruleset", id);
            }

            // Prevent deletion of built-in rulesets
            if (ruleset.IsBuiltIn)
            {
                return Results.BadRequest(new { error = "Cannot delete built-in rulesets" });
            }

            await repository.DeleteAsync(id);

            return Results.NoContent();
        });

        // ===== RULE ENDPOINTS =====

        var ruleGroup = app.MapGroup("/api/rules")
            .WithTags("Rules")
            .RequireAuthorization();

        // POST /api/rules - Create rule
        ruleGroup.MapPost("/", async (
            [FromBody] CreateRuleRequest request,
            IRulesetRepository rulesetRepository,
            LucidAdmin.Infrastructure.Data.LucidDbContext context) =>
        {
            // Verify ruleset exists
            var ruleset = await rulesetRepository.GetByIdAsync(request.RulesetId);
            if (ruleset == null)
            {
                throw new EntityNotFoundException("Ruleset", request.RulesetId);
            }

            // Prevent modification of built-in rulesets
            if (ruleset.IsBuiltIn)
            {
                return Results.BadRequest(new { error = "Cannot add rules to built-in rulesets" });
            }

            var rule = new Rule
            {
                RulesetId = request.RulesetId,
                Name = request.Name,
                RuleText = request.RuleText,
                Description = request.Description,
                Priority = request.Priority,
                IsActive = request.IsActive
            };

            context.Rules.Add(rule);
            await context.SaveChangesAsync();

            return Results.Created($"/api/rules/{rule.Id}", MapRuleToResponse(rule));
        });

        // PUT /api/rules/{id} - Update rule
        ruleGroup.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateRuleRequest request,
            IRulesetRepository rulesetRepository,
            LucidAdmin.Infrastructure.Data.LucidDbContext context) =>
        {
            var rule = await context.Rules.FindAsync(id);
            if (rule == null)
            {
                throw new EntityNotFoundException("Rule", id);
            }

            // Verify ruleset is not built-in
            var ruleset = await rulesetRepository.GetByIdAsync(rule.RulesetId);
            if (ruleset != null && ruleset.IsBuiltIn)
            {
                return Results.BadRequest(new { error = "Cannot modify rules in built-in rulesets" });
            }

            if (request.Name != null) rule.Name = request.Name;
            if (request.RuleText != null) rule.RuleText = request.RuleText;
            if (request.Description != null) rule.Description = request.Description;
            if (request.Priority.HasValue) rule.Priority = request.Priority.Value;
            if (request.IsActive.HasValue) rule.IsActive = request.IsActive.Value;

            context.Rules.Update(rule);
            await context.SaveChangesAsync();

            return Results.Ok(MapRuleToResponse(rule));
        });

        // DELETE /api/rules/{id} - Delete rule
        ruleGroup.MapDelete("/{id:guid}", async (
            Guid id,
            IRulesetRepository rulesetRepository,
            LucidAdmin.Infrastructure.Data.LucidDbContext context) =>
        {
            var rule = await context.Rules.FindAsync(id);
            if (rule == null)
            {
                throw new EntityNotFoundException("Rule", id);
            }

            // Verify ruleset is not built-in
            var ruleset = await rulesetRepository.GetByIdAsync(rule.RulesetId);
            if (ruleset != null && ruleset.IsBuiltIn)
            {
                return Results.BadRequest(new { error = "Cannot delete rules from built-in rulesets" });
            }

            context.Rules.Remove(rule);
            await context.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    private static RulesetResponse MapToResponse(Ruleset ruleset) => new(
        Id: ruleset.Id,
        Name: ruleset.Name,
        DisplayName: ruleset.DisplayName,
        Description: ruleset.Description,
        Category: ruleset.Category,
        IsBuiltIn: ruleset.IsBuiltIn,
        IsActive: ruleset.IsActive,
        Rules: null,
        CreatedAt: ruleset.CreatedAt,
        UpdatedAt: ruleset.UpdatedAt
    );

    private static RulesetResponse MapToResponseWithRules(Ruleset ruleset) => new(
        Id: ruleset.Id,
        Name: ruleset.Name,
        DisplayName: ruleset.DisplayName,
        Description: ruleset.Description,
        Category: ruleset.Category,
        IsBuiltIn: ruleset.IsBuiltIn,
        IsActive: ruleset.IsActive,
        Rules: ruleset.Rules?.Select(MapRuleToResponse).ToList() ?? new(),
        CreatedAt: ruleset.CreatedAt,
        UpdatedAt: ruleset.UpdatedAt
    );

    private static RuleResponse MapRuleToResponse(Rule rule) => new(
        Id: rule.Id,
        RulesetId: rule.RulesetId,
        Name: rule.Name,
        RuleText: rule.RuleText,
        Description: rule.Description,
        Priority: rule.Priority,
        IsActive: rule.IsActive,
        CreatedAt: rule.CreatedAt,
        UpdatedAt: rule.UpdatedAt
    );
}
