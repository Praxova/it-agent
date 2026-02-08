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
        await SeedClassificationRuleset();
        await SeedSecurityRulesRuleset();
        await SeedCommunicationRuleset();
        await SeedAuditRuleset();
        await SeedSoftwareInstallRuleset();
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
            _context.Rules.AddRange(rules);
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
            _context.Rules.AddRange(rules);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }

    private async Task SeedClassificationRuleset()
    {
        const string rulesetName = "classification-rules";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Classification Rules",
                Description = "Rules for LLM ticket classification behavior",
                Category = RulesetCategory.Validation,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "intent-based-classification",
                    RuleText = "Classify tickets based on user intent and context, not just keyword matching",
                    Description = "Focus on intent rather than keywords",
                    Priority = 100,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "extract-affected-user",
                    RuleText = "Extract the affected username from the ticket - check 'Affected User' field first, then description",
                    Description = "Extract the correct affected user",
                    Priority = 200,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "primary-action-focus",
                    RuleText = "If multiple actions are requested, classify as the primary/first action mentioned",
                    Description = "Focus on primary action",
                    Priority = 300,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "confidence-threshold",
                    RuleText = "When confidence is below 80%, flag for human review rather than guessing",
                    Description = "Require high confidence",
                    Priority = 400,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _context.Rules.AddRange(rules);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }

    private async Task SeedSecurityRulesRuleset()
    {
        const string rulesetName = "security-rules";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Security Rules",
                Description = "Security constraints for ticket processing",
                Category = RulesetCategory.Security,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "no-admin-accounts",
                    RuleText = "Never reset passwords for accounts in Domain Admins, Enterprise Admins, or Schema Admins groups",
                    Description = "Protect admin accounts",
                    Priority = 100,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "verify-requester",
                    RuleText = "Verify the ticket requester has authority to request actions for the affected user",
                    Description = "Verify requester authority",
                    Priority = 200,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "service-account-protection",
                    RuleText = "Do not modify service accounts (accounts starting with 'svc-' or 'sa-')",
                    Description = "Protect service accounts",
                    Priority = 300,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "executive-protection",
                    RuleText = "Escalate any requests involving C-level executives to human review",
                    Description = "Escalate executive requests",
                    Priority = 400,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _context.Rules.AddRange(rules);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }

    private async Task SeedCommunicationRuleset()
    {
        const string rulesetName = "communication-rules";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Communication Rules",
                Description = "Guidelines for customer-facing communications",
                Category = RulesetCategory.Communication,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "professional-tone",
                    RuleText = "Use professional, friendly tone in all customer communications",
                    Description = "Maintain professional tone",
                    Priority = 100,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "no-technical-errors",
                    RuleText = "Never include technical error details or stack traces in customer-facing messages",
                    Description = "Hide technical details from customers",
                    Priority = 200,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "provide-next-steps",
                    RuleText = "Always provide next steps or contact information for follow-up questions",
                    Description = "Provide next steps",
                    Priority = 300,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "acknowledge-first",
                    RuleText = "Acknowledge the user's request before describing the resolution",
                    Description = "Acknowledge request first",
                    Priority = 400,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _context.Rules.AddRange(rules);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }

    private async Task SeedAuditRuleset()
    {
        const string rulesetName = "audit-rules";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Audit Rules",
                Description = "Logging and audit trail requirements",
                Category = RulesetCategory.Custom,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "log-all-actions",
                    RuleText = "Log all actions with ticket number, affected user, and timestamp",
                    Description = "Log all actions",
                    Priority = 100,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "record-confidence",
                    RuleText = "Record the classification confidence score for quality tracking",
                    Description = "Track confidence scores",
                    Priority = 200,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "document-failures",
                    RuleText = "Document any validation failures with specific reasons",
                    Description = "Document failures",
                    Priority = 300,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "track-performance",
                    RuleText = "Track execution time for performance monitoring",
                    Description = "Track performance",
                    Priority = 400,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _context.Rules.AddRange(rules);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }

    private async Task SeedSoftwareInstallRuleset()
    {
        const string rulesetName = "software-install-rules";

        var existing = await _context.Rulesets
            .Include(r => r.Rules)
            .FirstOrDefaultAsync(r => r.Name == rulesetName);

        if (existing == null)
        {
            var ruleset = new Ruleset
            {
                Name = rulesetName,
                DisplayName = "Software Install Rules",
                Description = "Rules governing automated software installation behavior",
                Category = RulesetCategory.Validation,
                IsBuiltIn = true,
                IsActive = true
            };

            var rules = new List<Rule>
            {
                new Rule
                {
                    Name = "catalog-enforcement",
                    RuleText = "Only install software that exists in the approved software catalog. If the requested software does not match any catalog entry, inform the user and escalate to a human operator.",
                    Description = "Restrict installations to approved catalog",
                    Priority = 100,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "computer-verification",
                    RuleText = "Before installing software, verify the target computer exists in Active Directory and is reachable on the network. Never attempt installation on a computer that cannot be verified.",
                    Description = "Verify computer before install",
                    Priority = 200,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "no-server-installs",
                    RuleText = "Do not install software on servers or domain controllers. Only install on workstation-class computers (computer objects in standard workstation OUs).",
                    Description = "Prevent server installations",
                    Priority = 300,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "single-package-per-request",
                    RuleText = "Process one software installation per ticket. If the user requests multiple packages, install the first identified package and advise them to submit separate tickets for additional software.",
                    Description = "One package per ticket",
                    Priority = 400,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "clarification-courtesy",
                    RuleText = "When asking the user for clarification, be polite and helpful. Provide specific options from the approved catalog when possible. Include instructions on how to find their computer name if needed.",
                    Description = "Polite clarification guidance",
                    Priority = 500,
                    IsActive = true,
                    Ruleset = ruleset
                },
                new Rule
                {
                    Name = "installation-verification",
                    RuleText = "After executing a software installation, report the outcome clearly to the user. Include the software name, target computer, and whether the installation succeeded or failed.",
                    Description = "Report install outcomes clearly",
                    Priority = 600,
                    IsActive = true,
                    Ruleset = ruleset
                }
            };

            _context.Rulesets.Add(ruleset);
            _context.Rules.AddRange(rules);
            _logger.LogInformation("Seeded built-in ruleset: {RulesetName} with {RuleCount} rules",
                rulesetName, rules.Count);
        }
        else
        {
            _logger.LogDebug("Built-in ruleset already exists: {RulesetName}", rulesetName);
        }
    }
}
