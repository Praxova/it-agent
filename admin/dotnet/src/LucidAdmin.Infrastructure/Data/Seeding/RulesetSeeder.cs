using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Data.Seeding;

/// <summary>
/// Seeds built-in rulesets that provide default behavioral guidance to agents.
/// </summary>
public class RulesetSeeder
{
    private readonly LucidDbContext _context;
    private readonly ILogger<RulesetSeeder> _logger;

    public RulesetSeeder(LucidDbContext context, ILogger<RulesetSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await SeedSecurityRuleset();
        await SeedEscalationRuleset();
        await _context.SaveChangesAsync();
    }

    private async Task SeedSecurityRuleset()
    {
        const string rulesetName = "security-defaults";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Security Defaults",
                Description = "Default security rules to prevent risky operations",
                Category = RulesetCategory.Security,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "no-admin-password-reset",
                    RuleText = "Never reset passwords for accounts that are members of the Domain Admins, Enterprise Admins, or other privileged groups.",
                    Description = "Prevents password resets on privileged accounts",
                    Priority = 10,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "verify-ticket-authenticity",
                    RuleText = "Always verify that the ticket requester's identity matches the account being modified. Reject requests where these don't match unless explicitly approved by a manager.",
                    Description = "Prevents unauthorized account modifications",
                    Priority = 20,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "require-approval-for-sensitive",
                    RuleText = "For any operation involving privileged accounts, executive accounts, or sensitive resources, require explicit manager approval before proceeding.",
                    Description = "Requires approval for sensitive operations",
                    Priority = 30,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }

    private async Task SeedEscalationRuleset()
    {
        const string rulesetName = "escalation-defaults";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Escalation Defaults",
                Description = "Default rules for when to escalate tickets to human operators",
                Category = RulesetCategory.Escalation,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "escalate-on-uncertainty",
                    RuleText = "If you are uncertain about the correct action, or if the request is ambiguous, escalate to a human operator with a clear explanation of the ambiguity.",
                    Description = "Escalate when uncertain",
                    Priority = 10,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "escalate-on-error",
                    RuleText = "If an operation fails after reasonable retry attempts, escalate the ticket with full error details and context.",
                    Description = "Escalate after repeated failures",
                    Priority = 20,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "escalate-policy-violations",
                    RuleText = "If a request would violate security policies or best practices, escalate rather than proceeding. Explain the policy concern clearly.",
                    Description = "Escalate policy violations",
                    Priority = 30,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }
}
