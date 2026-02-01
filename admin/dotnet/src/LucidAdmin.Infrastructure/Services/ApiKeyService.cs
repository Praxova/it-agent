using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

public class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyRepository _keyRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IToolServerRepository _toolServerRepository;
    private readonly IAuditEventRepository _auditRepository;
    private readonly ILogger<ApiKeyService> _logger;

    private const string KeyPrefix = "lk_";
    private const int KeyLength = 32; // Characters after prefix

    public ApiKeyService(
        IApiKeyRepository keyRepository,
        IAgentRepository agentRepository,
        IToolServerRepository toolServerRepository,
        IAuditEventRepository auditRepository,
        ILogger<ApiKeyService> logger)
    {
        _keyRepository = keyRepository;
        _agentRepository = agentRepository;
        _toolServerRepository = toolServerRepository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<ApiKeyCreateResult> CreateAgentKeyAsync(
        Guid agentId,
        string name,
        string? description = null,
        DateTime? expiresAt = null,
        CancellationToken ct = default)
    {
        var agent = await _agentRepository.GetByIdAsync(agentId, ct);
        if (agent == null)
        {
            return ApiKeyCreateResult.Failed($"Agent {agentId} not found");
        }

        // Generate the plaintext key
        var plaintextKey = GenerateKey();
        var keyHash = HashKey(plaintextKey);
        var keyPrefixDisplay = plaintextKey[..(KeyPrefix.Length + 8)] + "...";

        // Build allowed service accounts list from agent configuration
        var allowedAccounts = new List<Guid>();
        if (agent.LlmServiceAccountId.HasValue)
            allowedAccounts.Add(agent.LlmServiceAccountId.Value);
        if (agent.ServiceNowAccountId.HasValue)
            allowedAccounts.Add(agent.ServiceNowAccountId.Value);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            KeyHash = keyHash,
            KeyPrefix = keyPrefixDisplay,
            Role = UserRole.Agent,
            AgentId = agentId,
            IsActive = true,
            ExpiresAt = expiresAt,
            AllowedServiceAccountIds = JsonSerializer.Serialize(allowedAccounts),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _keyRepository.AddAsync(apiKey, ct);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ApiKeyCreated,
            PerformedBy = "System", // TODO: Get from current user
            TargetResource = $"Agent:{agent.Name}",
            Success = true,
            DetailsJson = JsonSerializer.Serialize(new
            {
                keyId = apiKey.Id,
                keyPrefix = keyPrefixDisplay,
                agentId,
                role = UserRole.Agent.ToString()
            })
        }, ct);

        _logger.LogInformation("Created API key {KeyPrefix} for agent {AgentName}", keyPrefixDisplay, agent.Name);

        return ApiKeyCreateResult.Succeeded(apiKey, plaintextKey);
    }

    public async Task<ApiKeyCreateResult> CreateToolServerKeyAsync(
        Guid toolServerId,
        string name,
        string? description = null,
        DateTime? expiresAt = null,
        CancellationToken ct = default)
    {
        var toolServer = await _toolServerRepository.GetByIdAsync(toolServerId, ct);
        if (toolServer == null)
        {
            return ApiKeyCreateResult.Failed($"Tool server {toolServerId} not found");
        }

        var plaintextKey = GenerateKey();
        var keyHash = HashKey(plaintextKey);
        var keyPrefixDisplay = plaintextKey[..(KeyPrefix.Length + 8)] + "...";

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            KeyHash = keyHash,
            KeyPrefix = keyPrefixDisplay,
            Role = UserRole.ToolServer,
            ToolServerId = toolServerId,
            IsActive = true,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _keyRepository.AddAsync(apiKey, ct);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ApiKeyCreated,
            PerformedBy = "System",
            TargetResource = $"ToolServer:{toolServer.Name}",
            Success = true,
            DetailsJson = JsonSerializer.Serialize(new
            {
                keyId = apiKey.Id,
                keyPrefix = keyPrefixDisplay,
                toolServerId,
                role = UserRole.ToolServer.ToString()
            })
        }, ct);

        _logger.LogInformation("Created API key {KeyPrefix} for tool server {ServerName}", keyPrefixDisplay, toolServer.Name);

        return ApiKeyCreateResult.Succeeded(apiKey, plaintextKey);
    }

    public async Task<ApiKeyValidationResult> ValidateKeyAsync(
        string plaintextKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plaintextKey))
        {
            return ApiKeyValidationResult.Invalid("API key is required");
        }

        if (!plaintextKey.StartsWith(KeyPrefix))
        {
            return ApiKeyValidationResult.Invalid("Invalid API key format");
        }

        var keyHash = HashKey(plaintextKey);
        var apiKey = await _keyRepository.GetByHashAsync(keyHash, ct);

        if (apiKey == null)
        {
            return ApiKeyValidationResult.Invalid("API key not found");
        }

        if (!apiKey.IsActive)
        {
            return ApiKeyValidationResult.Invalid("API key is disabled");
        }

        if (apiKey.RevokedAt != null)
        {
            return ApiKeyValidationResult.Invalid("API key has been revoked");
        }

        if (apiKey.ExpiresAt != null && apiKey.ExpiresAt <= DateTime.UtcNow)
        {
            return ApiKeyValidationResult.Invalid("API key has expired");
        }

        return ApiKeyValidationResult.Valid(apiKey);
    }

    public async Task<bool> RevokeKeyAsync(
        Guid keyId,
        string revokedBy,
        string? reason = null,
        CancellationToken ct = default)
    {
        var apiKey = await _keyRepository.GetByIdAsync(keyId, ct);
        if (apiKey == null)
        {
            return false;
        }

        apiKey.RevokedAt = DateTime.UtcNow;
        apiKey.RevokedBy = revokedBy;
        apiKey.RevocationReason = reason;
        apiKey.IsActive = false;
        apiKey.UpdatedAt = DateTime.UtcNow;

        await _keyRepository.UpdateAsync(apiKey, ct);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ApiKeyRevoked,
            PerformedBy = revokedBy,
            TargetResource = apiKey.KeyPrefix,
            Success = true,
            DetailsJson = JsonSerializer.Serialize(new
            {
                keyId,
                reason,
                agentId = apiKey.AgentId,
                toolServerId = apiKey.ToolServerId
            })
        }, ct);

        _logger.LogInformation("Revoked API key {KeyPrefix} by {RevokedBy}: {Reason}",
            apiKey.KeyPrefix, revokedBy, reason ?? "No reason provided");

        return true;
    }

    public async Task<IEnumerable<ApiKey>> GetAllKeysAsync(CancellationToken ct = default)
    {
        return await _keyRepository.GetAllAsync(ct);
    }

    public async Task<IEnumerable<ApiKey>> GetAgentKeysAsync(Guid agentId, CancellationToken ct = default)
    {
        return await _keyRepository.GetByAgentIdAsync(agentId, ct);
    }

    public async Task<IEnumerable<ApiKey>> GetToolServerKeysAsync(Guid toolServerId, CancellationToken ct = default)
    {
        return await _keyRepository.GetByToolServerIdAsync(toolServerId, ct);
    }

    public async Task UpdateLastUsedAsync(Guid keyId, string? ipAddress = null, CancellationToken ct = default)
    {
        var apiKey = await _keyRepository.GetByIdAsync(keyId, ct);
        if (apiKey != null)
        {
            apiKey.LastUsedAt = DateTime.UtcNow;
            apiKey.LastUsedFromIp = ipAddress;
            apiKey.UpdatedAt = DateTime.UtcNow;
            await _keyRepository.UpdateAsync(apiKey, ct);
        }
    }

    public bool IsServiceAccountAllowed(ApiKey agentKey, Guid serviceAccountId)
    {
        if (agentKey.Role != UserRole.Agent)
        {
            return false;
        }

        if (string.IsNullOrEmpty(agentKey.AllowedServiceAccountIds))
        {
            return false;
        }

        try
        {
            var allowedIds = JsonSerializer.Deserialize<List<Guid>>(agentKey.AllowedServiceAccountIds);
            return allowedIds?.Contains(serviceAccountId) ?? false;
        }
        catch
        {
            return false;
        }
    }

    public async Task UpdateAllowedServiceAccountsAsync(
        Guid keyId,
        IEnumerable<Guid> serviceAccountIds,
        CancellationToken ct = default)
    {
        var apiKey = await _keyRepository.GetByIdAsync(keyId, ct);
        if (apiKey == null)
        {
            return;
        }

        apiKey.AllowedServiceAccountIds = JsonSerializer.Serialize(serviceAccountIds.ToList());
        apiKey.UpdatedAt = DateTime.UtcNow;
        await _keyRepository.UpdateAsync(apiKey, ct);

        _logger.LogInformation("Updated allowed service accounts for API key {KeyPrefix}", apiKey.KeyPrefix);
    }

    private static string GenerateKey()
    {
        var bytes = new byte[KeyLength];
        RandomNumberGenerator.Fill(bytes);
        var base64 = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return KeyPrefix + base64[..KeyLength];
    }

    private static string HashKey(string plaintextKey)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintextKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
