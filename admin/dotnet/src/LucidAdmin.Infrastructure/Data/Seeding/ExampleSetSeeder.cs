using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Data.Seeding;

/// <summary>
/// Seeds built-in example sets for classifier training.
/// </summary>
public class ExampleSetSeeder
{
    private readonly LucidDbContext _context;
    private readonly ILogger<ExampleSetSeeder> _logger;

    public ExampleSetSeeder(LucidDbContext context, ILogger<ExampleSetSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await SeedPasswordResetExamples();
        await SeedGroupAccessExamples();
        await _context.SaveChangesAsync();
    }

    private async Task SeedPasswordResetExamples()
    {
        const string setName = "password-reset-examples";

        var existing = await _context.ExampleSets
            .Include(e => e.Examples)
            .FirstOrDefaultAsync(e => e.Name == setName);

        if (existing == null)
        {
            var exampleSet = new ExampleSet
            {
                Name = setName,
                DisplayName = "Password Reset Examples",
                Description = "Examples for classifying password reset and account unlock requests",
                TargetTicketType = TicketType.PasswordReset,
                IsBuiltIn = true,
                IsActive = true
            };

            var examples = new List<Example>
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
                    IsActive = true,
                    ExampleSet = exampleSet
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
                    IsActive = true,
                    ExampleSet = exampleSet
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
                    IsActive = true,
                    ExampleSet = exampleSet
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
                    IsActive = true,
                    ExampleSet = exampleSet
                }
            };

            exampleSet.Examples = examples;
            _context.ExampleSets.Add(exampleSet);
            _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples",
                setName, examples.Count);
        }
        else
        {
            _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName);
        }
    }

    private async Task SeedGroupAccessExamples()
    {
        const string setName = "group-access-examples";

        var existing = await _context.ExampleSets
            .Include(e => e.Examples)
            .FirstOrDefaultAsync(e => e.Name == setName);

        if (existing == null)
        {
            var exampleSet = new ExampleSet
            {
                Name = setName,
                DisplayName = "Group Access Examples",
                Description = "Examples for classifying AD group membership requests",
                TargetTicketType = TicketType.GroupAccessAdd,
                IsBuiltIn = true,
                IsActive = true
            };

            var examples = new List<Example>
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
                    IsActive = true,
                    ExampleSet = exampleSet
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
                    IsActive = true,
                    ExampleSet = exampleSet
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
                    IsActive = true,
                    ExampleSet = exampleSet
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
                    IsActive = true,
                    ExampleSet = exampleSet
                }
            };

            exampleSet.Examples = examples;
            _context.ExampleSets.Add(exampleSet);
            _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples",
                setName, examples.Count);
        }
        else
        {
            _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName);
        }
    }
}
