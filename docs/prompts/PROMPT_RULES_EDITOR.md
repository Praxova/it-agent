# Claude Code Prompt: Add Rules Editor to Admin Portal (Phase 1)

## Context

We're building a visual workflow designer for the Lucid IT Agent. This is Phase 1: the Rules Editor.
Rules define behavioral constraints for the agent (security policies, escalation triggers, etc.).

See:
- `docs/adr/ADR-010-visual-workflow-designer.md` - Architecture decision
- `docs/WORKFLOW_DESIGNER_ENTITIES.md` - Full entity model documentation

## Overview

Add Ruleset and Rule entities to the Admin Portal with full CRUD UI. This is pure Blazor with MudBlazor - no JavaScript needed.

## Files to Create/Modify

### 1. Core Layer - Entities

Create these files in `admin/dotnet/src/LucidAdmin.Core/`:

**Enums/RulesetCategory.cs:**
```csharp
namespace LucidAdmin.Core.Enums;

public enum RulesetCategory
{
    Security,
    Escalation,
    Validation,
    Communication,
    General
}
```

**Entities/Ruleset.cs:**
```csharp
namespace LucidAdmin.Core.Entities;

public class Ruleset : BaseEntity
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public required RulesetCategory Category { get; set; }
    public bool IsBuiltIn { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 100;
    
    // Navigation
    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
}
```

**Entities/Rule.cs:**
```csharp
namespace LucidAdmin.Core.Entities;

public class Rule : BaseEntity
{
    public Guid RulesetId { get; set; }
    public required string Name { get; set; }
    public required string RuleText { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string? AppliesTo { get; set; }  // null = all steps
    
    // Navigation
    public Ruleset? Ruleset { get; set; }
}
```

**Interfaces/Repositories/IRulesetRepository.cs:**
```csharp
namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IRulesetRepository : IRepository<Ruleset>
{
    Task<IEnumerable<Ruleset>> GetByCategoryAsync(RulesetCategory category, CancellationToken ct = default);
    Task<IEnumerable<Ruleset>> GetActiveAsync(CancellationToken ct = default);
    Task<Ruleset?> GetWithRulesAsync(Guid id, CancellationToken ct = default);
}
```

**Interfaces/Repositories/IRuleRepository.cs:**
```csharp
namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IRuleRepository : IRepository<Rule>
{
    Task<IEnumerable<Rule>> GetByRulesetAsync(Guid rulesetId, CancellationToken ct = default);
    Task ReorderAsync(Guid rulesetId, IEnumerable<Guid> ruleIds, CancellationToken ct = default);
}
```

### 2. Infrastructure Layer

**Data/Configurations/RulesetConfiguration.cs:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class RulesetConfiguration : IEntityTypeConfiguration<Ruleset>
{
    public void Configure(EntityTypeBuilder<Ruleset> builder)
    {
        builder.ToTable("Rulesets");
        
        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.HasIndex(r => r.Name)
            .IsUnique();
            
        builder.Property(r => r.DisplayName)
            .HasMaxLength(200);
            
        builder.Property(r => r.Description)
            .HasMaxLength(1000);
            
        builder.Property(r => r.Category)
            .IsRequired()
            .HasConversion<string>();
            
        builder.HasMany(r => r.Rules)
            .WithOne(r => r.Ruleset)
            .HasForeignKey(r => r.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Data/Configurations/RuleConfiguration.cs:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("Rules");
        
        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(r => r.RuleText)
            .IsRequired()
            .HasMaxLength(2000);
            
        builder.Property(r => r.Description)
            .HasMaxLength(500);
            
        builder.Property(r => r.AppliesTo)
            .HasMaxLength(200);
            
        builder.HasIndex(r => new { r.RulesetId, r.Name })
            .IsUnique();
    }
}
```

**Update LucidDbContext.cs** - Add DbSets:
```csharp
public DbSet<Ruleset> Rulesets => Set<Ruleset>();
public DbSet<Rule> Rules => Set<Rule>();
```

**Repositories/RulesetRepository.cs:**
```csharp
namespace LucidAdmin.Infrastructure.Repositories;

public class RulesetRepository : RepositoryBase<Ruleset>, IRulesetRepository
{
    public RulesetRepository(LucidDbContext context) : base(context) { }
    
    public async Task<IEnumerable<Ruleset>> GetByCategoryAsync(RulesetCategory category, CancellationToken ct = default)
    {
        return await DbSet
            .Where(r => r.Category == category)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);
    }
    
    public async Task<IEnumerable<Ruleset>> GetActiveAsync(CancellationToken ct = default)
    {
        return await DbSet
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);
    }
    
    public async Task<Ruleset?> GetWithRulesAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet
            .Include(r => r.Rules.OrderBy(rule => rule.SortOrder))
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }
}
```

**Repositories/RuleRepository.cs:**
```csharp
namespace LucidAdmin.Infrastructure.Repositories;

public class RuleRepository : RepositoryBase<Rule>, IRuleRepository
{
    public RuleRepository(LucidDbContext context) : base(context) { }
    
    public async Task<IEnumerable<Rule>> GetByRulesetAsync(Guid rulesetId, CancellationToken ct = default)
    {
        return await DbSet
            .Where(r => r.RulesetId == rulesetId)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);
    }
    
    public async Task ReorderAsync(Guid rulesetId, IEnumerable<Guid> ruleIds, CancellationToken ct = default)
    {
        var rules = await DbSet
            .Where(r => r.RulesetId == rulesetId)
            .ToListAsync(ct);
            
        var order = 0;
        foreach (var ruleId in ruleIds)
        {
            var rule = rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.SortOrder = order++;
            }
        }
        
        await Context.SaveChangesAsync(ct);
    }
}
```

**Update DependencyInjection.cs** - Register repositories:
```csharp
services.AddScoped<IRulesetRepository, RulesetRepository>();
services.AddScoped<IRuleRepository, RuleRepository>();
```

### 3. Web Layer - API Endpoints

**Api/Endpoints/RulesetEndpoints.cs:**
```csharp
namespace LucidAdmin.Web.Api.Endpoints;

public static class RulesetEndpoints
{
    public static void MapRulesetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rulesets")
            .WithTags("Rulesets");
        
        // List all rulesets
        group.MapGet("/", async (
            IRulesetRepository repo,
            [FromQuery] RulesetCategory? category = null) =>
        {
            var rulesets = category.HasValue 
                ? await repo.GetByCategoryAsync(category.Value)
                : await repo.GetAllAsync();
            return Results.Ok(rulesets);
        });
        
        // Get single ruleset with rules
        group.MapGet("/{id:guid}", async (Guid id, IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetWithRulesAsync(id);
            return ruleset is null ? Results.NotFound() : Results.Ok(ruleset);
        });
        
        // Create ruleset
        group.MapPost("/", async (CreateRulesetRequest request, IRulesetRepository repo) =>
        {
            var ruleset = new Ruleset
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Category = request.Category,
                Priority = request.Priority ?? 100,
                IsActive = request.IsActive ?? true
            };
            
            await repo.AddAsync(ruleset);
            return Results.Created($"/api/v1/rulesets/{ruleset.Id}", ruleset);
        });
        
        // Update ruleset
        group.MapPut("/{id:guid}", async (Guid id, UpdateRulesetRequest request, IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetByIdAsync(id);
            if (ruleset is null) return Results.NotFound();
            if (ruleset.IsBuiltIn) return Results.BadRequest("Cannot modify built-in ruleset");
            
            ruleset.Name = request.Name ?? ruleset.Name;
            ruleset.DisplayName = request.DisplayName ?? ruleset.DisplayName;
            ruleset.Description = request.Description ?? ruleset.Description;
            ruleset.Category = request.Category ?? ruleset.Category;
            ruleset.Priority = request.Priority ?? ruleset.Priority;
            ruleset.IsActive = request.IsActive ?? ruleset.IsActive;
            
            await repo.UpdateAsync(ruleset);
            return Results.Ok(ruleset);
        });
        
        // Delete ruleset
        group.MapDelete("/{id:guid}", async (Guid id, IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetByIdAsync(id);
            if (ruleset is null) return Results.NotFound();
            if (ruleset.IsBuiltIn) return Results.BadRequest("Cannot delete built-in ruleset");
            
            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
        
        // Clone ruleset
        group.MapPost("/{id:guid}/clone", async (Guid id, CloneRulesetRequest request, IRulesetRepository repo, IRuleRepository ruleRepo) =>
        {
            var source = await repo.GetWithRulesAsync(id);
            if (source is null) return Results.NotFound();
            
            var clone = new Ruleset
            {
                Name = request.NewName,
                DisplayName = request.NewDisplayName ?? $"Copy of {source.DisplayName}",
                Description = source.Description,
                Category = source.Category,
                Priority = source.Priority,
                IsBuiltIn = false,
                IsActive = true
            };
            
            await repo.AddAsync(clone);
            
            // Clone rules
            foreach (var rule in source.Rules)
            {
                var clonedRule = new Rule
                {
                    RulesetId = clone.Id,
                    Name = rule.Name,
                    RuleText = rule.RuleText,
                    Description = rule.Description,
                    SortOrder = rule.SortOrder,
                    IsActive = rule.IsActive,
                    AppliesTo = rule.AppliesTo
                };
                await ruleRepo.AddAsync(clonedRule);
            }
            
            return Results.Created($"/api/v1/rulesets/{clone.Id}", clone);
        });
    }
}
```

**Api/Endpoints/RuleEndpoints.cs:**
```csharp
namespace LucidAdmin.Web.Api.Endpoints;

public static class RuleEndpoints
{
    public static void MapRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rulesets/{rulesetId:guid}/rules")
            .WithTags("Rules");
        
        // List rules in ruleset
        group.MapGet("/", async (Guid rulesetId, IRuleRepository repo) =>
        {
            var rules = await repo.GetByRulesetAsync(rulesetId);
            return Results.Ok(rules);
        });
        
        // Get single rule
        group.MapGet("/{ruleId:guid}", async (Guid rulesetId, Guid ruleId, IRuleRepository repo) =>
        {
            var rule = await repo.GetByIdAsync(ruleId);
            if (rule is null || rule.RulesetId != rulesetId) return Results.NotFound();
            return Results.Ok(rule);
        });
        
        // Create rule
        group.MapPost("/", async (Guid rulesetId, CreateRuleRequest request, IRuleRepository repo, IRulesetRepository rulesetRepo) =>
        {
            var ruleset = await rulesetRepo.GetByIdAsync(rulesetId);
            if (ruleset is null) return Results.NotFound("Ruleset not found");
            
            var existingRules = await repo.GetByRulesetAsync(rulesetId);
            var maxOrder = existingRules.Any() ? existingRules.Max(r => r.SortOrder) : -1;
            
            var rule = new Rule
            {
                RulesetId = rulesetId,
                Name = request.Name,
                RuleText = request.RuleText,
                Description = request.Description,
                SortOrder = maxOrder + 1,
                IsActive = request.IsActive ?? true,
                AppliesTo = request.AppliesTo
            };
            
            await repo.AddAsync(rule);
            return Results.Created($"/api/v1/rulesets/{rulesetId}/rules/{rule.Id}", rule);
        });
        
        // Update rule
        group.MapPut("/{ruleId:guid}", async (Guid rulesetId, Guid ruleId, UpdateRuleRequest request, IRuleRepository repo) =>
        {
            var rule = await repo.GetByIdAsync(ruleId);
            if (rule is null || rule.RulesetId != rulesetId) return Results.NotFound();
            
            rule.Name = request.Name ?? rule.Name;
            rule.RuleText = request.RuleText ?? rule.RuleText;
            rule.Description = request.Description ?? rule.Description;
            rule.IsActive = request.IsActive ?? rule.IsActive;
            rule.AppliesTo = request.AppliesTo ?? rule.AppliesTo;
            
            await repo.UpdateAsync(rule);
            return Results.Ok(rule);
        });
        
        // Delete rule
        group.MapDelete("/{ruleId:guid}", async (Guid rulesetId, Guid ruleId, IRuleRepository repo) =>
        {
            var rule = await repo.GetByIdAsync(ruleId);
            if (rule is null || rule.RulesetId != rulesetId) return Results.NotFound();
            
            await repo.DeleteAsync(ruleId);
            return Results.NoContent();
        });
        
        // Reorder rules
        group.MapPost("/reorder", async (Guid rulesetId, ReorderRulesRequest request, IRuleRepository repo) =>
        {
            await repo.ReorderAsync(rulesetId, request.RuleIds);
            return Results.Ok();
        });
    }
}
```

**Api/Models/Requests/** - Add request DTOs:
```csharp
public record CreateRulesetRequest(
    string Name,
    string? DisplayName,
    string? Description,
    RulesetCategory Category,
    int? Priority,
    bool? IsActive
);

public record UpdateRulesetRequest(
    string? Name,
    string? DisplayName,
    string? Description,
    RulesetCategory? Category,
    int? Priority,
    bool? IsActive
);

public record CloneRulesetRequest(
    string NewName,
    string? NewDisplayName
);

public record CreateRuleRequest(
    string Name,
    string RuleText,
    string? Description,
    bool? IsActive,
    string? AppliesTo
);

public record UpdateRuleRequest(
    string? Name,
    string? RuleText,
    string? Description,
    bool? IsActive,
    string? AppliesTo
);

public record ReorderRulesRequest(
    IEnumerable<Guid> RuleIds
);
```

**Update Program.cs** - Map endpoints:
```csharp
app.MapRulesetEndpoints();
app.MapRuleEndpoints();
```

### 4. Web Layer - Blazor UI

**Components/Pages/Rulesets/Index.razor:**
Create a page listing all rulesets with:
- Filter chips by category (All, Security, Escalation, Validation, Communication, General)
- Table showing: Name, Category, Rules count, Priority, Status (Active/Inactive), Actions
- Built-in badge for IsBuiltIn rulesets
- Actions: Edit, Clone, Delete (disabled for built-in)
- Create Ruleset button

**Components/Pages/Rulesets/Create.razor:**
Form to create new ruleset:
- Name (required)
- Display Name
- Description (textarea)
- Category (dropdown)
- Priority (number)
- Active (checkbox)

**Components/Pages/Rulesets/Edit.razor:**
Page for editing ruleset and managing rules:
- Ruleset properties form at top
- Rules list below with:
  - Drag-and-drop reordering (MudDropZone)
  - Inline toggle for Active status
  - Edit/Delete buttons per rule
- Add Rule button opens dialog
- Rule dialog: Name, Rule Text (multiline), Description, Applies To, Active

**Components/Layout/NavMenu.razor** - Add navigation:
```razor
<MudNavLink Href="/rulesets" Icon="@Icons.Material.Filled.Rule">Rulesets</MudNavLink>
```

### 5. Database Migration

After creating entities and configurations:
```bash
cd admin/dotnet
dotnet ef migrations add AddRulesetsAndRules \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
dotnet ef database update \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
```

### 6. Seed Data

Create seed data for built-in rulesets. Add to `LucidDbContext.OnModelCreating` or create a separate seeder.

**Security Ruleset:**
- Name: "security-rules"
- DisplayName: "Security Rules"
- Category: Security
- IsBuiltIn: true
- Priority: 10
- Rules:
  1. Name: "no-admin-accounts", RuleText: "Never reset passwords for accounts ending in '-admin', '-svc', or 'service'."
  2. Name: "no-privileged-groups", RuleText: "Never modify group membership for Domain Admins, Enterprise Admins, or Schema Admins."
  3. Name: "verify-manager", RuleText: "Always verify the requesting user has a manager listed in Active Directory."
  4. Name: "executives-review", RuleText: "Flag any request involving accounts in the 'Executives' OU for manual review."

**Escalation Ruleset:**
- Name: "escalation-rules"
- DisplayName: "Escalation Rules"
- Category: Escalation
- IsBuiltIn: true
- Priority: 20
- Rules:
  1. Name: "low-confidence", RuleText: "Escalate to human queue if classification confidence is below 0.6."
  2. Name: "unknown-type", RuleText: "Escalate if ticket type is 'unknown' or cannot be determined."
  3. Name: "user-not-found", RuleText: "Escalate if the affected user cannot be found in Active Directory."
  4. Name: "resource-not-found", RuleText: "Escalate if the requested resource does not exist."

**Communication Ruleset:**
- Name: "communication-rules"
- DisplayName: "Communication Rules"
- Category: Communication
- IsBuiltIn: true
- Priority: 50
- Rules:
  1. Name: "use-first-name", RuleText: "Always address the user by their first name in customer-visible comments."
  2. Name: "include-ticket-number", RuleText: "Include the ticket number in all communications."
  3. Name: "password-next-steps", RuleText: "Provide clear next steps when a temporary password is issued."
  4. Name: "professional-tone", RuleText: "Use professional, friendly tone in all messages."

## Testing

After implementation:
1. Navigate to /rulesets - should see built-in rulesets
2. Create a new ruleset with custom rules
3. Edit rules, reorder them via drag-and-drop
4. Clone a built-in ruleset - should create editable copy
5. Verify built-in rulesets cannot be deleted (button disabled or error)
6. Test API endpoints directly via Swagger or curl

## Summary

This adds:
- 2 new entities (Ruleset, Rule)
- 2 new repositories with interfaces
- REST API endpoints for full CRUD + clone + reorder
- Blazor UI for managing rulesets and rules
- Built-in seed data for common rulesets
- Database migration
