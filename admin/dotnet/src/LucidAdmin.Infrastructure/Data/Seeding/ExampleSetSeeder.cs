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
        await SeedDispatcherClassificationExamples();
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

    private async Task SeedDispatcherClassificationExamples()
    {
        const string setName = "it-dispatch-classification";

        var existing = await _context.ExampleSets
            .Include(e => e.Examples)
            .FirstOrDefaultAsync(e => e.Name == setName);

        if (existing == null)
        {
            var exampleSet = new ExampleSet
            {
                Name = setName,
                DisplayName = "IT Dispatch Classification",
                Description = "Multi-type classification examples for the IT dispatcher workflow. Teaches LLM to classify tickets as password-reset, group-membership, file-permissions, or unknown.",
                TargetTicketType = TicketType.Unknown,
                IsBuiltIn = true,
                IsActive = true
            };

            var examples = new List<Example>
            {
                new Example
                {
                    Name = "password-reset-standard",
                    TicketShortDescription = "Password reset needed",
                    TicketDescription = "User John Smith needs his password reset. He forgot it over the weekend.",
                    ExpectedTicketType = TicketType.PasswordReset,
                    ExpectedConfidence = 0.95m,
                    ExpectedAffectedUser = "jsmith",
                    Notes = "Standard password reset request",
                    SortOrder = 0,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "group-membership-add",
                    TicketShortDescription = "Add user to security group",
                    TicketDescription = "Please add user jane.doe to the Finance-Reports security group in Active Directory.",
                    CallerName = "Mike Manager",
                    ExpectedTicketType = TicketType.GroupAccessAdd,
                    ExpectedConfidence = 0.92m,
                    ExpectedAffectedUser = "jane.doe",
                    ExpectedTargetGroup = "Finance-Reports",
                    Notes = "Group membership addition",
                    SortOrder = 1,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "file-permissions-read",
                    TicketShortDescription = "Need folder access",
                    TicketDescription = "Sarah Connor needs read access to \\\\fileserver\\shared\\marketing folder.",
                    ExpectedTicketType = TicketType.FilePermissionGrant,
                    ExpectedConfidence = 0.88m,
                    ExpectedAffectedUser = "sconnor",
                    ExpectedTargetResource = "\\\\fileserver\\shared\\marketing",
                    ExpectedPermissionLevel = "read",
                    Notes = "File permissions request",
                    SortOrder = 2,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "unknown-printer-jam",
                    TicketShortDescription = "Printer jammed",
                    TicketDescription = "The printer on the 3rd floor is jammed again.",
                    ExpectedTicketType = TicketType.OutOfScope,
                    ExpectedConfidence = 0.15m,
                    ExpectedShouldEscalate = true,
                    ExpectedEscalationReason = "Hardware issue - not in agent capabilities",
                    Notes = "Unknown type - should escalate",
                    SortOrder = 3,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "unknown-vpn-issue",
                    TicketShortDescription = "VPN not working",
                    TicketDescription = "I can't connect to the VPN from home.",
                    ExpectedTicketType = TicketType.OutOfScope,
                    ExpectedConfidence = 0.20m,
                    ExpectedShouldEscalate = true,
                    ExpectedEscalationReason = "VPN connectivity - not in agent capabilities",
                    Notes = "Unknown type - should escalate",
                    SortOrder = 4,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "group-membership-urgent-removal",
                    TicketShortDescription = "Remove terminated employee from IT-Admins",
                    TicketDescription = "Remove user mike.jones from the IT-Admins group immediately - he's been terminated.",
                    ExpectedTicketType = TicketType.GroupAccessRemove,
                    ExpectedConfidence = 0.94m,
                    ExpectedAffectedUser = "mike.jones",
                    ExpectedTargetGroup = "IT-Admins",
                    Notes = "Urgent group membership removal",
                    SortOrder = 5,
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
