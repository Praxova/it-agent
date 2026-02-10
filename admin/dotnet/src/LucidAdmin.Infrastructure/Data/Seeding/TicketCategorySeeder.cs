using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Data.Seeding;

/// <summary>
/// Seeds built-in ticket categories that replace the former TicketType enum.
/// Must run BEFORE ExampleSetSeeder and WorkflowSeeder.
/// </summary>
public class TicketCategorySeeder
{
    private readonly LucidDbContext _context;
    private readonly ILogger<TicketCategorySeeder> _logger;

    public TicketCategorySeeder(LucidDbContext context, ILogger<TicketCategorySeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        var categories = new List<(string Name, string DisplayName, string Color, int SortOrder)>
        {
            ("password-reset", "Password Reset", "#2196F3", 0),
            ("group-membership", "Group Membership", "#4CAF50", 1),
            ("file-permissions", "File Permissions", "#FF9800", 2),
            ("software-install", "Software Install", "#9C27B0", 3),
            ("unknown", "Unknown", "#9E9E9E", 4),
            ("out-of-scope", "Out of Scope", "#F44336", 5),
        };

        foreach (var (name, displayName, color, sortOrder) in categories)
        {
            var existing = await _context.TicketCategories.FirstOrDefaultAsync(c => c.Name == name);
            if (existing == null)
            {
                _context.TicketCategories.Add(new TicketCategory
                {
                    Name = name,
                    DisplayName = displayName,
                    Color = color,
                    IsBuiltIn = true,
                    IsActive = true,
                    SortOrder = sortOrder
                });
                _logger.LogInformation("Seeded built-in ticket category: {CategoryName}", name);
            }
            else
            {
                _logger.LogDebug("Built-in ticket category already exists: {CategoryName}", name);
            }
        }

        await _context.SaveChangesAsync();
    }
}
