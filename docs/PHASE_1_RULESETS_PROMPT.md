We are now going to build a low-code agent builder into the admin portal.  This is going to be a significant effort.

I am going to first give you the high level picture so that you know where we are headed, then the prompt for the first portion.  Let me know if this prompt is ok to post in the chat window, or if I need to create a file and give you the reference.  

At a high level we are going to give the users a graphical way to create gripetape workflows/pipelines/tasks, etc.  Please reference the grip tape documentation as needed to ensure we are creating compliant griptape code at the end.

The Conceptual Model
┌─────────────────────────────────────────────────────────────────────────────┐
│                          WORKFLOW DESIGNER                                   │
│                                                                             │
│   ┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐          │
│   │ Trigger  │────▶│ Classify │────▶│  Route   │────▶│ Execute  │          │
│   │(ServiceNow)│    │  (LLM)   │     │          │     │          │          │
│   └──────────┘     └────┬─────┘     └────┬─────┘     └────┬─────┘          │
│                         │                │                │                 │
│                    Uses Examples    Uses Rules      Uses Capabilities       │
│                         │                │                │                 │
└─────────────────────────┼────────────────┼────────────────┼─────────────────┘
                          │                │                │
          ┌───────────────┘                │                │
          │                    ┌───────────┘                │
          ▼                    ▼                            ▼
┌──────────────────┐  ┌──────────────────┐  ┌─────────────────────────────────┐
│    EXAMPLES      │  │  RULES/RULESETS  │  │      CAPABILITIES               │
│   (Few-Shot)     │  │                  │  │   (from Tool Servers)           │
│                  │  │  • Security      │  │                                 │
│  Ticket → {      │  │  • Escalation    │  │  • ad-password-reset            │
│    type,         │  │  • Validation    │  │  • ad-group-add                 │
│    user,         │  │  • Communication │  │  • ntfs-permission-grant        │
│    confidence    │  │                  │  │                                 │
│  }               │  │                  │  │                                 │
└──────────────────┘  └──────────────────┘  └─────────────────────────────────┘
Screen Relationships
ScreenPurposeData ModelOutputWorkflow DesignerVisual flow of stepsSteps, Connections, ConditionsPipeline definition (JSON → Python)Rules EditorBehavioral constraintsRulesets containing RulesGriptape Ruleset codeExamples EditorFew-shot training dataExample tickets + expected outputClassifier prompt
Key Insight: The Workflow Designer references Rules and Examples but doesn't contain them. This lets you:

Reuse the same Ruleset across multiple workflows
Share Examples between different agent configurations
Update Rules without touching the workflow

Data Model
┌─────────────────────────────────────────────────────────────────┐
│                        WorkflowDefinition                        │
├─────────────────────────────────────────────────────────────────┤
│  Id, Name, Description, Version                                  │
│  AgentId (which agent uses this workflow)                        │
│  IsActive, IsBuiltIn                                             │
├─────────────────────────────────────────────────────────────────┤
│  Trigger                                                         │
│    ├─ ConnectorType (ServiceNow, Jira, Email)                   │
│    ├─ ServiceAccountId (FK)                                      │
│    ├─ AssignmentGroup                                            │
│    └─ PollIntervalSeconds                                        │
├─────────────────────────────────────────────────────────────────┤
│  Steps[]                                                         │
│    ├─ Id, Type (Classify|Validate|Execute|Update|Escalate)      │
│    ├─ Position (x, y for visual layout)                         │
│    ├─ Configuration (JSON - type-specific)                       │
│    ├─ RulesetIds[] (rules that apply to this step)              │
│    └─ Transitions[]                                              │
│         ├─ TargetStepId                                          │
│         ├─ Condition (e.g., "confidence >= 0.8")                │
│         └─ Label ("success", "escalate", "review")              │
├─────────────────────────────────────────────────────────────────┤
│  ExampleSetId (FK - which few-shot examples to use)              │
│  DefaultRulesetIds[] (rules that apply to all steps)            │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                           Ruleset                                │
├─────────────────────────────────────────────────────────────────┤
│  Id, Name, Description                                           │
│  Category (Security|Validation|Communication|Escalation)         │
│  IsBuiltIn, IsActive                                             │
├─────────────────────────────────────────────────────────────────┤
│  Rules[]                                                         │
│    ├─ Id, Name, Description                                      │
│    ├─ RuleText (the actual instruction)                         │
│    ├─ Priority (order of application)                           │
│    └─ AppliesTo (step type or "all")                            │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                          ExampleSet                              │
├─────────────────────────────────────────────────────────────────┤
│  Id, Name, Description                                           │
│  TicketTypes[] (which types this set covers)                     │
│  IsBuiltIn, IsActive                                             │
├─────────────────────────────────────────────────────────────────┤
│  Examples[]                                                      │
│    ├─ Id                                                         │
│    ├─ ShortDescription                                           │
│    ├─ Description                                                │
│    ├─ ExpectedOutput (JSON)                                      │
│    │    ├─ ticket_type                                           │
│    │    ├─ confidence                                            │
│    │    ├─ affected_user                                         │
│    │    ├─ target_group                                          │
│    │    └─ ...                                                   │
│    └─ Notes (why this example is useful)                        │
└─────────────────────────────────────────────────────────────────┘
Technology
Blazor + Drawflow.js
Drawflow is a lightweight (~15KB), MIT-licensed JavaScript library for creating visual node editors.
┌─────────────────────────────────────────────────────────────────┐
│  Blazor Server App                                               │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Admin Portal (existing)                                   │  │
│  │    • Service Accounts                                      │  │
│  │    • Tool Servers                                          │  │
│  │    • Agents                                                │  │
│  │    • Capability Mappings                                   │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Workflow Designer (new)                                   │  │
│  │    • Drawflow canvas (JS interop)                         │  │
│  │    • Node configuration panels (Blazor forms)             │  │
│  │    • Save/Load workflow JSON                              │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Rules Editor (new)                                        │  │
│  │    • Pure Blazor CRUD                                      │  │
│  │    • MudBlazor forms                                       │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Examples Editor (new)                                     │  │
│  │    • Pure Blazor CRUD                                      │  │
│  │    • MudBlazor forms with JSON preview                    │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘

## Code Generation Flow
```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Visual     │     │    JSON      │     │   Python     │
│   Designer   │────▶│  Definition  │────▶│    Code      │
│   (Blazor)   │     │              │     │  (Griptape)  │
└──────────────┘     └──────────────┘     └──────────────┘
                            │
                            ▼
                     ┌──────────────┐
                     │   Database   │
                     │  (EF Core)   │
                     └──────────────┘
                     
                     


Entity Models Overview
First, let me sketch the complete data model so we know where we're headed, then the Claude Code prompt will focus on just the Rules/Rulesets portion.
┌─────────────────────────────────────────────────────────────────────────────┐
│                            DATA MODEL OVERVIEW                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PHASE 1 (This prompt)         PHASE 2                  PHASE 3            │
│  ┌─────────────────┐          ┌─────────────────┐      ┌─────────────────┐ │
│  │    Ruleset      │          │   ExampleSet    │      │WorkflowDefinition│ │
│  │  ┌───────────┐  │          │  ┌───────────┐  │      │  ┌───────────┐  │ │
│  │  │   Rule    │  │          │  │  Example  │  │      │  │WorkflowStep│  │ │
│  │  │   Rule    │  │          │  │  Example  │  │      │  │WorkflowStep│  │ │
│  │  │   Rule    │  │          │  │  Example  │  │      │  │   ...     │  │ │
│  │  └───────────┘  │          │  └───────────┘  │      │  └───────────┘  │ │
│  └─────────────────┘          └─────────────────┘      └─────────────────┘ │
│           │                            │                        │          │
│           └────────────────────────────┴────────────────────────┘          │
│                                        │                                    │
│                                        ▼                                    │
│                               ┌─────────────────┐                          │
│                               │      Agent      │                          │
│                               │   (existing)    │                          │
│                               └─────────────────┘                          │
└─────────────────────────────────────────────────────────────────────────────┘

Claude Code Prompt: Add Rules and Rulesets to Admin Portal
Add Rules and Rulesets management to the Lucid Admin Portal. This is Phase 1 of the 
Workflow Designer feature set (per ADR-009 and upcoming ADR-010).

## Context

Rulesets contain Rules that govern agent behavior. These map directly to Griptape's 
Ruleset/Rule classes. Examples:
- Security rules: "Never reset passwords for accounts in the Admins group"
- Validation rules: "Always verify the user exists before taking action"
- Communication rules: "Always include ticket number in customer responses"
- Escalation rules: "Escalate if confidence is below 0.6"

## Files to Create/Modify

### 1. Core Entities

**Create `/src/LucidAdmin.Core/Entities/Ruleset.cs`:**
```csharp
namespace LucidAdmin.Core.Entities;

/// <summary>
/// A collection of rules that govern agent behavior.
/// Maps to Griptape's Ruleset class.
/// </summary>
public class Ruleset : BaseEntity
{
    /// <summary>
    /// Unique name for the ruleset (e.g., "security-rules", "escalation-rules").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Description of what this ruleset does.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Category for organization (Security, Validation, Communication, Escalation, Custom).
    /// </summary>
    public required string Category { get; set; }
    
    /// <summary>
    /// Whether this is a built-in ruleset (read-only, can be copied but not edited).
    /// </summary>
    public bool IsBuiltIn { get; set; }
    
    /// <summary>
    /// Whether this ruleset is active and available for use.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Rules in this ruleset.
    /// </summary>
    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
}
```

**Create `/src/LucidAdmin.Core/Entities/Rule.cs`:**
```csharp
namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single rule that provides behavioral guidance to the agent.
/// Maps to Griptape's Rule class.
/// </summary>
public class Rule : BaseEntity
{
    /// <summary>
    /// Foreign key to parent Ruleset.
    /// </summary>
    public Guid RulesetId { get; set; }
    
    /// <summary>
    /// Parent Ruleset navigation property.
    /// </summary>
    public Ruleset? Ruleset { get; set; }
    
    /// <summary>
    /// Short name for the rule (e.g., "no-admin-password-reset").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// The actual rule text that will be included in the agent prompt.
    /// Example: "Never reset passwords for accounts that are members of the Domain Admins group."
    /// </summary>
    public required string RuleText { get; set; }
    
    /// <summary>
    /// Optional description explaining why this rule exists.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Priority/order within the ruleset (lower = higher priority).
    /// </summary>
    public int Priority { get; set; } = 100;
    
    /// <summary>
    /// Whether this rule is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
```

**Create `/src/LucidAdmin.Core/Enums/RulesetCategory.cs`:**
```csharp
namespace LucidAdmin.Core.Enums;

/// <summary>
/// Categories for organizing rulesets.
/// </summary>
public static class RulesetCategory
{
    public const string Security = "Security";
    public const string Validation = "Validation";
    public const string Communication = "Communication";
    public const string Escalation = "Escalation";
    public const string Custom = "Custom";
    
    public static readonly string[] All = { Security, Validation, Communication, Escalation, Custom };
}
```

### 2. Repository Interface

**Create `/src/LucidAdmin.Core/Interfaces/Repositories/IRulesetRepository.cs`:**
```csharp
namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IRulesetRepository : IRepository<Ruleset>
{
    /// <summary>
    /// Get ruleset with all its rules loaded.
    /// </summary>
    Task<Ruleset?> GetWithRulesAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Get all rulesets in a category.
    /// </summary>
    Task<IEnumerable<Ruleset>> GetByCategoryAsync(string category, CancellationToken ct = default);
    
    /// <summary>
    /// Get all active rulesets with their rules.
    /// </summary>
    Task<IEnumerable<Ruleset>> GetAllActiveWithRulesAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Check if a ruleset name already exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
```

### 3. EF Core Configuration

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/RulesetConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class RulesetConfiguration : IEntityTypeConfiguration<Ruleset>
{
    public void Configure(EntityTypeBuilder<Ruleset> builder)
    {
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
            .HasMaxLength(50);
            
        builder.HasMany(r => r.Rules)
            .WithOne(rule => rule.Ruleset)
            .HasForeignKey(rule => rule.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Create `/src/LucidAdmin.Infrastructure/Data/Configurations/RuleConfiguration.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(r => r.RuleText)
            .IsRequired()
            .HasMaxLength(2000);
            
        builder.Property(r => r.Description)
            .HasMaxLength(1000);
            
        builder.HasIndex(r => new { r.RulesetId, r.Name })
            .IsUnique();
    }
}
```

**Update `/src/LucidAdmin.Infrastructure/Data/LucidDbContext.cs`:**
Add DbSets:
```csharp
public DbSet<Ruleset> Rulesets => Set<Ruleset>();
public DbSet<Rule> Rules => Set<Rule>();
```

### 4. Repository Implementation

**Create `/src/LucidAdmin.Infrastructure/Repositories/RulesetRepository.cs`:**
```csharp
using Microsoft.EntityFrameworkCore;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Infrastructure.Repositories;

public class RulesetRepository : RepositoryBase<Ruleset>, IRulesetRepository
{
    public RulesetRepository(LucidDbContext context) : base(context) { }
    
    public async Task<Ruleset?> GetWithRulesAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet
            .Include(r => r.Rules.OrderBy(rule => rule.Priority))
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }
    
    public async Task<IEnumerable<Ruleset>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        return await DbSet
            .Where(r => r.Category == category)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
    }
    
    public async Task<IEnumerable<Ruleset>> GetAllActiveWithRulesAsync(CancellationToken ct = default)
    {
        return await DbSet
            .Where(r => r.IsActive)
            .Include(r => r.Rules.Where(rule => rule.IsActive).OrderBy(rule => rule.Priority))
            .OrderBy(r => r.Category)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);
    }
    
    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await DbSet.AnyAsync(r => r.Name == name, ct);
    }
}
```

**Update `/src/LucidAdmin.Infrastructure/DependencyInjection.cs`:**
Add registration:
```csharp
services.AddScoped<IRulesetRepository, RulesetRepository>();
```

### 5. API Request/Response Models

**Create `/src/LucidAdmin.Web/Api/Models/Requests/CreateRulesetRequest.cs`:**
```csharp
namespace LucidAdmin.Web.Api.Models.Requests;

public record CreateRulesetRequest(
    string Name,
    string? DisplayName,
    string? Description,
    string Category,
    bool IsActive = true
);
```

**Create `/src/LucidAdmin.Web/Api/Models/Requests/UpdateRulesetRequest.cs`:**
```csharp
namespace LucidAdmin.Web.Api.Models.Requests;

public record UpdateRulesetRequest(
    string? DisplayName,
    string? Description,
    string? Category,
    bool? IsActive
);
```

**Create `/src/LucidAdmin.Web/Api/Models/Requests/CreateRuleRequest.cs`:**
```csharp
namespace LucidAdmin.Web.Api.Models.Requests;

public record CreateRuleRequest(
    string Name,
    string RuleText,
    string? Description,
    int Priority = 100,
    bool IsActive = true
);
```

**Create `/src/LucidAdmin.Web/Api/Models/Requests/UpdateRuleRequest.cs`:**
```csharp
namespace LucidAdmin.Web.Api.Models.Requests;

public record UpdateRuleRequest(
    string? Name,
    string? RuleText,
    string? Description,
    int? Priority,
    bool? IsActive
);
```

**Create `/src/LucidAdmin.Web/Api/Models/Responses/RulesetResponse.cs`:**
```csharp
namespace LucidAdmin.Web.Api.Models.Responses;

public record RulesetResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    string Category,
    bool IsBuiltIn,
    bool IsActive,
    int RuleCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record RulesetDetailResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    string Category,
    bool IsBuiltIn,
    bool IsActive,
    List<RuleResponse> Rules,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record RuleResponse(
    Guid Id,
    string Name,
    string RuleText,
    string? Description,
    int Priority,
    bool IsActive
);
```

### 6. API Endpoints

**Create `/src/LucidAdmin.Web/Api/Endpoints/RulesetEndpoints.cs`:**
```csharp
using Microsoft.AspNetCore.Mvc;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;

namespace LucidAdmin.Web.Api.Endpoints;

public static class RulesetEndpoints
{
    public static void MapRulesetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rulesets")
            .WithTags("Rulesets");
        
        // List all rulesets
        group.MapGet("/", async (
            [FromQuery] string? category,
            IRulesetRepository repo) =>
        {
            var rulesets = string.IsNullOrEmpty(category)
                ? await repo.GetAllAsync()
                : await repo.GetByCategoryAsync(category);
                
            var response = rulesets.Select(r => new RulesetResponse(
                r.Id, r.Name, r.DisplayName, r.Description, r.Category,
                r.IsBuiltIn, r.IsActive, r.Rules.Count,
                r.CreatedAt, r.UpdatedAt
            ));
            
            return Results.Ok(response);
        });
        
        // Get ruleset categories
        group.MapGet("/categories", () => Results.Ok(RulesetCategory.All));
        
        // Get single ruleset with rules
        group.MapGet("/{id:guid}", async (
            Guid id,
            IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetWithRulesAsync(id);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            var response = new RulesetDetailResponse(
                ruleset.Id, ruleset.Name, ruleset.DisplayName, ruleset.Description,
                ruleset.Category, ruleset.IsBuiltIn, ruleset.IsActive,
                ruleset.Rules.Select(r => new RuleResponse(
                    r.Id, r.Name, r.RuleText, r.Description, r.Priority, r.IsActive
                )).ToList(),
                ruleset.CreatedAt, ruleset.UpdatedAt
            );
            
            return Results.Ok(response);
        });
        
        // Create ruleset
        group.MapPost("/", async (
            CreateRulesetRequest request,
            IRulesetRepository repo) =>
        {
            if (await repo.ExistsAsync(request.Name))
                return Results.Conflict(new { error = "RulesetExists", message = $"Ruleset '{request.Name}' already exists" });
                
            var ruleset = new Ruleset
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Category = request.Category,
                IsActive = request.IsActive,
                IsBuiltIn = false
            };
            
            await repo.AddAsync(ruleset);
            
            return Results.Created($"/api/v1/rulesets/{ruleset.Id}", new RulesetResponse(
                ruleset.Id, ruleset.Name, ruleset.DisplayName, ruleset.Description,
                ruleset.Category, ruleset.IsBuiltIn, ruleset.IsActive, 0,
                ruleset.CreatedAt, ruleset.UpdatedAt
            ));
        });
        
        // Update ruleset
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateRulesetRequest request,
            IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetByIdAsync(id);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (ruleset.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn", message = "Built-in rulesets cannot be modified. Copy it to create a custom version." });
            
            if (request.DisplayName is not null) ruleset.DisplayName = request.DisplayName;
            if (request.Description is not null) ruleset.Description = request.Description;
            if (request.Category is not null) ruleset.Category = request.Category;
            if (request.IsActive.HasValue) ruleset.IsActive = request.IsActive.Value;
            
            await repo.UpdateAsync(ruleset);
            
            return Results.Ok(new RulesetResponse(
                ruleset.Id, ruleset.Name, ruleset.DisplayName, ruleset.Description,
                ruleset.Category, ruleset.IsBuiltIn, ruleset.IsActive, ruleset.Rules.Count,
                ruleset.CreatedAt, ruleset.UpdatedAt
            ));
        });
        
        // Delete ruleset
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetByIdAsync(id);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (ruleset.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotDeleteBuiltIn", message = "Built-in rulesets cannot be deleted." });
            
            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
        
        // Copy ruleset (for customizing built-in)
        group.MapPost("/{id:guid}/copy", async (
            Guid id,
            [FromQuery] string newName,
            IRulesetRepository repo) =>
        {
            var source = await repo.GetWithRulesAsync(id);
            if (source is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (await repo.ExistsAsync(newName))
                return Results.Conflict(new { error = "RulesetExists", message = $"Ruleset '{newName}' already exists" });
            
            var copy = new Ruleset
            {
                Name = newName,
                DisplayName = $"{source.DisplayName} (Copy)",
                Description = source.Description,
                Category = source.Category,
                IsActive = true,
                IsBuiltIn = false,
                Rules = source.Rules.Select(r => new Rule
                {
                    Name = r.Name,
                    RuleText = r.RuleText,
                    Description = r.Description,
                    Priority = r.Priority,
                    IsActive = r.IsActive
                }).ToList()
            };
            
            await repo.AddAsync(copy);
            
            return Results.Created($"/api/v1/rulesets/{copy.Id}", new RulesetResponse(
                copy.Id, copy.Name, copy.DisplayName, copy.Description,
                copy.Category, copy.IsBuiltIn, copy.IsActive, copy.Rules.Count,
                copy.CreatedAt, copy.UpdatedAt
            ));
        });
        
        // === Rule endpoints (nested under ruleset) ===
        
        // Add rule to ruleset
        group.MapPost("/{rulesetId:guid}/rules", async (
            Guid rulesetId,
            CreateRuleRequest request,
            IRulesetRepository repo) =>
        {
            var ruleset = await repo.GetWithRulesAsync(rulesetId);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (ruleset.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
                
            if (ruleset.Rules.Any(r => r.Name == request.Name))
                return Results.Conflict(new { error = "RuleExists", message = $"Rule '{request.Name}' already exists in this ruleset" });
            
            var rule = new Rule
            {
                RulesetId = rulesetId,
                Name = request.Name,
                RuleText = request.RuleText,
                Description = request.Description,
                Priority = request.Priority,
                IsActive = request.IsActive
            };
            
            ruleset.Rules.Add(rule);
            await repo.UpdateAsync(ruleset);
            
            return Results.Created(
                $"/api/v1/rulesets/{rulesetId}/rules/{rule.Id}",
                new RuleResponse(rule.Id, rule.Name, rule.RuleText, rule.Description, rule.Priority, rule.IsActive)
            );
        });
        
        // Update rule
        group.MapPut("/{rulesetId:guid}/rules/{ruleId:guid}", async (
            Guid rulesetId,
            Guid ruleId,
            UpdateRuleRequest request,
            IRulesetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var ruleset = await repo.GetByIdAsync(rulesetId);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (ruleset.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            var rule = await db.Rules.FindAsync(ruleId);
            if (rule is null || rule.RulesetId != rulesetId)
                return Results.NotFound(new { error = "RuleNotFound" });
            
            if (request.Name is not null) rule.Name = request.Name;
            if (request.RuleText is not null) rule.RuleText = request.RuleText;
            if (request.Description is not null) rule.Description = request.Description;
            if (request.Priority.HasValue) rule.Priority = request.Priority.Value;
            if (request.IsActive.HasValue) rule.IsActive = request.IsActive.Value;
            
            await db.SaveChangesAsync();
            
            return Results.Ok(new RuleResponse(rule.Id, rule.Name, rule.RuleText, rule.Description, rule.Priority, rule.IsActive));
        });
        
        // Delete rule
        group.MapDelete("/{rulesetId:guid}/rules/{ruleId:guid}", async (
            Guid rulesetId,
            Guid ruleId,
            IRulesetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var ruleset = await repo.GetByIdAsync(rulesetId);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (ruleset.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            var rule = await db.Rules.FindAsync(ruleId);
            if (rule is null || rule.RulesetId != rulesetId)
                return Results.NotFound(new { error = "RuleNotFound" });
            
            db.Rules.Remove(rule);
            await db.SaveChangesAsync();
            
            return Results.NoContent();
        });
        
        // Reorder rules in ruleset
        group.MapPost("/{rulesetId:guid}/rules/reorder", async (
            Guid rulesetId,
            List<Guid> ruleIds,
            IRulesetRepository repo,
            LucidAdmin.Infrastructure.Data.LucidDbContext db) =>
        {
            var ruleset = await repo.GetWithRulesAsync(rulesetId);
            if (ruleset is null)
                return Results.NotFound(new { error = "RulesetNotFound" });
                
            if (ruleset.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });
            
            // Update priorities based on order in list
            for (int i = 0; i < ruleIds.Count; i++)
            {
                var rule = ruleset.Rules.FirstOrDefault(r => r.Id == ruleIds[i]);
                if (rule != null)
                {
                    rule.Priority = i * 10; // Use increments of 10 for easy insertion later
                }
            }
            
            await db.SaveChangesAsync();
            
            return Results.Ok(new { message = "Rules reordered successfully" });
        });
    }
}
```

**Update `/src/LucidAdmin.Web/Program.cs`:**
Add endpoint registration:
```csharp
app.MapRulesetEndpoints();
```

### 7. Blazor UI Pages

**Create `/src/LucidAdmin.Web/Components/Pages/Rulesets/Index.razor`:**
```razor
@page "/rulesets"
@using LucidAdmin.Web.Api.Models.Responses
@using LucidAdmin.Core.Enums
@inject HttpClient Http
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<PageTitle>Rulesets - Lucid Admin</PageTitle>

<MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="mt-4">
    <MudGrid>
        <MudItem xs="12">
            <MudText Typo="Typo.h4">Rulesets</MudText>
            <MudText Typo="Typo.body2" Class="mb-4">
                Manage behavioral rules for your agents. Rulesets control security, validation, 
                communication, and escalation policies.
            </MudText>
        </MudItem>
        
        <MudItem xs="12">
            <MudPaper Class="pa-4">
                <MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center" Class="mb-4">
                    <MudStack Row="true" Spacing="2">
                        <MudSelect T="string" Label="Category" Value="_selectedCategory" 
                                   ValueChanged="OnCategoryChanged" Style="min-width: 200px;">
                            <MudSelectItem Value="@("")">All Categories</MudSelectItem>
                            @foreach (var category in RulesetCategory.All)
                            {
                                <MudSelectItem Value="@category">@category</MudSelectItem>
                            }
                        </MudSelect>
                    </MudStack>
                    <MudButton Variant="Variant.Filled" Color="Color.Primary" 
                               StartIcon="@Icons.Material.Filled.Add"
                               OnClick="@(() => Navigation.NavigateTo("/rulesets/create"))">
                        New Ruleset
                    </MudButton>
                </MudStack>
                
                @if (_loading)
                {
                    <MudProgressLinear Indeterminate="true" />
                }
                else if (_rulesets is null || !_rulesets.Any())
                {
                    <MudAlert Severity="Severity.Info">
                        No rulesets found. Create your first ruleset to get started.
                    </MudAlert>
                }
                else
                {
                    <MudTable Items="_rulesets" Hover="true" Dense="true">
                        <HeaderContent>
                            <MudTh>Name</MudTh>
                            <MudTh>Category</MudTh>
                            <MudTh>Rules</MudTh>
                            <MudTh>Status</MudTh>
                            <MudTh>Actions</MudTh>
                        </HeaderContent>
                        <RowTemplate>
                            <MudTd DataLabel="Name">
                                <MudStack Spacing="0">
                                    <MudLink Href="@($"/rulesets/{context.Id}")">
                                        @(context.DisplayName ?? context.Name)
                                    </MudLink>
                                    @if (context.IsBuiltIn)
                                    {
                                        <MudChip Size="Size.Small" Color="Color.Info" Variant="Variant.Outlined">
                                            Built-in
                                        </MudChip>
                                    }
                                </MudStack>
                            </MudTd>
                            <MudTd DataLabel="Category">
                                <MudChip Size="Size.Small" Color="@GetCategoryColor(context.Category)">
                                    @context.Category
                                </MudChip>
                            </MudTd>
                            <MudTd DataLabel="Rules">@context.RuleCount</MudTd>
                            <MudTd DataLabel="Status">
                                @if (context.IsActive)
                                {
                                    <MudChip Size="Size.Small" Color="Color.Success">Active</MudChip>
                                }
                                else
                                {
                                    <MudChip Size="Size.Small" Color="Color.Default">Inactive</MudChip>
                                }
                            </MudTd>
                            <MudTd DataLabel="Actions">
                                <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                                               Href="@($"/rulesets/{context.Id}")" 
                                               Disabled="@context.IsBuiltIn" />
                                <MudIconButton Icon="@Icons.Material.Filled.ContentCopy" Size="Size.Small"
                                               OnClick="@(() => CopyRuleset(context))"
                                               Title="Copy ruleset" />
                                <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small"
                                               Color="Color.Error"
                                               OnClick="@(() => DeleteRuleset(context))"
                                               Disabled="@context.IsBuiltIn" />
                            </MudTd>
                        </RowTemplate>
                    </MudTable>
                }
            </MudPaper>
        </MudItem>
    </MudGrid>
</MudContainer>

@code {
    private List<RulesetResponse>? _rulesets;
    private string _selectedCategory = "";
    private bool _loading = true;
    
    protected override async Task OnInitializedAsync()
    {
        await LoadRulesets();
    }
    
    private async Task LoadRulesets()
    {
        _loading = true;
        try
        {
            var url = string.IsNullOrEmpty(_selectedCategory)
                ? "/api/v1/rulesets"
                : $"/api/v1/rulesets?category={_selectedCategory}";
            _rulesets = await Http.GetFromJsonAsync<List<RulesetResponse>>(url);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error loading rulesets: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }
    
    private async Task OnCategoryChanged(string category)
    {
        _selectedCategory = category;
        await LoadRulesets();
    }
    
    private Color GetCategoryColor(string category) => category switch
    {
        "Security" => Color.Error,
        "Validation" => Color.Warning,
        "Communication" => Color.Info,
        "Escalation" => Color.Secondary,
        _ => Color.Default
    };
    
    private async Task CopyRuleset(RulesetResponse ruleset)
    {
        var newName = $"{ruleset.Name}-copy";
        try
        {
            var response = await Http.PostAsync($"/api/v1/rulesets/{ruleset.Id}/copy?newName={newName}", null);
            if (response.IsSuccessStatusCode)
            {
                Snackbar.Add("Ruleset copied successfully", Severity.Success);
                await LoadRulesets();
            }
            else
            {
                Snackbar.Add("Failed to copy ruleset", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
    }
    
    private async Task DeleteRuleset(RulesetResponse ruleset)
    {
        // In production, show a confirmation dialog first
        try
        {
            var response = await Http.DeleteAsync($"/api/v1/rulesets/{ruleset.Id}");
            if (response.IsSuccessStatusCode)
            {
                Snackbar.Add("Ruleset deleted", Severity.Success);
                await LoadRulesets();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
    }
}
```

**Create `/src/LucidAdmin.Web/Components/Pages/Rulesets/Edit.razor`:**
```razor
@page "/rulesets/{Id:guid}"
@page "/rulesets/create"
@using LucidAdmin.Web.Api.Models.Requests
@using LucidAdmin.Web.Api.Models.Responses
@using LucidAdmin.Core.Enums
@inject HttpClient Http
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<PageTitle>@(_isNew ? "Create Ruleset" : "Edit Ruleset") - Lucid Admin</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="mt-4">
    <MudText Typo="Typo.h4" Class="mb-4">
        @(_isNew ? "Create Ruleset" : (_ruleset?.DisplayName ?? _ruleset?.Name ?? "Edit Ruleset"))
    </MudText>
    
    @if (_loading)
    {
        <MudProgressLinear Indeterminate="true" />
    }
    else
    {
        <MudGrid>
            @* Ruleset Details *@
            <MudItem xs="12" md="4">
                <MudPaper Class="pa-4">
                    <MudText Typo="Typo.h6" Class="mb-3">Ruleset Details</MudText>
                    
                    <MudTextField @bind-Value="_name" Label="Name" Required="true" 
                                  Disabled="@(!_isNew)" Class="mb-3"
                                  HelperText="Unique identifier (e.g., security-rules)" />
                    
                    <MudTextField @bind-Value="_displayName" Label="Display Name" Class="mb-3" />
                    
                    <MudTextField @bind-Value="_description" Label="Description" 
                                  Lines="3" Class="mb-3" />
                    
                    <MudSelect @bind-Value="_category" Label="Category" Required="true" Class="mb-3">
                        @foreach (var cat in RulesetCategory.All)
                        {
                            <MudSelectItem Value="@cat">@cat</MudSelectItem>
                        }
                    </MudSelect>
                    
                    <MudSwitch @bind-Value="_isActive" Label="Active" Color="Color.Primary" Class="mb-3" />
                    
                    <MudStack Row="true" Spacing="2">
                        <MudButton Variant="Variant.Filled" Color="Color.Primary" 
                                   OnClick="SaveRuleset" Disabled="_saving">
                            @(_saving ? "Saving..." : "Save")
                        </MudButton>
                        <MudButton Variant="Variant.Outlined" 
                                   OnClick="@(() => Navigation.NavigateTo("/rulesets"))">
                            Cancel
                        </MudButton>
                    </MudStack>
                </MudPaper>
            </MudItem>
            
            @* Rules List *@
            <MudItem xs="12" md="8">
                <MudPaper Class="pa-4">
                    <MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center" Class="mb-3">
                        <MudText Typo="Typo.h6">Rules</MudText>
                        @if (!_isNew)
                        {
                            <MudButton Variant="Variant.Outlined" Size="Size.Small"
                                       StartIcon="@Icons.Material.Filled.Add"
                                       OnClick="ShowAddRuleDialog">
                                Add Rule
                            </MudButton>
                        }
                    </MudStack>
                    
                    @if (_isNew)
                    {
                        <MudAlert Severity="Severity.Info">
                            Save the ruleset first, then you can add rules.
                        </MudAlert>
                    }
                    else if (_rules is null || !_rules.Any())
                    {
                        <MudAlert Severity="Severity.Info">
                            No rules yet. Add your first rule to get started.
                        </MudAlert>
                    }
                    else
                    {
                        <MudList T="RuleResponse" Dense="true">
                            @foreach (var rule in _rules.OrderBy(r => r.Priority))
                            {
                                <MudListItem>
                                    <MudStack>
                                        <MudStack Row="true" Justify="Justify.SpaceBetween">
                                            <MudText Typo="Typo.subtitle2">@rule.Name</MudText>
                                            <MudStack Row="true" Spacing="1">
                                                <MudChip Size="Size.Small" Color="@(rule.IsActive ? Color.Success : Color.Default)">
                                                    @(rule.IsActive ? "Active" : "Inactive")
                                                </MudChip>
                                                <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                                                               OnClick="@(() => EditRule(rule))" />
                                                <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small"
                                                               Color="Color.Error"
                                                               OnClick="@(() => DeleteRule(rule))" />
                                            </MudStack>
                                        </MudStack>
                                        <MudText Typo="Typo.body2" Style="font-style: italic;">
                                            "@rule.RuleText"
                                        </MudText>
                                        @if (!string.IsNullOrEmpty(rule.Description))
                                        {
                                            <MudText Typo="Typo.caption" Color="Color.Secondary">
                                                @rule.Description
                                            </MudText>
                                        }
                                    </MudStack>
                                </MudListItem>
                                <MudDivider />
                            }
                        </MudList>
                    }
                </MudPaper>
            </MudItem>
        </MudGrid>
    }
</MudContainer>

@* Add/Edit Rule Dialog *@
<MudDialog @bind-Visible="_showRuleDialog">
    <TitleContent>
        @(_editingRule is null ? "Add Rule" : "Edit Rule")
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_ruleName" Label="Name" Required="true" Class="mb-3"
                      HelperText="Short identifier (e.g., no-admin-reset)" />
        <MudTextField @bind-Value="_ruleText" Label="Rule Text" Required="true" 
                      Lines="4" Class="mb-3"
                      HelperText="The instruction given to the agent" />
        <MudTextField @bind-Value="_ruleDescription" Label="Description" Lines="2" Class="mb-3"
                      HelperText="Why this rule exists (for documentation)" />
        <MudNumericField @bind-Value="_rulePriority" Label="Priority" Min="0" Max="1000" Class="mb-3"
                         HelperText="Lower number = higher priority" />
        <MudSwitch @bind-Value="_ruleIsActive" Label="Active" Color="Color.Primary" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => _showRuleDialog = false)">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="SaveRule">
            Save Rule
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [Parameter] public Guid? Id { get; set; }
    
    private bool _isNew => Id is null;
    private bool _loading = true;
    private bool _saving = false;
    
    // Ruleset fields
    private RulesetDetailResponse? _ruleset;
    private string _name = "";
    private string? _displayName;
    private string? _description;
    private string _category = "Custom";
    private bool _isActive = true;
    private List<RuleResponse>? _rules;
    
    // Rule dialog fields
    private bool _showRuleDialog = false;
    private RuleResponse? _editingRule;
    private string _ruleName = "";
    private string _ruleText = "";
    private string? _ruleDescription;
    private int _rulePriority = 100;
    private bool _ruleIsActive = true;
    
    protected override async Task OnInitializedAsync()
    {
        if (!_isNew)
        {
            await LoadRuleset();
        }
        else
        {
            _loading = false;
        }
    }
    
    private async Task LoadRuleset()
    {
        _loading = true;
        try
        {
            _ruleset = await Http.GetFromJsonAsync<RulesetDetailResponse>($"/api/v1/rulesets/{Id}");
            if (_ruleset != null)
            {
                _name = _ruleset.Name;
                _displayName = _ruleset.DisplayName;
                _description = _ruleset.Description;
                _category = _ruleset.Category;
                _isActive = _ruleset.IsActive;
                _rules = _ruleset.Rules;
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error loading ruleset: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }
    
    private async Task SaveRuleset()
    {
        _saving = true;
        try
        {
            if (_isNew)
            {
                var request = new CreateRulesetRequest(_name, _displayName, _description, _category, _isActive);
                var response = await Http.PostAsJsonAsync("/api/v1/rulesets", request);
                if (response.IsSuccessStatusCode)
                {
                    var created = await response.Content.ReadFromJsonAsync<RulesetResponse>();
                    Snackbar.Add("Ruleset created", Severity.Success);
                    Navigation.NavigateTo($"/rulesets/{created?.Id}");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add($"Error: {error}", Severity.Error);
                }
            }
            else
            {
                var request = new UpdateRulesetRequest(_displayName, _description, _category, _isActive);
                var response = await Http.PutAsJsonAsync($"/api/v1/rulesets/{Id}", request);
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Ruleset updated", Severity.Success);
                }
                else
                {
                    Snackbar.Add("Error updating ruleset", Severity.Error);
                }
            }
        }
        finally
        {
            _saving = false;
        }
    }
    
    private void ShowAddRuleDialog()
    {
        _editingRule = null;
        _ruleName = "";
        _ruleText = "";
        _ruleDescription = null;
        _rulePriority = 100;
        _ruleIsActive = true;
        _showRuleDialog = true;
    }
    
    private void EditRule(RuleResponse rule)
    {
        _editingRule = rule;
        _ruleName = rule.Name;
        _ruleText = rule.RuleText;
        _ruleDescription = rule.Description;
        _rulePriority = rule.Priority;
        _ruleIsActive = rule.IsActive;
        _showRuleDialog = true;
    }
    
    private async Task SaveRule()
    {
        try
        {
            if (_editingRule is null)
            {
                // Add new rule
                var request = new CreateRuleRequest(_ruleName, _ruleText, _ruleDescription, _rulePriority, _ruleIsActive);
                var response = await Http.PostAsJsonAsync($"/api/v1/rulesets/{Id}/rules", request);
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Rule added", Severity.Success);
                    _showRuleDialog = false;
                    await LoadRuleset();
                }
            }
            else
            {
                // Update existing rule
                var request = new UpdateRuleRequest(_ruleName, _ruleText, _ruleDescription, _rulePriority, _ruleIsActive);
                var response = await Http.PutAsJsonAsync($"/api/v1/rulesets/{Id}/rules/{_editingRule.Id}", request);
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Rule updated", Severity.Success);
                    _showRuleDialog = false;
                    await LoadRuleset();
                }
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
    }
    
    private async Task DeleteRule(RuleResponse rule)
    {
        try
        {
            var response = await Http.DeleteAsync($"/api/v1/rulesets/{Id}/rules/{rule.Id}");
            if (response.IsSuccessStatusCode)
            {
                Snackbar.Add("Rule deleted", Severity.Success);
                await LoadRuleset();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
    }
}
```

### 8. Add Navigation Link

**Update `/src/LucidAdmin.Web/Components/Layout/NavMenu.razor`:**
Add under the existing navigation items:
```razor
<MudNavLink Href="/rulesets" Icon="@Icons.Material.Filled.Rule">
    Rulesets
</MudNavLink>
```

### 9. Create Migration

After all files are created, run:
```bash
cd admin/dotnet
dotnet ef migrations add AddRulesetsAndRules \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
dotnet ef database update \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web
```

### 10. Add Seed Data for Built-in Rulesets

Create a data seeder or add to Program.cs to seed built-in rulesets:
```csharp
// In Program.cs after database migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
    
    if (!db.Rulesets.Any(r => r.IsBuiltIn))
    {
        var securityRuleset = new Ruleset
        {
            Name = "security-default",
            DisplayName = "Default Security Rules",
            Description = "Built-in security rules to protect sensitive accounts and operations",
            Category = "Security",
            IsBuiltIn = true,
            IsActive = true,
            Rules = new List<Rule>
            {
                new Rule
                {
                    Name = "no-admin-reset",
                    RuleText = "Never reset passwords for accounts that are members of Domain Admins, Enterprise Admins, or Schema Admins groups.",
                    Description = "Protects privileged accounts from automated password resets",
                    Priority = 10,
                    IsActive = true
                },
                new Rule
                {
                    Name = "no-service-account-reset",
                    RuleText = "Never reset passwords for service accounts (accounts starting with 'svc-' or 'sa-').",
                    Description = "Prevents breaking automated services",
                    Priority = 20,
                    IsActive = true
                },
                new Rule
                {
                    Name = "verify-user-exists",
                    RuleText = "Always verify a user exists in Active Directory before attempting any operation on their account.",
                    Description = "Prevents errors from typos or incorrect usernames",
                    Priority = 30,
                    IsActive = true
                }
            }
        };
        
        var escalationRuleset = new Ruleset
        {
            Name = "escalation-default",
            DisplayName = "Default Escalation Rules",
            Description = "Built-in rules for when to escalate tickets to human agents",
            Category = "Escalation",
            IsBuiltIn = true,
            IsActive = true,
            Rules = new List<Rule>
            {
                new Rule
                {
                    Name = "low-confidence-escalate",
                    RuleText = "If classification confidence is below 0.6, escalate the ticket to a human agent for review.",
                    Priority = 10,
                    IsActive = true
                },
                new Rule
                {
                    Name = "unknown-type-escalate",
                    RuleText = "If the ticket type cannot be determined, escalate to a human agent rather than guessing.",
                    Priority = 20,
                    IsActive = true
                },
                new Rule
                {
                    Name = "multiple-requests-escalate",
                    RuleText = "If a ticket contains multiple unrelated requests, escalate for human review.",
                    Priority = 30,
                    IsActive = true
                }
            }
        };
        
        db.Rulesets.AddRange(securityRuleset, escalationRuleset);
        await db.SaveChangesAsync();
    }
}
```

## Summary

This prompt creates:
- Core entities: Ruleset, Rule
- Repository with query methods
- Full CRUD API endpoints
- Blazor UI for listing, creating, editing rulesets and rules
- Built-in security and escalation rulesets as seed data

Build and test with:
```bash
cd admin/dotnet/src/LucidAdmin.Web
dotnet run
# Navigate to https://localhost:5000/rulesets
```

# references
{
`content`: `# ADR-010: Visual Workflow Designer Architecture
`path`: `/home/alton/Documents/lucid-it-agent/docs/adr/ADR-010-visual-workflow-designer.md`
}


