using System.Text.Json;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Credentials;

/// <summary>
/// Stores credentials encrypted in the Admin Portal database
/// </summary>
public class DatabaseCredentialProvider : ICredentialProvider
{
    private readonly IServiceAccountRepository _repository;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<DatabaseCredentialProvider> _logger;

    public DatabaseCredentialProvider(
        IServiceAccountRepository repository,
        IEncryptionService encryptionService,
        ILogger<DatabaseCredentialProvider> logger)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public string ProviderId => "Database";
    public string DisplayName => "Database (Encrypted)";
    public string Description => "Store credentials encrypted in the Admin Portal database using AES-256-GCM";
    public bool IsAvailable => _encryptionService.IsConfigured;
    public bool SupportsStorage => true;

    public async Task<CredentialStoreResult> StoreAsync(
        Guid accountId,
        CredentialSet credentials,
        string? reference = null,
        CancellationToken ct = default)
    {
        if (!_encryptionService.IsConfigured)
        {
            return CredentialStoreResult.Failed("Encryption service not configured. Set LUCID_KEY_FILE or LUCID_ENCRYPTION_KEY.");
        }

        try
        {
            var account = await _repository.GetByIdAsync(accountId, ct);
            if (account == null)
            {
                return CredentialStoreResult.Failed($"Service account {accountId} not found");
            }

            // Serialize credentials to JSON
            var json = JsonSerializer.Serialize(credentials.Values);

            // Encrypt
            var (ciphertext, nonce) = _encryptionService.Encrypt(json);

            // Update account
            account.EncryptedCredentials = ciphertext;
            account.CredentialNonce = nonce;
            account.CredentialsUpdatedAt = DateTime.UtcNow;
            account.CredentialStorage = CredentialStorageType.Database;

            await _repository.UpdateAsync(account, ct);

            _logger.LogInformation("Stored encrypted credentials for service account {AccountId}", accountId);
            return CredentialStoreResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store credentials for service account {AccountId}", accountId);
            return CredentialStoreResult.Failed($"Failed to store credentials: {ex.Message}");
        }
    }

    public async Task<CredentialSet?> RetrieveAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default)
    {
        if (!_encryptionService.IsConfigured)
        {
            _logger.LogWarning("Cannot retrieve credentials: encryption service not configured");
            return null;
        }

        try
        {
            var account = await _repository.GetByIdAsync(accountId, ct);
            if (account == null)
            {
                _logger.LogWarning("Service account {AccountId} not found", accountId);
                return null;
            }

            if (account.EncryptedCredentials == null || account.CredentialNonce == null)
            {
                _logger.LogDebug("No encrypted credentials stored for service account {AccountId}", accountId);
                return null;
            }

            // Decrypt
            var json = _encryptionService.DecryptToString(account.EncryptedCredentials, account.CredentialNonce);

            // Deserialize
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (values == null)
            {
                _logger.LogWarning("Failed to deserialize credentials for service account {AccountId}", accountId);
                return null;
            }

            return new CredentialSet(values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve credentials for service account {AccountId}", accountId);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid accountId,
        string? reference = null,
        CancellationToken ct = default)
    {
        try
        {
            var account = await _repository.GetByIdAsync(accountId, ct);
            if (account == null)
            {
                return false;
            }

            account.EncryptedCredentials = null;
            account.CredentialNonce = null;
            account.CredentialsUpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(account, ct);

            _logger.LogInformation("Deleted credentials for service account {AccountId}", accountId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete credentials for service account {AccountId}", accountId);
            return false;
        }
    }

    public ValidationResult ValidateReference(string? reference)
    {
        // Database provider doesn't use reference - accountId is sufficient
        return ValidationResult.Success();
    }

    public Task<HealthCheckResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        if (!_encryptionService.IsConfigured)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Encryption key not configured. Set LUCID_KEY_FILE environment variable to the path of your encryption key file."));
        }

        // Test encryption round-trip
        try
        {
            var testData = "credential-provider-test";
            var (ciphertext, nonce) = _encryptionService.Encrypt(testData);
            var decrypted = _encryptionService.DecryptToString(ciphertext, nonce);

            if (decrypted != testData)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Encryption round-trip test failed"));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Database credential storage is ready"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Encryption test failed: {ex.Message}"));
        }
    }
}
