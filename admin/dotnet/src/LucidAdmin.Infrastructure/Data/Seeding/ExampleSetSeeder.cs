using LucidAdmin.Core.Entities;
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
        var categories = await _context.TicketCategories.ToDictionaryAsync(c => c.Name);

        await SeedPasswordResetExamples(categories);
        await SeedGroupAccessExamples(categories);
        await SeedDispatcherClassificationExamples(categories);
        await SeedSoftwareInstallExamples(categories);
        await SeedSoftwareCatalogExamples(categories);
        await _context.SaveChangesAsync();
    }

    private async Task SeedPasswordResetExamples(Dictionary<string, TicketCategory> categories)
    {
        const string setName = "password-reset-examples";
        var existing = await _context.ExampleSets.Include(e => e.Examples).FirstOrDefaultAsync(e => e.Name == setName);
        if (existing != null) { _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName); return; }

        var exampleSet = new ExampleSet
        {
            Name = setName, DisplayName = "Password Reset Examples",
            Description = "Examples for classifying password reset and account unlock requests",
            TicketCategoryId = categories["password-reset"].Id, IsBuiltIn = true, IsActive = true
        };
        var examples = new List<Example>
        {
            new() { Name = "simple-forgot-password", TicketShortDescription = "I forgot my password", TicketDescription = "I can't remember my password and need to log in.", TicketCategoryId = categories["password-reset"].Id, ExpectedConfidence = 0.95m, Notes = "Simple, clear password reset request", SortOrder = 0, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "account-locked", TicketShortDescription = "My account is locked out", TicketDescription = "I tried logging in too many times and now my account is locked. Please help.", TicketCategoryId = categories["password-reset"].Id, ExpectedConfidence = 0.90m, Notes = "Account lockout - also handled by password reset flow", SortOrder = 1, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "coworker-password-reset", TicketShortDescription = "Password reset for John Smith", TicketDescription = "John Smith (jsmith) forgot his password while on vacation. Can you reset it so I can give him the temp password?", CallerName = "Jane Doe", TicketCategoryId = categories["password-reset"].Id, ExpectedConfidence = 0.85m, ExpectedAffectedUser = "jsmith", Notes = "Third-party request - note lower confidence, affected_user extracted", SortOrder = 2, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "ambiguous-login-issue", TicketShortDescription = "Can't log in", TicketDescription = "I'm having trouble logging in to my computer.", TicketCategoryId = categories["password-reset"].Id, ExpectedConfidence = 0.60m, ExpectedShouldEscalate = true, ExpectedEscalationReason = "Ambiguous request - could be password, network, or hardware issue", Notes = "Low confidence example - should escalate for clarification", SortOrder = 3, IsActive = true, ExampleSet = exampleSet }
        };
        exampleSet.Examples = examples;
        _context.ExampleSets.Add(exampleSet);
        _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples", setName, examples.Count);
    }

    private async Task SeedGroupAccessExamples(Dictionary<string, TicketCategory> categories)
    {
        const string setName = "group-access-examples";
        var existing = await _context.ExampleSets.Include(e => e.Examples).FirstOrDefaultAsync(e => e.Name == setName);
        if (existing != null) { _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName); return; }

        var exampleSet = new ExampleSet
        {
            Name = setName, DisplayName = "Group Access Examples",
            Description = "Examples for classifying AD group membership requests",
            TicketCategoryId = categories["group-membership"].Id, IsBuiltIn = true, IsActive = true
        };
        var examples = new List<Example>
        {
            new() { Name = "add-to-vpn-group", TicketShortDescription = "Need VPN access", TicketDescription = "I'm starting to work remotely and need to be added to the VPN users group.", TicketCategoryId = categories["group-membership"].Id, ExpectedConfidence = 0.90m, ExpectedTargetGroup = "VPN-Users", Notes = "Clear add request with identifiable group", SortOrder = 0, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "add-user-to-team-share", TicketShortDescription = "Add Sarah to Marketing share", TicketDescription = "Please add Sarah Johnson (sjohnson) to the Marketing-ReadWrite group so she can access the team files.", CallerName = "Mike Manager", TicketCategoryId = categories["group-membership"].Id, ExpectedConfidence = 0.95m, ExpectedAffectedUser = "sjohnson", ExpectedTargetGroup = "Marketing-ReadWrite", Notes = "Manager requesting access for team member - clear and complete", SortOrder = 1, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "remove-from-group", TicketShortDescription = "Remove access for terminated employee", TicketDescription = "Bob Wilson (bwilson) has left the company. Please remove him from all groups.", TicketCategoryId = categories["group-membership"].Id, ExpectedConfidence = 0.85m, ExpectedAffectedUser = "bwilson", ExpectedShouldEscalate = true, ExpectedEscalationReason = "Request to remove from ALL groups requires human review", Notes = "Bulk removal should be escalated for verification", SortOrder = 2, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "remove-single-group", TicketShortDescription = "Remove me from Finance-Readonly", TicketDescription = "I transferred to a different department and no longer need access to the Finance folder.", TicketCategoryId = categories["group-membership"].Id, ExpectedConfidence = 0.95m, ExpectedTargetGroup = "Finance-Readonly", Notes = "Self-service removal from specific group", SortOrder = 3, IsActive = true, ExampleSet = exampleSet }
        };
        exampleSet.Examples = examples;
        _context.ExampleSets.Add(exampleSet);
        _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples", setName, examples.Count);
    }

    private async Task SeedDispatcherClassificationExamples(Dictionary<string, TicketCategory> categories)
    {
        const string setName = "it-dispatch-classification";
        var existing = await _context.ExampleSets.Include(e => e.Examples).FirstOrDefaultAsync(e => e.Name == setName);
        if (existing != null)
        {
            if (!existing.Examples.Any(e => e.Name.StartsWith("software-install-")))
            {
                _context.Examples.AddRange(new List<Example>
                {
                    new() { Name = "software-install-standard", TicketShortDescription = "Install software on my computer", TicketDescription = "I need Google Chrome installed on my workstation WS-HR-015.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.92m, Notes = "Standard software install request for dispatcher routing", SortOrder = 10, IsActive = true, ExampleSet = existing },
                    new() { Name = "software-install-vague", TicketShortDescription = "Need new software", TicketDescription = "I need some new software for my job. Can someone help me get it installed?", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.70m, Notes = "Vague software install - lower confidence, still routes to software-install workflow", SortOrder = 11, IsActive = true, ExampleSet = existing }
                });
                _logger.LogInformation("Added software install examples to existing set: {ExampleSetName}", setName);
            }
            else { _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName); }
            return;
        }

        var exampleSet = new ExampleSet
        {
            Name = setName, DisplayName = "IT Dispatch Classification",
            Description = "Multi-type classification examples for the IT dispatcher workflow. Teaches LLM to classify tickets as password-reset, group-membership, file-permissions, or unknown.",
            TicketCategoryId = categories["unknown"].Id, IsBuiltIn = true, IsActive = true
        };
        var examples = new List<Example>
        {
            new() { Name = "password-reset-standard", TicketShortDescription = "Password reset needed", TicketDescription = "User John Smith needs his password reset. He forgot it over the weekend.", TicketCategoryId = categories["password-reset"].Id, ExpectedConfidence = 0.95m, ExpectedAffectedUser = "jsmith", Notes = "Standard password reset request", SortOrder = 0, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "group-membership-add", TicketShortDescription = "Add user to security group", TicketDescription = "Please add user jane.doe to the Finance-Reports security group in Active Directory.", CallerName = "Mike Manager", TicketCategoryId = categories["group-membership"].Id, ExpectedConfidence = 0.92m, ExpectedAffectedUser = "jane.doe", ExpectedTargetGroup = "Finance-Reports", Notes = "Group membership addition", SortOrder = 1, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "file-permissions-read", TicketShortDescription = "Need folder access", TicketDescription = "Sarah Connor needs read access to \\\\fileserver\\shared\\marketing folder.", TicketCategoryId = categories["file-permissions"].Id, ExpectedConfidence = 0.88m, ExpectedAffectedUser = "sconnor", ExpectedTargetResource = "\\\\fileserver\\shared\\marketing", ExpectedPermissionLevel = "read", Notes = "File permissions request", SortOrder = 2, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "unknown-printer-jam", TicketShortDescription = "Printer jammed", TicketDescription = "The printer on the 3rd floor is jammed again.", TicketCategoryId = categories["out-of-scope"].Id, ExpectedConfidence = 0.15m, ExpectedShouldEscalate = true, ExpectedEscalationReason = "Hardware issue - not in agent capabilities", Notes = "Unknown type - should escalate", SortOrder = 3, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "unknown-vpn-issue", TicketShortDescription = "VPN not working", TicketDescription = "I can't connect to the VPN from home.", TicketCategoryId = categories["out-of-scope"].Id, ExpectedConfidence = 0.20m, ExpectedShouldEscalate = true, ExpectedEscalationReason = "VPN connectivity - not in agent capabilities", Notes = "Unknown type - should escalate", SortOrder = 4, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "group-membership-urgent-removal", TicketShortDescription = "Remove terminated employee from IT-Admins", TicketDescription = "Remove user mike.jones from the IT-Admins group immediately - he's been terminated.", TicketCategoryId = categories["group-membership"].Id, ExpectedConfidence = 0.94m, ExpectedAffectedUser = "mike.jones", ExpectedTargetGroup = "IT-Admins", Notes = "Urgent group membership removal", SortOrder = 5, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "software-install-standard", TicketShortDescription = "Install software on my computer", TicketDescription = "I need Google Chrome installed on my workstation WS-HR-015.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.92m, Notes = "Standard software install request for dispatcher routing", SortOrder = 10, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "software-install-vague", TicketShortDescription = "Need new software", TicketDescription = "I need some new software for my job. Can someone help me get it installed?", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.70m, Notes = "Vague software install - lower confidence, still routes to software-install workflow", SortOrder = 11, IsActive = true, ExampleSet = exampleSet }
        };
        exampleSet.Examples = examples;
        _context.ExampleSets.Add(exampleSet);
        _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples", setName, examples.Count);
    }

    private async Task SeedSoftwareInstallExamples(Dictionary<string, TicketCategory> categories)
    {
        const string setName = "software-install-examples";
        var existing = await _context.ExampleSets.Include(e => e.Examples).FirstOrDefaultAsync(e => e.Name == setName);
        if (existing != null) { _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName); return; }

        var exampleSet = new ExampleSet
        {
            Name = setName, DisplayName = "Software Install Examples",
            Description = "Examples for classifying software installation requests",
            TicketCategoryId = categories["software-install"].Id, IsBuiltIn = true, IsActive = true
        };
        var examples = new List<Example>
        {
            new() { Name = "install-chrome-clear", TicketShortDescription = "Need Google Chrome installed", TicketDescription = "I need Google Chrome installed on my laptop YOURPC01. My current browser isn't working well for our web apps.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.95m, Notes = "Clear request with both software and computer identified", SortOrder = 0, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "install-vscode-no-computer", TicketShortDescription = "Please install Visual Studio Code", TicketDescription = "I'm a new developer and need VS Code installed. Can you help?", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.90m, Notes = "Software identified but computer name missing — triggers clarification", SortOrder = 1, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "install-ambiguous-software", TicketShortDescription = "Need a PDF reader", TicketDescription = "I need to be able to read PDF files on my computer YOURPC02.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.75m, Notes = "Computer identified but software ambiguous — multiple catalog matches, triggers clarification", SortOrder = 2, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "install-unauthorized-software", TicketShortDescription = "Install BitTorrent client", TicketDescription = "I need a BitTorrent client installed on YOURPC03 for downloading files.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.85m, ExpectedShouldEscalate = true, ExpectedEscalationReason = "Requested software not in approved catalog", Notes = "Software not in catalog — should be denied/escalated", SortOrder = 3, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "install-multiple-missing-info", TicketShortDescription = "Software installation request", TicketDescription = "I need some software installed on my machine.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.65m, Notes = "Both software and computer missing — needs clarification for both", SortOrder = 4, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "install-from-catalog-exact", TicketShortDescription = "Install 7-Zip on my workstation", TicketDescription = "Please install 7-Zip on workstation WS-DEV-042. I need it for extracting archive files.", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 0.95m, Notes = "Exact catalog match with computer — happy path, no clarification needed", SortOrder = 5, IsActive = true, ExampleSet = exampleSet }
        };
        exampleSet.Examples = examples;
        _context.ExampleSets.Add(exampleSet);
        _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples", setName, examples.Count);
    }

    private async Task SeedSoftwareCatalogExamples(Dictionary<string, TicketCategory> categories)
    {
        const string setName = "approved-software-catalog";
        var existing = await _context.ExampleSets.Include(e => e.Examples).FirstOrDefaultAsync(e => e.Name == setName);
        if (existing != null) { _logger.LogDebug("Built-in example set already exists: {ExampleSetName}", setName); return; }

        var exampleSet = new ExampleSet
        {
            Name = setName, DisplayName = "Approved Software Catalog",
            Description = "Catalog of approved software available for automated installation. Used by the software install workflow to validate and match software requests.",
            TicketCategoryId = categories["software-install"].Id, IsBuiltIn = true, IsActive = true
        };
        var examples = new List<Example>
        {
            new() { Name = "chrome", TicketShortDescription = "Google Chrome", TicketDescription = "Google Chrome web browser. Aliases: chrome, google chrome, web browser", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install googlechrome -y", Notes = "Enterprise web browser", SortOrder = 0, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "firefox", TicketShortDescription = "Mozilla Firefox", TicketDescription = "Mozilla Firefox web browser. Aliases: firefox, mozilla", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install firefox -y", Notes = "Alternative web browser", SortOrder = 1, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "vscode", TicketShortDescription = "Visual Studio Code", TicketDescription = "Visual Studio Code editor. Aliases: vscode, vs code, code editor", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install vscode -y", Notes = "Code editor for developers", SortOrder = 2, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "7zip", TicketShortDescription = "7-Zip", TicketDescription = "7-Zip file archiver. Aliases: 7zip, 7-zip, archive tool, zip tool", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install 7zip -y", Notes = "File compression utility", SortOrder = 3, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "notepadplusplus", TicketShortDescription = "Notepad++", TicketDescription = "Notepad++ text editor. Aliases: notepad++, npp, text editor", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install notepadplusplus -y", Notes = "Advanced text editor", SortOrder = 4, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "putty", TicketShortDescription = "PuTTY", TicketDescription = "PuTTY SSH client. Aliases: putty, ssh client, terminal", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install putty -y", Notes = "SSH and telnet client", SortOrder = 5, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "adobereader", TicketShortDescription = "Adobe Acrobat Reader", TicketDescription = "Adobe Acrobat Reader PDF viewer. Aliases: adobe reader, acrobat, pdf reader, pdf viewer", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install adobereader -y", Notes = "PDF document viewer", SortOrder = 6, IsActive = true, ExampleSet = exampleSet },
            new() { Name = "vlc", TicketShortDescription = "VLC Media Player", TicketDescription = "VLC media player. Aliases: vlc, media player, video player", TicketCategoryId = categories["software-install"].Id, ExpectedConfidence = 1.0m, ExpectedTargetResource = "choco install vlc -y", Notes = "Media player", SortOrder = 7, IsActive = true, ExampleSet = exampleSet }
        };
        exampleSet.Examples = examples;
        _context.ExampleSets.Add(exampleSet);
        _logger.LogInformation("Seeded built-in example set: {ExampleSetName} with {ExampleCount} examples", setName, examples.Count);
    }
}
