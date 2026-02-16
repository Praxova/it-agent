using System.Security.Cryptography;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// Manages the JWT signing key by storing it encrypted in the SystemSecrets table.
/// On first startup, generates a 64-byte random key. On subsequent startups,
/// retrieves and decrypts the existing key. Registered as singleton — uses
/// IServiceProvider to create a scoped DbContext for initialization.
/// </summary>
public class JwtKeyManager : IJwtKeyManager
{
    private const string SecretName = "jwt-signing-key";
    private const int KeySizeBytes = 64;

    private readonly IServiceProvider _serviceProvider;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<JwtKeyManager> _logger;
    private byte[]? _signingKey;

    public JwtKeyManager(
        IServiceProvider serviceProvider,
        IEncryptionService encryptionService,
        ILogger<JwtKeyManager> logger)
    {
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public byte[] GetSigningKey()
    {
        return _signingKey ?? throw new InvalidOperationException(
            "JWT key manager has not been initialized. Call InitializeAsync at startup.");
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (!_encryptionService.IsConfigured)
        {
            _logger.LogWarning(
                "Secrets store is sealed — JWT signing key cannot be accessed. " +
                "Unseal via POST /api/v1/system/unseal or set PRAXOVA_UNSEAL_PASSPHRASE.");
            throw new InvalidOperationException(
                "Secrets store must be unsealed before JWT key manager can initialize.");
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        var existing = await dbContext.SystemSecrets
            .FirstOrDefaultAsync(s => s.Name == SecretName);

        if (existing != null)
        {
            _signingKey = _encryptionService.Decrypt(existing.EncryptedValue, existing.Nonce);
            _logger.LogInformation(
                "Loaded existing JWT signing key from database (created {CreatedAt})",
                existing.CreatedAt);
        }
        else
        {
            _signingKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
            var (ciphertext, nonce) = _encryptionService.Encrypt(_signingKey);

            dbContext.SystemSecrets.Add(new SystemSecret
            {
                Name = SecretName,
                EncryptedValue = ciphertext,
                Nonce = nonce,
                Purpose = "HMAC-SHA256 signing key for JWT bearer tokens"
            });

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Generated new JWT signing key and stored encrypted in database");
        }
    }
}
