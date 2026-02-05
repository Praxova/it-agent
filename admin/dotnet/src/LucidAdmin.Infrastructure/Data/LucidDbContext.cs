using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Data;

public class LucidDbContext : DbContext
{
    public DbSet<ServiceAccount> ServiceAccounts { get; set; } = null!;
    public DbSet<ToolServer> ToolServers { get; set; } = null!;
    public DbSet<Capability> Capabilities { get; set; } = null!;
    public DbSet<CapabilityMapping> CapabilityMappings { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
    public DbSet<Agent> Agents { get; set; } = null!;
    public DbSet<ApiKey> ApiKeys { get; set; } = null!;
    public DbSet<Ruleset> Rulesets { get; set; } = null!;
    public DbSet<Rule> Rules { get; set; } = null!;
    public DbSet<ExampleSet> ExampleSets { get; set; } = null!;
    public DbSet<Example> Examples { get; set; } = null!;
    public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; } = null!;
    public DbSet<WorkflowStep> WorkflowSteps { get; set; } = null!;
    public DbSet<StepTransition> StepTransitions { get; set; } = null!;
    public DbSet<WorkflowRulesetMapping> WorkflowRulesetMappings { get; set; } = null!;
    public DbSet<StepRulesetMapping> StepRulesetMappings { get; set; } = null!;
    public DbSet<ManualSubmission> ManualSubmissions { get; set; } = null!;
    public DbSet<AgentServiceAccountBinding> AgentServiceAccountBindings { get; set; } = null!;

    public LucidDbContext(DbContextOptions<LucidDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LucidDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<BaseEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
