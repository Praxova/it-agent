using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// Manages the seal/unseal lifecycle using a two-level key hierarchy:
///   Master Key (MK) — derived from passphrase via Argon2id, never persisted
///   Key Encryption Key (KEK) — encrypted by MK, stored in SystemSecrets
///   Secret Values — encrypted by KEK via EncryptionService
///
/// Registered as singleton — holds KEK in memory for app lifetime.
/// Uses IServiceProvider to create scoped DbContext for database access.
/// </summary>
public class SealManager : ISealManager
{
    private const string KekSecretName = "envelope-kek";
    private const string RecoveryKekSecretName = "envelope-kek-recovery";
    private const int KeySize = 32; // AES-256
    private const int NonceSize = 12; // GCM nonce
    private const int TagSize = 16; // GCM tag
    private const int SaltSize = 32;
    private const int RecoveryKeySize = 16; // 128-bit recovery key

    // Argon2id parameters (matching Argon2PasswordHasher)
    private const int Argon2MemorySize = 128 * 1024; // 128 MB
    private const int Argon2Iterations = 4;
    private const int Argon2Parallelism = 2;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SealManager> _logger;
    private readonly object _lock = new();

    private byte[]? _kek;
    private bool _requiresInitialization;
    private bool _initChecked;
    private bool _wasUnsealedViaRecoveryKey;

    public SealManager(IServiceProvider serviceProvider, ILogger<SealManager> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public bool IsUnsealed
    {
        get { lock (_lock) { return _kek != null; } }
    }

    public bool WasUnsealedViaRecoveryKey
    {
        get { lock (_lock) { return _wasUnsealedViaRecoveryKey; } }
    }

    public bool RequiresInitialization
    {
        get
        {
            if (!_initChecked)
                CheckInitializationState().GetAwaiter().GetResult();
            return _requiresInitialization;
        }
    }

    public async Task<string> InitializeAsync(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty.", nameof(passphrase));

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        // Verify no KEK exists already
        var existing = await db.SystemSecrets.FirstOrDefaultAsync(s => s.Name == KekSecretName);
        if (existing != null)
            throw new InvalidOperationException("Secrets store is already initialized. Use UnsealAsync instead.");

        // 1. Generate random salt
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        // 2. Derive master key from passphrase via Argon2id
        var mk = await DeriveKeyAsync(passphrase, salt);

        // 3. Generate random KEK
        var kek = RandomNumberGenerator.GetBytes(KeySize);

        // 4. Encrypt KEK with MK using AES-256-GCM (passphrase path)
        var (encryptedKek, nonce) = EncryptWithKey(mk, kek);

        // 5. Store passphrase-encrypted KEK in SystemSecrets
        db.SystemSecrets.Add(new SystemSecret
        {
            Name = KekSecretName,
            EncryptedValue = encryptedKek,
            Nonce = nonce,
            Purpose = "Key Encryption Key — protects all secret data",
            Metadata = Convert.ToHexString(salt).ToLowerInvariant()
        });

        // 6. Generate recovery key and encrypt KEK with it (recovery path)
        var recoveryKeyBytes = RandomNumberGenerator.GetBytes(RecoveryKeySize);
        var formattedRecoveryKey = FormatRecoveryKey(recoveryKeyBytes);

        var recoverySalt = RandomNumberGenerator.GetBytes(SaltSize);
        var recoveryDerivedKey = await DeriveKeyAsync(formattedRecoveryKey, recoverySalt);
        var (recoveryEncryptedKek, recoveryNonce) = EncryptWithKey(recoveryDerivedKey, kek);

        db.SystemSecrets.Add(new SystemSecret
        {
            Name = RecoveryKekSecretName,
            EncryptedValue = recoveryEncryptedKek,
            Nonce = recoveryNonce,
            Purpose = "Recovery-encrypted KEK — emergency unseal path",
            Metadata = Convert.ToHexString(recoverySalt).ToLowerInvariant()
        });

        await db.SaveChangesAsync();

        // Zero derived keys — no longer needed
        Array.Clear(mk);
        Array.Clear(recoveryDerivedKey);

        // Hold KEK in memory
        lock (_lock)
        {
            _kek = kek;
            _requiresInitialization = false;
            _initChecked = true;
        }

        _logger.LogInformation("Secrets store initialized — KEK generated with passphrase and recovery key paths");
        return formattedRecoveryKey;
    }

    public async Task<bool> UnsealAsync(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            return false;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        var kekRecord = await db.SystemSecrets.FirstOrDefaultAsync(s => s.Name == KekSecretName);
        if (kekRecord == null)
        {
            _logger.LogWarning("Cannot unseal — no KEK found in database. Initialize first.");
            return false;
        }

        // Extract salt from metadata
        if (string.IsNullOrEmpty(kekRecord.Metadata))
        {
            _logger.LogError("KEK record has no metadata (salt). Database may be corrupted.");
            return false;
        }

        byte[] salt;
        try
        {
            salt = Convert.FromHexString(kekRecord.Metadata);
        }
        catch (FormatException)
        {
            _logger.LogError("KEK salt metadata is not valid hex.");
            return false;
        }

        // Derive MK from passphrase + salt
        var mk = await DeriveKeyAsync(passphrase, salt);

        // Try to decrypt KEK
        try
        {
            var kek = DecryptWithKey(mk, kekRecord.EncryptedValue, kekRecord.Nonce);

            lock (_lock)
            {
                _kek = kek;
                _requiresInitialization = false;
                _initChecked = true;
            }

            _logger.LogInformation("Secrets store unsealed successfully");
            return true;
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("Failed to unseal — incorrect passphrase (GCM authentication failed)");
            return false;
        }
        finally
        {
            Array.Clear(mk);
        }
    }

    public async Task<bool> UnsealWithRecoveryKeyAsync(string recoveryKey)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey))
            return false;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        var recoveryRecord = await db.SystemSecrets.FirstOrDefaultAsync(s => s.Name == RecoveryKekSecretName);
        if (recoveryRecord == null)
        {
            _logger.LogWarning("Cannot unseal via recovery key — no recovery KEK found in database");
            return false;
        }

        if (string.IsNullOrEmpty(recoveryRecord.Metadata))
        {
            _logger.LogError("Recovery KEK record has no metadata (salt). Database may be corrupted.");
            return false;
        }

        byte[] salt;
        try
        {
            salt = Convert.FromHexString(recoveryRecord.Metadata);
        }
        catch (FormatException)
        {
            _logger.LogError("Recovery KEK salt metadata is not valid hex.");
            return false;
        }

        var derivedKey = await DeriveKeyAsync(recoveryKey, salt);

        try
        {
            var kek = DecryptWithKey(derivedKey, recoveryRecord.EncryptedValue, recoveryRecord.Nonce);

            lock (_lock)
            {
                _kek = kek;
                _requiresInitialization = false;
                _initChecked = true;
                _wasUnsealedViaRecoveryKey = true;
            }

            _logger.LogWarning("Secrets store unsealed via RECOVERY KEY — passphrase should be changed");
            return true;
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("Failed to unseal via recovery key — incorrect key (GCM authentication failed)");
            return false;
        }
        finally
        {
            Array.Clear(derivedKey);
        }
    }

    public async Task<string> RegenerateRecoveryKeyAsync()
    {
        byte[] currentKek;
        lock (_lock)
        {
            if (_kek == null)
                throw new InvalidOperationException("Cannot regenerate recovery key — secrets store is sealed.");
            currentKek = new byte[_kek.Length];
            Buffer.BlockCopy(_kek, 0, currentKek, 0, _kek.Length);
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();

        // Generate new recovery key
        var recoveryKeyBytes = RandomNumberGenerator.GetBytes(RecoveryKeySize);
        var formattedRecoveryKey = FormatRecoveryKey(recoveryKeyBytes);

        var recoverySalt = RandomNumberGenerator.GetBytes(SaltSize);
        var recoveryDerivedKey = await DeriveKeyAsync(formattedRecoveryKey, recoverySalt);
        var (recoveryEncryptedKek, recoveryNonce) = EncryptWithKey(recoveryDerivedKey, currentKek);

        // Replace existing recovery record
        var existing = await db.SystemSecrets.FirstOrDefaultAsync(s => s.Name == RecoveryKekSecretName);
        if (existing != null)
        {
            existing.EncryptedValue = recoveryEncryptedKek;
            existing.Nonce = recoveryNonce;
            existing.Metadata = Convert.ToHexString(recoverySalt).ToLowerInvariant();
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.SystemSecrets.Add(new SystemSecret
            {
                Name = RecoveryKekSecretName,
                EncryptedValue = recoveryEncryptedKek,
                Nonce = recoveryNonce,
                Purpose = "Recovery-encrypted KEK — emergency unseal path",
                Metadata = Convert.ToHexString(recoverySalt).ToLowerInvariant()
            });
        }

        await db.SaveChangesAsync();

        Array.Clear(recoveryDerivedKey);
        Array.Clear(currentKek);

        _logger.LogInformation("Recovery key regenerated");
        return formattedRecoveryKey;
    }

    public void Seal()
    {
        lock (_lock)
        {
            if (_kek != null)
            {
                Array.Clear(_kek);
                _kek = null;
            }
            _wasUnsealedViaRecoveryKey = false;
        }

        _logger.LogInformation("Secrets store sealed — KEK cleared from memory");
    }

    public byte[] GetEncryptionKey()
    {
        lock (_lock)
        {
            if (_kek == null)
                throw new InvalidOperationException(
                    "Secrets store is sealed. Provide the master passphrase via " +
                    "POST /api/v1/system/unseal or set PRAXOVA_UNSEAL_PASSPHRASE.");

            // Return a copy to prevent external callers from zeroing our key
            var copy = new byte[_kek.Length];
            Buffer.BlockCopy(_kek, 0, copy, 0, _kek.Length);
            return copy;
        }
    }

    private async Task CheckInitializationState()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
            var exists = await db.SystemSecrets.AnyAsync(s => s.Name == KekSecretName);
            _requiresInitialization = !exists;
            _initChecked = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check KEK initialization state (database may not be ready)");
            _requiresInitialization = true;
            _initChecked = true;
        }
    }

    /// <summary>
    /// Derives a 32-byte AES-256 key from passphrase + salt using Argon2id.
    /// </summary>
    private static async Task<byte[]> DeriveKeyAsync(string passphrase, byte[] salt)
    {
        return await Task.Run(() =>
        {
            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
            {
                Salt = salt,
                DegreeOfParallelism = Argon2Parallelism,
                MemorySize = Argon2MemorySize,
                Iterations = Argon2Iterations
            };

            return argon2.GetBytes(KeySize);
        });
    }

    /// <summary>
    /// Encrypts data with a given key using AES-256-GCM.
    /// Uses raw AesGcm directly (not EncryptionService, to avoid circular dependency).
    /// </summary>
    private static (byte[] CiphertextWithTag, byte[] Nonce) EncryptWithKey(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Combine ciphertext + tag (same format as EncryptionService)
        var result = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);

        return (result, nonce);
    }

    /// <summary>
    /// Decrypts data with a given key using AES-256-GCM.
    /// Throws CryptographicException if the key is wrong (GCM auth tag mismatch).
    /// </summary>
    private static byte[] DecryptWithKey(byte[] key, byte[] ciphertextWithTag, byte[] nonce)
    {
        if (ciphertextWithTag.Length < TagSize)
            throw new ArgumentException("Ciphertext too short", nameof(ciphertextWithTag));

        var ciphertext = new byte[ciphertextWithTag.Length - TagSize];
        var tag = new byte[TagSize];

        Buffer.BlockCopy(ciphertextWithTag, 0, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(ciphertextWithTag, ciphertext.Length, tag, 0, TagSize);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Formats raw bytes as a XXXX-XXXX-XXXX-XXXX recovery key string.
    /// </summary>
    private static string FormatRecoveryKey(byte[] keyBytes)
    {
        var hex = Convert.ToHexString(keyBytes).ToUpperInvariant();
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..24]}-{hex[24..28]}-{hex[28..32]}";
    }
}
