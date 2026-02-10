using LucidAdmin.Core.Entities;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Api.Models.Responses;
using LucidAdmin.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class TicketCategoryEndpoints
{
    public static void MapTicketCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ticket-categories")
            .WithTags("Ticket Categories")
            .RequireAuthorization();

        // List all categories
        group.MapGet("/", async (
            [FromQuery] bool? active,
            LucidDbContext db) =>
        {
            var query = db.TicketCategories.AsQueryable();

            if (active.HasValue)
                query = query.Where(c => c.IsActive == active.Value);

            var categories = await query
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.DisplayName)
                .Select(c => new TicketCategoryResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    Description = c.Description,
                    Color = c.Color,
                    IsBuiltIn = c.IsBuiltIn,
                    IsActive = c.IsActive,
                    SortOrder = c.SortOrder,
                    ExampleCount = c.Examples.Count + c.ExampleSets.Count
                })
                .ToListAsync();

            return Results.Ok(categories);
        });

        // Get single category
        group.MapGet("/{id:guid}", async (
            Guid id,
            LucidDbContext db) =>
        {
            var category = await db.TicketCategories
                .Where(c => c.Id == id)
                .Select(c => new TicketCategoryResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    Description = c.Description,
                    Color = c.Color,
                    IsBuiltIn = c.IsBuiltIn,
                    IsActive = c.IsActive,
                    SortOrder = c.SortOrder,
                    ExampleCount = c.Examples.Count + c.ExampleSets.Count
                })
                .FirstOrDefaultAsync();

            return category is not null
                ? Results.Ok(category)
                : Results.NotFound(new { error = "TicketCategoryNotFound" });
        });

        // Create category
        group.MapPost("/", async (
            CreateTicketCategoryRequest request,
            LucidDbContext db) =>
        {
            if (await db.TicketCategories.AnyAsync(c => c.Name == request.Name))
                return Results.Conflict(new { error = "CategoryExists", message = $"Category '{request.Name}' already exists" });

            var category = new TicketCategory
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Color = request.Color,
                IsBuiltIn = false,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder
            };

            db.TicketCategories.Add(category);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/ticket-categories/{category.Id}", new TicketCategoryResponse
            {
                Id = category.Id,
                Name = category.Name,
                DisplayName = category.DisplayName,
                Description = category.Description,
                Color = category.Color,
                IsBuiltIn = category.IsBuiltIn,
                IsActive = category.IsActive,
                SortOrder = category.SortOrder,
                ExampleCount = 0
            });
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Update category
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateTicketCategoryRequest request,
            LucidDbContext db) =>
        {
            var category = await db.TicketCategories.FindAsync(id);
            if (category is null)
                return Results.NotFound(new { error = "TicketCategoryNotFound" });

            // Cannot change Name on built-in categories
            if (category.IsBuiltIn && request.Name is not null && request.Name != category.Name)
                return Results.BadRequest(new { error = "CannotRenameBuiltIn", message = "Cannot change the name of a built-in category" });

            if (request.Name is not null) category.Name = request.Name;
            if (request.DisplayName is not null) category.DisplayName = request.DisplayName;
            if (request.Description is not null) category.Description = request.Description;
            if (request.Color is not null) category.Color = request.Color;
            if (request.IsActive.HasValue) category.IsActive = request.IsActive.Value;
            if (request.SortOrder.HasValue) category.SortOrder = request.SortOrder.Value;

            await db.SaveChangesAsync();

            var exampleCount = await db.Examples.CountAsync(e => e.TicketCategoryId == id)
                + await db.ExampleSets.CountAsync(e => e.TicketCategoryId == id);

            return Results.Ok(new TicketCategoryResponse
            {
                Id = category.Id,
                Name = category.Name,
                DisplayName = category.DisplayName,
                Description = category.Description,
                Color = category.Color,
                IsBuiltIn = category.IsBuiltIn,
                IsActive = category.IsActive,
                SortOrder = category.SortOrder,
                ExampleCount = exampleCount
            });
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Delete category
        group.MapDelete("/{id:guid}", async (
            Guid id,
            LucidDbContext db) =>
        {
            var category = await db.TicketCategories.FindAsync(id);
            if (category is null)
                return Results.NotFound(new { error = "TicketCategoryNotFound" });

            if (category.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotDeleteBuiltIn", message = "Built-in categories cannot be deleted" });

            var hasExamples = await db.Examples.AnyAsync(e => e.TicketCategoryId == id);
            var hasExampleSets = await db.ExampleSets.AnyAsync(e => e.TicketCategoryId == id);
            if (hasExamples || hasExampleSets)
                return Results.BadRequest(new { error = "CategoryInUse", message = "Cannot delete category that is referenced by examples or example sets" });

            db.TicketCategories.Remove(category);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.RequireAdmin);
    }
}

public record CreateTicketCategoryRequest(
    string Name,
    string? DisplayName,
    string? Description,
    string? Color,
    bool IsActive = true,
    int SortOrder = 0
);

public record UpdateTicketCategoryRequest(
    string? Name,
    string? DisplayName,
    string? Description,
    string? Color,
    bool? IsActive,
    int? SortOrder
);
