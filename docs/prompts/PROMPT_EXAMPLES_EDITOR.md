# Claude Code Prompt: Add Examples Editor to Admin Portal (Phase 2)

## Context

This is Phase 2 of the Workflow Designer feature set. We're adding ExampleSet and Example entities for managing few-shot training data used by the LLM classifier.

Phase 1 (Rules Editor) has been completed - use it as a reference for patterns.

See:
- `docs/adr/ADR-010-visual-workflow-designer.md` - Architecture decision
- `docs/WORKFLOW_DESIGNER_ENTITIES.md` - Full entity model documentation
- Existing Rules Editor implementation for patterns

## Overview

ExampleSets contain Examples that teach the LLM how to classify tickets. Each Example has:
- Input: A sample ticket (short description + description)
- Output: The expected classification result (JSON with ticket_type, confidence, affected_user, etc.)

This helps the classifier learn patterns like:
- "I forgot my password" → password_reset
- "Add John to the VPN group" → group_access_add
- "Remove Mary from Finance-ReadOnly" → group_access_remove

## Files to Create/Modify

### 1. Core Layer - Entities

**Create `/src/LucidAdmin.Core/Entities/ExampleSet.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A collection of few-shot examples for training the LLM classifier.
/// </summary>
public class ExampleSet : BaseEntity
{
    /// <summary>
    /// Unique name for the example set (e.g., "password-reset-examples").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Description of what ticket types this example set covers.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Primary ticket type this example set is designed for.
    /// </summary>
    public required TicketType TargetTicketType { get; set; }
    
    /// <summary>
    /// Whether this example set ships with the product.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;
    
    /// <summary>
    /// Whether this example set is active and available for use.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Examples in this set.
    /// </summary>
    public ICollection<Example> Examples { get; set; } = new List<Example>();
}
```

**Create `/src/LucidAdmin.Core/Entities/Example.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single few-shot example for classifier training.
/// </summary>
public class Example : BaseEntity
{
    /// <summary>
    /// Foreign key to parent ExampleSet.
    /// </summary>
    public Guid ExampleSetId { get; set; }
    
    /// <summary>
    /// Parent ExampleSet navigation property.
    /// </summary>
    public ExampleSet? ExampleSet { get; set; }
    
    /// <summary>
    /// Short name for this example (e.g., "simple-password-reset").
    /// </summary>
    public required string Name { get; set; }
    
    // === INPUT (the ticket) ===
    
    /// <summary>
    /// The ticket short description (what appears in the subject line).
    /// </summary>
    public required string TicketShortDescription { get; set; }
    
    /// <summary>
    /// The ticket full description/body.
    /// </summary>
    public string? TicketDescription { get; set; }
    
    /// <summary>
    /// Optional: The caller/requester name for context.
    /// </summary>
    public string? CallerName { get; set; }
    
    // === OUTPUT (expected classification) ===
    
    /// <summary>
    /// The expected ticket type classification.
    /// </summary>
    public required TicketType ExpectedTicketType { get; set; }
    
    /// <summary>
    /// Expected confidence level (0.0 to 1.0).
    /// </summary>
    public decimal ExpectedConfidence { get; set; } = 0.95m;
    
    /// <summary>
    /// Expected affected user (extracted from ticket).
    /// Null if the affected user is the caller themselves.
    /// </summary>
    public string? ExpectedAffectedUser { get; set; }
    
    /// <summary>
    /// Expected target group (for group access requests).
    /// </summary>
    public string? ExpectedTargetGroup { get; set; }
    
    /// <summary>
    /// Expected target resource/path (for file permission requests).
    /// </summary>
    public string? ExpectedTargetResource { get; set; }
    
    /// <summary>
    /// Expected permission level (for file permission requests).
    /// </summary>
    public string? ExpectedPermissionLevel { get; set; }
    
    /// <summary>
    /// Whether this example should result in escalation.
    /// </summary>
    public bool ExpectedShouldEscalate { get; set; } = false;
    
    /// <summary>
    /// Reason for escalation (if ExpectedShouldEscalate is true).
    /// </summary>
    public string? ExpectedEscalationReason { get; set; }
    
    // === METADATA ===
    
    /// <summary>
    /// Notes explaining why this example is useful for training.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Order within the example set (lower = first).
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    /// <summary>
    /// Whether this example is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
```

**Create `/src/LucidAdmin.Core/Enums/TicketType.cs`:**
```csharp
namespace LucidAdmin.Core.Enums;

/// <summary>
/// Types of tickets the agent can classify and handle.
/// </summary>
public enum TicketType
{
    /// <summary>User needs password reset or account unlock.</summary>
    PasswordReset,
    
    /// <summary>Add user to an AD group.</summary>
    GroupAccessAdd,
    
    /// <summary>Remove user from an AD group.</summary>
    GroupAccessRemove,
    
    /// <summary>Grant file/folder permissions.</summary>
    FilePermissionGrant,
    
    /// <summary>Revoke file/folder permissions.</summary>
    FilePermissionRevoke,
    
    /// <summary>Ticket type could not be determined.</summary>
    Unknown,
    
    /// <summary>Multiple request types in one ticket.</summary>
    MultipleRequests,
    
    /// <summary>Request is outside agent's capabilities.</summary>
    OutOfScope
}
```

### 2. Repository Interface

**Create `/src/LucidAdmin.Core/Interfaces/Repositories/IExampleSetRepository.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IExampleSetRepository : IRepository<ExampleSet>
{
    /// <summary>
    /// Get example set with all its examples loaded.
    /// </summary>
    Task<ExampleSet?> GetWithExamplesAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Get all example sets for a specific ticket type.
    /// </summary>
    Task<IEnumerable<ExampleSet>> GetByTicketTypeAsync(TicketType ticketType, CancellationToken ct = default);
    
    /// <summary>
    /// Get all active example sets with their examples for classifier training.
    /// </summary>
    Task<IEnumerable<ExampleSet>> GetAllActiveWithExamplesAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Check if an example set name already exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
```

### 3. EF Core Configuration

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/ExampleSetConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ExampleSetConfiguration : IEntityTypeConfiguration<ExampleSet>
{
    public void Configure(EntityTypeBuilder<ExampleSet> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.HasIndex(e => e.Name)
            .IsUnique();
            
        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);
            
        builder.Property(e => e.Description)
            .HasMaxLength(1000);
            
        builder.Property(e => e.TargetTicketType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
            
        builder.HasMany(e => e.Examples)
            .WithOne(ex => ex.ExampleSet)
            .HasForeignKey(ex => ex.ExampleSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/ExampleConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ExampleConfiguration : IEntityTypeConfiguration<Example>
{
    public void Configure(EntityTypeBuilder<Example> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.TicketShortDescription)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(e => e.TicketDescription)
            .HasMaxLength(4000);
            
        builder.Property(e => e.CallerName)
            .HasMaxLength(200);
            
        builder.Property(e => e.ExpectedTicketType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
            
        builder.Property(e => e.ExpectedConfidence)
            .HasPrecision(3, 2);
            
        builder.Property(e => e.ExpectedAffectedUser)
            .HasMaxLength(200);
            
        builder.Property(e => e.ExpectedTargetGroup)
            .HasMaxLength(200);
            
        builder.Property(e => e.ExpectedTargetResource)
            .HasMaxLength(500);
            
        builder.Property(e => e.ExpectedPermissionLevel)
            .HasMaxLength(50);
            
        builder.Property(e => e.ExpectedEscalationReason)
            .HasMaxLength(500);
            
        builder.Property(e => e.Notes)
            .HasMaxLength(1000);
            
        builder.HasIndex(e => new { e.ExampleSetId, e.Name })
            .IsUnique();
    }
}
```

**Update `/src/LucidAdmin.Infrastructure/Data/LucidDbContext.cs`:**
Add DbSets:
```csharp
public DbSet<ExampleSet> ExampleSets => Set<ExampleSet>();
public DbSet<Example> Examples => Set<Example>();
```

### 4. Repository Implementation

**Create `/src/LucidAdmin.Infrastructure/Repositories/ExampleSetRepository.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Infrastructure.Repositories;

public class ExampleSetRepository : RepositoryBase<ExampleSet>, IExampleSetRepository
{
    public ExampleSetRepository(LucidDbContext context) : base(context) { }
    
    public async Task<ExampleSet?> GetWithExamplesAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet
            .Include(e => e.Examples.OrderBy(ex => ex.SortOrder))
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }
    
    public async Task<IEnumerable<ExampleSet>> GetByTicketTypeAsync(TicketType ticketType, CancellationToken ct = default)
    {
        return await DbSet
            .Where(e => e.TargetTicketType == ticketType)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
    }
    
    public async Task<IEnumerable<ExampleSet>> GetAllActiveWithExamplesAsync(CancellationToken ct = default)
    {
        return await DbSet
            .Where(e => e.IsActive)
            .Include(e => e.Examples.Where(ex => ex.IsActive).OrderBy(ex => ex.SortOrder))
            .OrderBy(e => e.TargetTicketType)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);
    }
    
    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await DbSet.AnyAsync(e => e.Name == name, ct);
    }
}
```

**Update `/src/LucidAdmin.Infrastructure/DependencyInjection.cs`:**
Add registration:
```csharp
services.AddScoped<IExampleSetRepository, ExampleSetRepository>();
```

### 5. API Request/Response Models

**Create `/src/LucidAdmin.Web/Api/Models/Requests/ExampleSetRequests.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Models.Requests;

public record CreateExampleSetRequest(
    string Name,
    string? DisplayName,
    string? Description,
    TicketType TargetTicketType,
    bool IsActive = true
);

public record UpdateExampleSetRequest(
    string? DisplayName,
    string? Description,
    TicketType? TargetTicketType,
    bool? IsActive
);

public record CreateExampleRequest(
    string Name,
    string TicketShortDescription,
    string? TicketDescription,
    string? CallerName,
    TicketType ExpectedTicketType,
    decimal ExpectedConfidence = 0.95m,
    string? ExpectedAffectedUser = null,
    string? ExpectedTargetGroup = null,
    string? ExpectedTargetResource = null,
    string? ExpectedPermissionLevel = null,
    bool ExpectedShouldEscalate = false,
    string? ExpectedEscalationReason = null,
    string? Notes = null,
    bool IsActive = true
);

public record UpdateExampleRequest(
    string? Name,
    string? TicketShortDescription,
    string? TicketDescription,
    string? CallerName,
    TicketType? ExpectedTicketType,
    decimal? ExpectedConfidence,
    string? ExpectedAffectedUser,
    string? ExpectedTargetGroup,
    string? ExpectedTargetResource,
    string? ExpectedPermissionLevel,
    bool? ExpectedShouldEscalate,
    string? ExpectedEscalationReason,
    string? Notes,
    bool? IsActive
);

public record ReorderExamplesRequest(
    List<Guid> ExampleIds
);
```

**Create `/src/LucidAdmin.Web/Api/Models/Responses/ExampleSetResponses.cs`:**
```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Models.Responses;

public record ExampleSetResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    TicketType TargetTicketType,
    bool IsBuiltIn,
    bool IsActive,
    int ExampleCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ExampleSetDetailResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    TicketType TargetTicketType,
    bool IsBuiltIn,
    bool IsActive,
    List<ExampleResponse> Examples,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ExampleResponse(
    Guid Id,
    string Name,
    // Input
    string TicketShortDescription,
    string? TicketDescription,
    string? CallerName,
    // Output
    TicketType ExpectedTicketType,
    decimal ExpectedConfidence,
    string? ExpectedAffectedUser,
    string? ExpectedTargetGroup,
    string? ExpectedTargetResource,
    string? ExpectedPermissionLevel,
    bool ExpectedShouldEscalate,
    string? ExpectedEscalationReason,
    // Metadata
    string? Notes,
    int SortOrder,
    bool IsActive
);

/// <summary>
/// Response format used when exporting examples for LLM training prompts.
/// </summary>
public record ExampleTrainingFormat(
    string Input,
    string ExpectedOutput
);
```

### 6. API Endpoints

**Create `/src/LucidAdmin.Web/Api/Endpoints/ExampleSetEndpoints.cs`:**
```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;

namespace LucidAdmin.Web.Api.Endpoints;

public static class ExampleSetEndpoints
{
    public static void MapExampleSetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/example-sets")
            .WithTags("Example Sets");
        
        // List all example sets
        group.MapGet("/", async (
            [FromQuery] TicketType? ticketType,
            IExampleSetRepository repo) =>
        {
            var sets = ticketType.HasValue
                ? await repo.GetByTicketTypeAsync(ticketType.Value)
                : await repo.GetAllAsync();
                
            var response = sets.Select(e => new ExampleSetResponse(
                e.Id, e.Name, e.DisplayName, e.Description, e.TargetTicketType,
                e.IsBuiltIn, e.IsActive, e.Examples.Count,
                e.CreatedAt, e.UpdatedAt
            ));
            
            return Results.Ok(response);
        });
        
        // Get ticket types enum values
        group.MapGet("/ticket-types", () => 
        {
            var types = Enum.GetValues<TicketType>()
                .Select(t => new { Value = t, Name = t.ToString() });
            return Results.Ok(types);
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
                set.Id, set.Name, set.DisplayName, set.Description, set.TargetTicketType,
                set.IsBuiltIn, set.IsActive,
                set.Examples.Select(e => new ExampleResponse(
                    e.Id, e.Name,
                    e.TicketShortDescription, e.TicketDescription, e.CallerName,
                    e.ExpectedTicketType, e.ExpectedConfidence,
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
                TargetTicketType = request.TargetTicketType,
                IsActive = request.IsActive,
                IsBuiltIn = false
            };
            
            await repo.AddAsync(set);
            
            return Results.Created($"/api/v1/example-sets/{set.Id}", new ExampleSetResponse(
                set.Id, set.Name, set.DisplayName, set.Description, set.TargetTicketType,
                set.IsBuiltIn, set.IsActive, 0,
                set.CreatedAt, set.UpdatedAt
            ));
        });
        
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
            if (request.TargetTicketType.HasValue) set.TargetTicketType = request.TargetTicketType.Value;
            if (request.IsActive.HasValue) set.IsActive = request.IsActive.Value;
            
            await repo.UpdateAsync(set);
            
            return Results.Ok(new ExampleSetResponse(
                set.Id, set.Name, set.DisplayName, set.Description, set.TargetTicketType,
                set.IsBuiltIn, set.IsActive, set.Examples.Count,
                set.CreatedAt, set.UpdatedAt
            ));
        });
        
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
        });
        
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
                TargetTicketType = source.TargetTicketType,
                IsActive = true,
                IsBuiltIn = false,
                Examples = source.Examples.Select(e => new Example
                {
                    Name = e.Name,
                    TicketShortDescription = e.TicketShortDescription,
                    TicketDescription = e.TicketDescription,
                    CallerName = e.CallerName,
                    ExpectedTicketType = e.ExpectedTicketType,
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
                copy.Id, copy.Name, copy.DisplayName, copy.Description, copy.TargetTicketType,
                copy.IsBuiltIn, copy.IsActive, copy.Examples.Count,
                copy.CreatedAt, copy.UpdatedAt
            ));
        });
        
        // === Example endpoints (nested under example set) ===
        
        // Add example to set
        group.MapPost("/{setId:guid}/examples", async (
            Guid setId,
            CreateExampleRequest request,
            IExampleSetRepository repo) =>
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
                ExpectedTicketType = request.ExpectedTicketType,
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
            
            set.Examples.Add(example);
            await repo.UpdateAsync(set);
            
            return Results.Created(
                $"/api/v1/example-sets/{setId}/examples/{example.Id}",
                MapToResponse(example)
            );
        });
        
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
            if (request.ExpectedTicketType.HasValue) example.ExpectedTicketType = request.ExpectedTicketType.Value;
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
        });
        
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
        });
        
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
        });
    }
    
    private static ExampleResponse MapToResponse(Example e) => new(
        e.Id, e.Name,
        e.TicketShortDescription, e.TicketDescription, e.CallerName,
        e.ExpectedTicketType, e.ExpectedConfidence,
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
            ["ticket_type"] = e.ExpectedTicketType.ToString().ToLowerInvariant(),
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
```

**Update `/src/LucidAdmin.Web/Program.cs`:**
Add endpoint registration:
```csharp
app.MapExampleSetEndpoints();
```

### 7. Blazor UI Pages

**Create `/src/LucidAdmin.Web/Components/Pages/Examples/Index.razor`:**
Create a list page similar to Rulesets/Index.razor with:
- Filter by TicketType dropdown
- Table showing: Name, Target Type, Examples count, Status, Actions
- Built-in badge for IsBuiltIn sets
- Actions: View/Edit, Copy, Delete (disabled for built-in)
- "New Example Set" button

**Create `/src/LucidAdmin.Web/Components/Pages/Examples/Edit.razor`:**
Create an edit page similar to Rulesets/Edit.razor with:

Left panel (Example Set details):
- Name (read-only after creation)
- Display Name
- Description (textarea)
- Target Ticket Type (dropdown)
- Active toggle
- Save/Cancel buttons

Right panel (Examples list):
- List of examples with expandable cards showing:
  - Name and status badge
  - "Input" section: Short Description, Description, Caller
  - "Expected Output" section: Ticket Type, Confidence, extracted fields
  - Notes (if any)
- Add Example button opens dialog
- Edit/Delete buttons per example
- Drag-and-drop reorder (using MudDropZone if possible)

Example Dialog form:
- Name
- Input section:
  - Ticket Short Description (required)
  - Ticket Description (textarea)
  - Caller Name
- Expected Output section:
  - Ticket Type (dropdown, required)
  - Confidence (slider 0-1, default 0.95)
  - Affected User (text, optional)
  - Target Group (text, optional) - show if GroupAccessAdd/Remove
  - Target Resource (text, optional) - show if FilePermission*
  - Permission Level (dropdown, optional) - show if FilePermission*
  - Should Escalate (toggle)
  - Escalation Reason (text, show if Should Escalate)
- Notes (textarea)
- Active toggle

**Add to `/src/LucidAdmin.Web/Components/Layout/NavMenu.razor`:**
```razor
<MudNavLink Href="/examples" Icon="@Icons.Material.Filled.School">
    Examples
</MudNavLink>
```

### 8. Create Migration

After all files are created:
```bash
cd admin/dotnet
dotnet ef migrations add AddExampleSetsAndExamples \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
dotnet ef database update \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
```

### 9. Seed Data for Built-in Example Sets

Add seed data after migration (in Program.cs or a seeder class):

```csharp
// Seed built-in example sets
if (!db.ExampleSets.Any(e => e.IsBuiltIn))
{
    var passwordResetExamples = new ExampleSet
    {
        Name = "password-reset-examples",
        DisplayName = "Password Reset Examples",
        Description = "Examples for classifying password reset and account unlock requests",
        TargetTicketType = TicketType.PasswordReset,
        IsBuiltIn = true,
        IsActive = true,
        Examples = new List<Example>
        {
            new Example
            {
                Name = "simple-forgot-password",
                TicketShortDescription = "I forgot my password",
                TicketDescription = "I can't remember my password and need to log in.",
                ExpectedTicketType = TicketType.PasswordReset,
                ExpectedConfidence = 0.95m,
                Notes = "Simple, clear password reset request",
                SortOrder = 0,
                IsActive = true
            },
            new Example
            {
                Name = "account-locked",
                TicketShortDescription = "My account is locked out",
                TicketDescription = "I tried logging in too many times and now my account is locked. Please help.",
                ExpectedTicketType = TicketType.PasswordReset,
                ExpectedConfidence = 0.90m,
                Notes = "Account lockout - also handled by password reset flow",
                SortOrder = 1,
                IsActive = true
            },
            new Example
            {
                Name = "coworker-password-reset",
                TicketShortDescription = "Password reset for John Smith",
                TicketDescription = "John Smith (jsmith) forgot his password while on vacation. Can you reset it so I can give him the temp password?",
                CallerName = "Jane Doe",
                ExpectedTicketType = TicketType.PasswordReset,
                ExpectedConfidence = 0.85m,
                ExpectedAffectedUser = "jsmith",
                Notes = "Third-party request - note lower confidence, affected_user extracted",
                SortOrder = 2,
                IsActive = true
            },
            new Example
            {
                Name = "ambiguous-login-issue",
                TicketShortDescription = "Can't log in",
                TicketDescription = "I'm having trouble logging in to my computer.",
                ExpectedTicketType = TicketType.PasswordReset,
                ExpectedConfidence = 0.60m,
                ExpectedShouldEscalate = true,
                ExpectedEscalationReason = "Ambiguous request - could be password, network, or hardware issue",
                Notes = "Low confidence example - should escalate for clarification",
                SortOrder = 3,
                IsActive = true
            }
        }
    };
    
    var groupAccessExamples = new ExampleSet
    {
        Name = "group-access-examples",
        DisplayName = "Group Access Examples",
        Description = "Examples for classifying AD group membership requests",
        TargetTicketType = TicketType.GroupAccessAdd,
        IsBuiltIn = true,
        IsActive = true,
        Examples = new List<Example>
        {
            new Example
            {
                Name = "add-to-vpn-group",
                TicketShortDescription = "Need VPN access",
                TicketDescription = "I'm starting to work remotely and need to be added to the VPN users group.",
                ExpectedTicketType = TicketType.GroupAccessAdd,
                ExpectedConfidence = 0.90m,
                ExpectedTargetGroup = "VPN-Users",
                Notes = "Clear add request with identifiable group",
                SortOrder = 0,
                IsActive = true
            },
            new Example
            {
                Name = "add-user-to-team-share",
                TicketShortDescription = "Add Sarah to Marketing share",
                TicketDescription = "Please add Sarah Johnson (sjohnson) to the Marketing-ReadWrite group so she can access the team files.",
                CallerName = "Mike Manager",
                ExpectedTicketType = TicketType.GroupAccessAdd,
                ExpectedConfidence = 0.95m,
                ExpectedAffectedUser = "sjohnson",
                ExpectedTargetGroup = "Marketing-ReadWrite",
                Notes = "Manager requesting access for team member - clear and complete",
                SortOrder = 1,
                IsActive = true
            },
            new Example
            {
                Name = "remove-from-group",
                TicketShortDescription = "Remove access for terminated employee",
                TicketDescription = "Bob Wilson (bwilson) has left the company. Please remove him from all groups.",
                ExpectedTicketType = TicketType.GroupAccessRemove,
                ExpectedConfidence = 0.85m,
                ExpectedAffectedUser = "bwilson",
                ExpectedShouldEscalate = true,
                ExpectedEscalationReason = "Request to remove from ALL groups requires human review",
                Notes = "Bulk removal should be escalated for verification",
                SortOrder = 2,
                IsActive = true
            },
            new Example
            {
                Name = "remove-single-group",
                TicketShortDescription = "Remove me from Finance-Readonly",
                TicketDescription = "I transferred to a different department and no longer need access to the Finance folder.",
                ExpectedTicketType = TicketType.GroupAccessRemove,
                ExpectedConfidence = 0.95m,
                ExpectedTargetGroup = "Finance-Readonly",
                Notes = "Self-service removal from specific group",
                SortOrder = 3,
                IsActive = true
            }
        }
    };
    
    db.ExampleSets.AddRange(passwordResetExamples, groupAccessExamples);
    await db.SaveChangesAsync();
}
```

## Testing

After implementation:
1. Navigate to http://localhost:5000/examples
2. Verify built-in example sets appear with correct examples
3. Create a new example set with custom examples
4. Test the training format export endpoint
5. Copy a built-in set and verify it's editable
6. Verify built-in sets cannot be modified or deleted

## Summary

This prompt creates:
- Core entities: ExampleSet, Example
- TicketType enum for classification types
- Repository with query methods
- Full CRUD API endpoints including training format export
- Blazor UI for managing example sets and examples
- Built-in Password Reset and Group Access example sets as seed data

The training format endpoint (`GET /api/v1/example-sets/{id}/training-format`) outputs examples in a format ready for inclusion in LLM prompts.
