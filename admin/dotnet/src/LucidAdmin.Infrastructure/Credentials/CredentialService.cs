using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Credentials;

/// <summary>
/// High-level credential service that orchestrates providers
/// </summary>
public class CredentialService : ICredentialService
{
    private readonly ICredentialProviderRegistry _registry;
    private readonly IServiceAccountRepository _repository;
    private readonly IAuditEventRepository _auditRepository;
    private readonly ILogger<CredentialService> _logger;

    public CredentialService(
        ICredentialProviderRegistry registry,
        IServiceAccountRepository repository,
        IAuditEventRepository auditRepository,
        ILogger<CredentialService> logger)
    {
        _registry = registry;
        _repository = repository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<CredentialStoreResult> StoreCredentialsAsync(
        Guid serviceAccountId,
        CredentialSet credentials,
        CancellationToken ct = default)
    {
        var account = await _repository.GetByIdAsync(serviceAccountId, ct);
        if (account == null)
        {
            return CredentialStoreResult.Failed($"Service account {serviceAccountId} not found");
        }

        var provider = _registry.GetProvider(account.CredentialStorage);
        if (provider == null)
        {
            return CredentialStoreResult.Failed($"Credential provider not found: {account.CredentialStorage}");
        }

        if (!provider.IsAvailable)
        {
            return CredentialStoreResult.Failed($"Credential provider '{provider.DisplayName}' is not available");
        }

        if (!provider.SupportsStorage)
        {
            return CredentialStoreResult.Failed($"Credential provider '{provider.DisplayName}' is read-only");
        }

        var result = await provider.StoreAsync(serviceAccountId, credentials, account.CredentialReference, ct);

        // Audit log
        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.CredentialUpdated,
            PerformedBy = "System", // TODO: Get from current user context
            TargetResource = account.Name,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                action = "store-credentials",
                provider = provider.ProviderId,
                accountId = serviceAccountId
            })
        }, ct);

        return result;
    }

    public async Task<CredentialSet?> GetCredentialsAsync(
        Guid serviceAccountId,
        CancellationToken ct = default)
    {
        var account = await _repository.GetByIdAsync(serviceAccountId, ct);
        if (account == null)
        {
            _logger.LogWarning("Service account {AccountId} not found", serviceAccountId);
            return null;
        }

        return await GetCredentialsAsync(account, ct);
    }

    public async Task<CredentialSet?> GetCredentialsAsync(
        ServiceAccount account,
        CancellationToken ct = default)
    {
        var provider = _registry.GetProvider(account.CredentialStorage);
        if (provider == null)
        {
            _logger.LogWarning("Credential provider not found: {StorageType}", account.CredentialStorage);
            return null;
        }

        if (!provider.IsAvailable)
        {
            _logger.LogWarning("Credential provider '{Provider}' is not available", provider.DisplayName);
            return null;
        }

        var credentials = await provider.RetrieveAsync(account.Id, account.CredentialReference, ct);

        // Audit log for credential access
        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.CredentialAccessed,
            PerformedBy = "System", // TODO: Get from current user context
            TargetResource = account.Name,
            Success = credentials != null,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                action = "retrieve-credentials",
                provider = provider.ProviderId,
                accountId = account.Id
            })
        }, ct);

        return credentials;
    }

    public async Task<bool> DeleteCredentialsAsync(
        Guid serviceAccountId,
        CancellationToken ct = default)
    {
        var account = await _repository.GetByIdAsync(serviceAccountId, ct);
        if (account == null)
        {
            return false;
        }

        var provider = _registry.GetProvider(account.CredentialStorage);
        if (provider == null)
        {
            return false;
        }

        var result = await provider.DeleteAsync(serviceAccountId, account.CredentialReference, ct);

        // Audit log
        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.CredentialDeleted,
            PerformedBy = "System", // TODO: Get from current user context
            TargetResource = account.Name,
            Success = result,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                action = "delete-credentials",
                provider = provider.ProviderId,
                accountId = serviceAccountId
            })
        }, ct);

        return result;
    }

    public async Task<bool> HasCredentialsAsync(
        Guid serviceAccountId,
        CancellationToken ct = default)
    {
        var account = await _repository.GetByIdAsync(serviceAccountId, ct);
        if (account == null)
        {
            return false;
        }

        // For None storage, there are no credentials
        if (account.CredentialStorage == CredentialStorageType.None)
        {
            return true; // "Has credentials" in the sense that none are needed
        }

        // For Database storage, check if encrypted credentials exist
        if (account.CredentialStorage == CredentialStorageType.Database)
        {
            return account.EncryptedCredentials != null && account.CredentialNonce != null;
        }

        // For Environment storage, check if the reference exists
        if (account.CredentialStorage == CredentialStorageType.Environment)
        {
            if (string.IsNullOrEmpty(account.CredentialReference))
            {
                return false;
            }
            // Check if env var is actually set
            var envVarName = account.CredentialReference.Split(',')[0].Split(':')[0].Trim();
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVarName));
        }

        // For other providers, try to retrieve (not ideal but works for now)
        var credentials = await GetCredentialsAsync(account, ct);
        return credentials != null && !credentials.IsEmpty;
    }
}
