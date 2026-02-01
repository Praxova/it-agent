using LucidAdmin.Core.Enums;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

/// <summary>
/// API endpoints for capability routing - allows agents to discover which Tool Servers
/// provide specific capabilities.
/// </summary>
public static class CapabilityRoutingEndpoints
{
    public static void MapCapabilityRoutingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/capabilities")
            .WithTags("Capability Routing");

        // GET /api/capabilities - List all capabilities
        group.MapGet("/", ListCapabilities)
            .WithName("ListCapabilities")
            .Produces<CapabilityListResponse>(StatusCodes.Status200OK);

        // GET /api/capabilities/{name}/servers - Get servers for a capability
        group.MapGet("/{name}/servers", GetServersForCapability)
            .WithName("GetServersForCapability")
            .Produces<CapabilityServersResponse>(StatusCodes.Status200OK)
            .Produces<CapabilityErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListCapabilities(LucidDbContext db)
    {
        var capabilities = await db.Capabilities
            .Include(c => c.Mappings)
                .ThenInclude(cm => cm.ToolServer)
            .OrderBy(c => c.Category)
            .ThenBy(c => c.CapabilityId)
            .ToListAsync();

        var response = new CapabilityListResponse
        {
            Capabilities = capabilities.Select(c => new CapabilitySummary
            {
                Id = c.Id,
                Name = c.CapabilityId,
                DisplayName = c.DisplayName,
                Description = c.Description,
                Category = c.Category,
                ServerCount = c.Mappings.Count(cm => cm.IsEnabled),
                OnlineServerCount = c.Mappings.Count(cm =>
                    cm.IsEnabled &&
                    cm.ToolServer != null &&
                    cm.ToolServer.IsEnabled &&
                    cm.ToolServer.Status == HealthStatus.Healthy)
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> GetServersForCapability(
        string name,
        string? status,
        LucidDbContext db)
    {
        // First, check if the capability exists
        var capability = await db.Capabilities
            .FirstOrDefaultAsync(c => c.CapabilityId == name);

        if (capability == null)
        {
            return Results.NotFound(new CapabilityErrorResponse
            {
                Error = "Capability not found",
                CapabilityName = name
            });
        }

        // Parse status filter (default to Healthy only)
        var statusFilter = ParseStatusFilter(status);

        // Query for Tool Servers that provide this capability
        var query = db.CapabilityMappings
            .Include(cm => cm.ToolServer)
            .Include(cm => cm.Capability)
            .Where(cm => cm.Capability!.CapabilityId == name)
            .Where(cm => cm.IsEnabled)
            .Where(cm => cm.ToolServer != null && cm.ToolServer.IsEnabled);

        // Apply status filter
        if (statusFilter != null)
        {
            query = query.Where(cm => cm.ToolServer!.Status == statusFilter.Value);
        }

        // Order by most recently seen first (as a proxy for "best available")
        var mappings = await query
            .OrderByDescending(cm => cm.ToolServer!.LastHeartbeat)
            .ToListAsync();

        var response = new CapabilityServersResponse
        {
            Capability = name,
            Servers = mappings.Select(cm => new ToolServerInfo
            {
                Id = cm.ToolServer!.Id,
                Name = cm.ToolServer.Name,
                DisplayName = cm.ToolServer.DisplayName,
                Url = cm.ToolServer.Endpoint,
                Domain = cm.ToolServer.Domain,
                Status = cm.ToolServer.Status.ToString(),
                LastHeartbeat = cm.ToolServer.LastHeartbeat
            }).ToList(),
            TotalCount = mappings.Count
        };

        return Results.Ok(response);
    }

    private static HealthStatus? ParseStatusFilter(string? status)
    {
        if (string.IsNullOrEmpty(status) || status.Equals("online", StringComparison.OrdinalIgnoreCase) || status.Equals("healthy", StringComparison.OrdinalIgnoreCase))
        {
            return HealthStatus.Healthy;
        }

        if (status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null; // No filter
        }

        if (Enum.TryParse<HealthStatus>(status, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        // Default to Healthy if invalid value
        return HealthStatus.Healthy;
    }
}

// === Response Models ===

public class CapabilityListResponse
{
    public List<CapabilitySummary> Capabilities { get; set; } = new();
}

public class CapabilitySummary
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int ServerCount { get; set; }
    public int OnlineServerCount { get; set; }
}

public class CapabilityServersResponse
{
    public required string Capability { get; set; }
    public List<ToolServerInfo> Servers { get; set; } = new();
    public int TotalCount { get; set; }
}

public class ToolServerInfo
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public required string Url { get; set; }
    public required string Domain { get; set; }
    public required string Status { get; set; }
    public DateTime? LastHeartbeat { get; set; }
}

public class CapabilityErrorResponse
{
    public required string Error { get; set; }
    public required string CapabilityName { get; set; }
}
