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
        await SeedSoftwareInstallExamples();
        await SeedSoftwareCatalogExamples();
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
            // Add software install examples if they don't exist yet
            if (!existing.Examples.Any(e => e.Name.StartsWith("software-install-")))
            {
                var newExamples = new List<Example>
                {
                    new Example
                    {
                        Name = "software-install-standard",
                        TicketShortDescription = "Install software on my computer",
                        TicketDescription = "I need Google Chrome installed on my workstation WS-HR-015.",
                        ExpectedTicketType = TicketType.SoftwareInstall,
                        ExpectedConfidence = 0.92m,
                        Notes = "Standard software install request for dispatcher routing",
                        SortOrder = 10,
                        IsActive = true,
                        ExampleSet = existing
                    },
                    new Example
                    {
                        Name = "software-install-vague",
                        TicketShortDescription = "Need new software",
                        TicketDescription = "I need some new software for my job. Can someone help me get it installed?",
                        ExpectedTicketType = TicketType.SoftwareInstall,
                        ExpectedConfidence = 0.70m,
                        Notes = "Vague software install - lower confidence, still routes to software-install workflow",
                        SortOrder = 11,
                        IsActive = true,
                        ExampleSet = existing
                    }
                };

                _context.Examples.AddRange(newExamples);
                _logger.LogInformation("Added software install examples to existing set: {ExampleSetName}", setName);
            }
            else
            {
                _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName);
            }
        }
    }

    private async Task SeedSoftwareInstallExamples()
    {
        const string setName = "software-install-examples";

        var existing = await _context.ExampleSets
            .Include(e => e.Examples)
            .FirstOrDefaultAsync(e => e.Name == setName);

        if (existing == null)
        {
            var exampleSet = new ExampleSet
            {
                Name = setName,
                DisplayName = "Software Install Examples",
                Description = "Examples for classifying software installation requests",
                TargetTicketType = TicketType.SoftwareInstall,
                IsBuiltIn = true,
                IsActive = true
            };

            var examples = new List<Example>
            {
                new Example
                {
                    Name = "install-chrome-clear",
                    TicketShortDescription = "Need Google Chrome installed",
                    TicketDescription = "I need Google Chrome installed on my laptop YOURPC01. My current browser isn't working well for our web apps.",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 0.95m,
                    Notes = "Clear request with both software and computer identified",
                    SortOrder = 0,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "install-vscode-no-computer",
                    TicketShortDescription = "Please install Visual Studio Code",
                    TicketDescription = "I'm a new developer and need VS Code installed. Can you help?",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 0.90m,
                    Notes = "Software identified but computer name missing — triggers clarification",
                    SortOrder = 1,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "install-ambiguous-software",
                    TicketShortDescription = "Need a PDF reader",
                    TicketDescription = "I need to be able to read PDF files on my computer YOURPC02.",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 0.75m,
                    Notes = "Computer identified but software ambiguous — multiple catalog matches, triggers clarification",
                    SortOrder = 2,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "install-unauthorized-software",
                    TicketShortDescription = "Install BitTorrent client",
                    TicketDescription = "I need a BitTorrent client installed on YOURPC03 for downloading files.",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 0.85m,
                    ExpectedShouldEscalate = true,
                    ExpectedEscalationReason = "Requested software not in approved catalog",
                    Notes = "Software not in catalog — should be denied/escalated",
                    SortOrder = 3,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "install-multiple-missing-info",
                    TicketShortDescription = "Software installation request",
                    TicketDescription = "I need some software installed on my machine.",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 0.65m,
                    Notes = "Both software and computer missing — needs clarification for both",
                    SortOrder = 4,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "install-from-catalog-exact",
                    TicketShortDescription = "Install 7-Zip on my workstation",
                    TicketDescription = "Please install 7-Zip on workstation WS-DEV-042. I need it for extracting archive files.",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 0.95m,
                    Notes = "Exact catalog match with computer — happy path, no clarification needed",
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

    private async Task SeedSoftwareCatalogExamples()
    {
        const string setName = "approved-software-catalog";

        var existing = await _context.ExampleSets
            .Include(e => e.Examples)
            .FirstOrDefaultAsync(e => e.Name == setName);

        if (existing == null)
        {
            var exampleSet = new ExampleSet
            {
                Name = setName,
                DisplayName = "Approved Software Catalog",
                Description = "Catalog of approved software available for automated installation. Used by the software install workflow to validate and match software requests.",
                TargetTicketType = TicketType.SoftwareInstall,
                IsBuiltIn = true,
                IsActive = true
            };

            var examples = new List<Example>
            {
                new Example
                {
                    Name = "chrome",
                    TicketShortDescription = "Google Chrome",
                    TicketDescription = "Google Chrome web browser. Aliases: chrome, google chrome, web browser",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install googlechrome -y",
                    Notes = "Enterprise web browser",
                    SortOrder = 0,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "firefox",
                    TicketShortDescription = "Mozilla Firefox",
                    TicketDescription = "Mozilla Firefox web browser. Aliases: firefox, mozilla",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install firefox -y",
                    Notes = "Alternative web browser",
                    SortOrder = 1,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "vscode",
                    TicketShortDescription = "Visual Studio Code",
                    TicketDescription = "Visual Studio Code editor. Aliases: vscode, vs code, code editor",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install vscode -y",
                    Notes = "Code editor for developers",
                    SortOrder = 2,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "7zip",
                    TicketShortDescription = "7-Zip",
                    TicketDescription = "7-Zip file archiver. Aliases: 7zip, 7-zip, archive tool, zip tool",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install 7zip -y",
                    Notes = "File compression utility",
                    SortOrder = 3,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "notepadplusplus",
                    TicketShortDescription = "Notepad++",
                    TicketDescription = "Notepad++ text editor. Aliases: notepad++, npp, text editor",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install notepadplusplus -y",
                    Notes = "Advanced text editor",
                    SortOrder = 4,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "putty",
                    TicketShortDescription = "PuTTY",
                    TicketDescription = "PuTTY SSH client. Aliases: putty, ssh client, terminal",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install putty -y",
                    Notes = "SSH and telnet client",
                    SortOrder = 5,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "adobereader",
                    TicketShortDescription = "Adobe Acrobat Reader",
                    TicketDescription = "Adobe Acrobat Reader PDF viewer. Aliases: adobe reader, acrobat, pdf reader, pdf viewer",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install adobereader -y",
                    Notes = "PDF document viewer",
                    SortOrder = 6,
                    IsActive = true,
                    ExampleSet = exampleSet
                },
                new Example
                {
                    Name = "vlc",
                    TicketShortDescription = "VLC Media Player",
                    TicketDescription = "VLC media player. Aliases: vlc, media player, video player",
                    ExpectedTicketType = TicketType.SoftwareInstall,
                    ExpectedConfidence = 1.0m,
                    ExpectedTargetResource = "choco install vlc -y",
                    Notes = "Media player",
                    SortOrder = 7,
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
