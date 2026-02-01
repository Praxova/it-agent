namespace LucidAdmin.Core.Interfaces.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive data
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypt plaintext data
    /// </summary>
    /// <param name="plaintext">Data to encrypt</param>
    /// <returns>Tuple of (encrypted data, nonce/IV)</returns>
    (byte[] CipherText, byte[] Nonce) Encrypt(byte[] plaintext);

    /// <summary>
    /// Encrypt a string
    /// </summary>
    (byte[] CipherText, byte[] Nonce) Encrypt(string plaintext);

    /// <summary>
    /// Decrypt data
    /// </summary>
    /// <param name="ciphertext">Encrypted data</param>
    /// <param name="nonce">Nonce/IV used during encryption</param>
    /// <returns>Decrypted data</returns>
    byte[] Decrypt(byte[] ciphertext, byte[] nonce);

    /// <summary>
    /// Decrypt to string
    /// </summary>
    string DecryptToString(byte[] ciphertext, byte[] nonce);

    /// <summary>
    /// Check if encryption service is properly configured
    /// </summary>
    bool IsConfigured { get; }
}
