using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

/// <summary>
/// API endpoints for agent configuration retrieval and heartbeat reporting.
/// Used by Python agents at startup to get their full configuration.
/// </summary>
public static class AgentConfigurationEndpoints
{
    public static void MapAgentConfigurationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/agents")
            .WithTags("Agent Configuration");

        // GET /api/agents/{name}/configuration
        group.MapGet("/{name}/configuration", GetAgentConfiguration)
            .WithName("GetAgentConfiguration")
            .Produces<AgentConfigurationResponse>(StatusCodes.Status200OK)
            .Produces<ConfigurationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ConfigurationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ConfigurationIncompleteResponse>(StatusCodes.Status422UnprocessableEntity);

        // POST /api/agents/{name}/runtime/heartbeat
        group.MapPost("/{name}/runtime/heartbeat", ReportAgentHeartbeat)
            .WithName("ReportAgentRuntimeHeartbeat")
            .Produces<AgentRuntimeHeartbeatResponse>(StatusCodes.Status200OK)
            .Produces<ConfigurationErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAgentConfiguration(
        string name,
        LucidDbContext db,
        ICredentialService credentialService,
        CancellationToken ct = default)
    {
        // Find agent by name, including ServiceAccount relationships
        var agent = await db.Agents
            .Include(a => a.LlmServiceAccount)
            .Include(a => a.ServiceNowAccount)
            .FirstOrDefaultAsync(a => a.Name == name, ct);

        if (agent == null)
        {
            return Results.NotFound(new ConfigurationErrorResponse
            {
                Error = "Agent not found",
                AgentName = name
            });
        }

        if (!agent.IsEnabled)
        {
            return Results.BadRequest(new ConfigurationErrorResponse
            {
                Error = "Agent is disabled",
                AgentName = name
            });
        }

        // Check for incomplete configuration
        var missingConfig = new List<string>();
        if (agent.LlmServiceAccount == null)
            missingConfig.Add("llmProvider");
        if (agent.ServiceNowAccount == null)
            missingConfig.Add("serviceNow");

        if (missingConfig.Any())
        {
            return Results.UnprocessableEntity(new ConfigurationIncompleteResponse
            {
                Error = "Agent configuration incomplete",
                AgentName = name,
                MissingConfiguration = missingConfig
            });
        }

        // Build response with credentials
        var response = new AgentConfigurationResponse
        {
            Agent = new AgentInfo
            {
                Id = agent.Id,
                Name = agent.Name,
                DisplayName = agent.DisplayName,
                Description = agent.Description,
                IsEnabled = agent.IsEnabled
            },
            LlmProvider = await BuildProviderConfigWithCredentials(agent.LlmServiceAccount!, credentialService, ct),
            ServiceNow = await BuildServiceNowConfigWithCredentials(agent.ServiceNowAccount!, credentialService, ct),
            AssignmentGroup = agent.AssignmentGroup
        };

        // Update last activity
        agent.LastActivity = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(response);
    }

    private static async Task<IResult> ReportAgentHeartbeat(
        string name,
        AgentRuntimeHeartbeatRequest request,
        LucidDbContext db)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == name);

        if (agent == null)
        {
            return Results.NotFound(new ConfigurationErrorResponse
            {
                Error = "Agent not found",
                AgentName = name
            });
        }

        // Update agent runtime info
        agent.LastHeartbeat = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.HostName))
        {
            agent.HostName = request.HostName;
        }

        // Parse status string to enum
        if (Enum.TryParse<AgentStatus>(request.Status, true, out var status))
        {
            agent.Status = status;
        }

        if (request.TicketsProcessed.HasValue)
        {
            agent.TicketsProcessed = request.TicketsProcessed.Value;
        }

        await db.SaveChangesAsync();

        return Results.Ok(new AgentRuntimeHeartbeatResponse
        {
            Acknowledged = true,
            ServerTime = DateTime.UtcNow,
            AgentEnabled = agent.IsEnabled
        });
    }

    private static async Task<ProviderConfig> BuildProviderConfigWithCredentials(
        ServiceAccount account,
        ICredentialService credentialService,
        CancellationToken ct)
    {
        // Parse the JSON configuration
        Dictionary<string, object>? configDict = null;
        if (!string.IsNullOrEmpty(account.Configuration))
        {
            try
            {
                configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    account.Configuration,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (JsonException)
            {
                // If parsing fails, return empty config
                configDict = new Dictionary<string, object>();
            }
        }

        // Get credentials
        var credentials = await credentialService.GetCredentialsAsync(account, ct);

        return new ProviderConfig
        {
            ServiceAccountId = account.Id,
            ServiceAccountName = account.Name,
            ProviderType = account.Provider,
            AccountType = account.AccountType,
            Config = configDict ?? new Dictionary<string, object>(),
            Credentials = credentials?.Values ?? new Dictionary<string, string>()
        };
    }

    private static async Task<ServiceNowConfig> BuildServiceNowConfigWithCredentials(
        ServiceAccount account,
        ICredentialService credentialService,
        CancellationToken ct)
    {
        // Parse the JSON configuration
        Dictionary<string, object>? configDict = null;
        if (!string.IsNullOrEmpty(account.Configuration))
        {
            try
            {
                configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    account.Configuration,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (JsonException)
            {
                // If parsing fails, return empty config
                configDict = new Dictionary<string, object>();
            }
        }

        // Get credentials
        var credentials = await credentialService.GetCredentialsAsync(account, ct);

        return new ServiceNowConfig
        {
            ServiceAccountId = account.Id,
            ServiceAccountName = account.Name,
            ProviderType = account.Provider,
            AccountType = account.AccountType,
            Config = configDict ?? new Dictionary<string, object>(),
            CredentialStorage = account.CredentialStorage.ToString().ToLowerInvariant(),
            CredentialReference = account.CredentialReference,
            Credentials = credentials?.Values ?? new Dictionary<string, string>()
        };
    }
}

// === Response Models ===

public class AgentConfigurationResponse
{
    public required AgentInfo Agent { get; set; }
    public required ProviderConfig LlmProvider { get; set; }
    public required ServiceNowConfig ServiceNow { get; set; }
    public string? AssignmentGroup { get; set; }
}

public class AgentInfo
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
}

public class ProviderConfig
{
    public Guid ServiceAccountId { get; set; }
    public required string ServiceAccountName { get; set; }
    public required string ProviderType { get; set; }
    public required string AccountType { get; set; }
    public Dictionary<string, object> Config { get; set; } = new();
    public Dictionary<string, string> Credentials { get; set; } = new();
}

public class ServiceNowConfig
{
    public Guid ServiceAccountId { get; set; }
    public required string ServiceAccountName { get; set; }
    public required string ProviderType { get; set; }
    public required string AccountType { get; set; }
    public Dictionary<string, object> Config { get; set; } = new();
    public required string CredentialStorage { get; set; }
    public string? CredentialReference { get; set; }
    public Dictionary<string, string> Credentials { get; set; } = new();
}

public class ConfigurationErrorResponse
{
    public required string Error { get; set; }
    public required string AgentName { get; set; }
}

public class ConfigurationIncompleteResponse
{
    public required string Error { get; set; }
    public required string AgentName { get; set; }
    public List<string> MissingConfiguration { get; set; } = new();
}

// === Request Models ===

public class AgentRuntimeHeartbeatRequest
{
    public string? HostName { get; set; }
    public string Status { get; set; } = "Unknown";
    public int? TicketsProcessed { get; set; }
}

public class AgentRuntimeHeartbeatResponse
{
    public bool Acknowledged { get; set; }
    public DateTime ServerTime { get; set; }
    public bool AgentEnabled { get; set; }
}
