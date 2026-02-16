using System.Security.Cryptography;
using System.Text;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

/// <summary>
/// AES-256-GCM encryption service. Obtains its encryption key from ISealManager
/// (the Key Encryption Key in the envelope encryption hierarchy).
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly ISealManager _sealManager;
    private readonly ILogger<EncryptionService> _logger;
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16; // 128 bits for GCM

    public EncryptionService(ISealManager sealManager, ILogger<EncryptionService> logger)
    {
        _sealManager = sealManager;
        _logger = logger;
    }

    public bool IsConfigured => _sealManager.IsUnsealed;

    public (byte[] CipherText, byte[] Nonce) Encrypt(byte[] plaintext)
    {
        var key = _sealManager.GetEncryptionKey();

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
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
        var key = _sealManager.GetEncryptionKey();

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

    public string DecryptToString(byte[] ciphertextWithTag, byte[] nonce)
    {
        var plaintext = Decrypt(ciphertextWithTag, nonce);
        return Encoding.UTF8.GetString(plaintext);
    }
}
