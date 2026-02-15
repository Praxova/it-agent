using System.Security.Cryptography;
using System.Text;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// AES-256-GCM encryption service with key file support
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly byte[]? _key;
    private readonly ILogger<EncryptionService> _logger;
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16; // 128 bits for GCM

    public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
    {
        _logger = logger;
        _key = LoadEncryptionKey(configuration);

        if (_key == null)
        {
            _logger.LogWarning("Encryption key not configured. Database credential storage will not be available.");
        }
        else
        {
            _logger.LogInformation("Encryption service initialized successfully");
        }
    }

    public bool IsConfigured => _key != null;

    public (byte[] CipherText, byte[] Nonce) Encrypt(byte[] plaintext)
    {
        if (_key == null)
            throw new InvalidOperationException("Encryption key not configured. Set PRAXOVA_KEY_FILE or PRAXOVA_ENCRYPTION_KEY environment variable.");

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Combine ciphertext and tag for storage
        var result = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);

        return (result, nonce);
    }

    public (byte[] CipherText, byte[] Nonce) Encrypt(string plaintext)
    {
        return Encrypt(Encoding.UTF8.GetBytes(plaintext));
    }

    public byte[] Decrypt(byte[] ciphertextWithTag, byte[] nonce)
    {
        if (_key == null)
            throw new InvalidOperationException("Encryption key not configured.");

        if (ciphertextWithTag.Length < TagSize)
            throw new ArgumentException("Ciphertext too short", nameof(ciphertextWithTag));

        var ciphertext = new byte[ciphertextWithTag.Length - TagSize];
        var tag = new byte[TagSize];

        Buffer.BlockCopy(ciphertextWithTag, 0, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(ciphertextWithTag, ciphertext.Length, tag, 0, TagSize);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    public string DecryptToString(byte[] ciphertextWithTag, byte[] nonce)
    {
        var plaintext = Decrypt(ciphertextWithTag, nonce);
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[]? LoadEncryptionKey(IConfiguration configuration)
    {
        // Try key file first
        var keyFilePath = GetKeyFilePath(configuration);
        if (!string.IsNullOrEmpty(keyFilePath) && File.Exists(keyFilePath))
        {
            try
            {
                var keyBytes = LoadKeyFromFile(keyFilePath);
                if (keyBytes != null)
                {
                    _logger.LogInformation("Loaded encryption key from file: {Path}", keyFilePath);
                    return keyBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load encryption key from file: {Path}", keyFilePath);
            }
        }

        // Try environment variable fallback
        var envKey = Environment.GetEnvironmentVariable("PRAXOVA_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            try
            {
                var keyBytes = ParseKeyString(envKey);
                if (keyBytes != null)
                {
                    _logger.LogInformation("Loaded encryption key from PRAXOVA_ENCRYPTION_KEY environment variable");
                    return keyBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse PRAXOVA_ENCRYPTION_KEY environment variable");
            }
        }

        return null;
    }

    private string? GetKeyFilePath(IConfiguration configuration)
    {
        // Check explicit configuration first
        var configuredPath = configuration["Encryption:KeyFile"]
            ?? Environment.GetEnvironmentVariable("PRAXOVA_KEY_FILE");

        if (!string.IsNullOrEmpty(configuredPath))
        {
            return configuredPath;
        }

        // Check default locations based on OS
        var defaultPaths = GetDefaultKeyPaths();
        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static List<string> GetDefaultKeyPaths()
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            paths.Add(@"C:\ProgramData\Praxova\encryption.key");
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths.Add(Path.Combine(userProfile, ".praxova", "encryption.key"));
        }
        else
        {
            // Linux/macOS
            paths.Add("/etc/praxova/encryption.key");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths.Add(Path.Combine(home, ".praxova", "encryption.key"));
        }

        // Current directory fallback (for development)
        paths.Add(Path.Combine(Directory.GetCurrentDirectory(), "keys", "encryption.key"));

        return paths;
    }

    private byte[]? LoadKeyFromFile(string path)
    {
        var content = File.ReadAllText(path).Trim();
        return ParseKeyString(content);
    }

    private byte[]? ParseKeyString(string keyString)
    {
        // Support both hex and base64 formats
        byte[] keyBytes;

        // Try hex first (64 characters for 32 bytes)
        if (keyString.Length == 64 && keyString.All(c => Uri.IsHexDigit(c)))
        {
            keyBytes = Convert.FromHexString(keyString);
        }
        // Try base64
        else
        {
            try
            {
                keyBytes = Convert.FromBase64String(keyString);
            }
            catch (FormatException)
            {
                // Treat as passphrase - derive key using PBKDF2
                using var pbkdf2 = new Rfc2898DeriveBytes(
                    keyString,
                    salt: Encoding.UTF8.GetBytes("PraxovaAdminPortal"),
                    iterations: 100000,
                    HashAlgorithmName.SHA256);
                keyBytes = pbkdf2.GetBytes(KeySize);
            }
        }

        if (keyBytes.Length != KeySize)
        {
            _logger.LogError("Encryption key must be {Expected} bytes, got {Actual}", KeySize, keyBytes.Length);
            return null;
        }

        return keyBytes;
    }
}
