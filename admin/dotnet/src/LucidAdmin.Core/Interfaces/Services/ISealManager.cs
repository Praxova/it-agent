namespace LucidAdmin.Core.Interfaces.Services;

/// <summary>
/// Manages the seal/unseal lifecycle of the secrets store.
/// When sealed, no secret operations are possible.
/// When unsealed, the Key Encryption Key (KEK) is available in memory.
/// </summary>
public interface ISealManager
{
    /// <summary>Whether the secrets store is currently unsealed and operational.</summary>
    bool IsUnsealed { get; }

    /// <summary>Whether this is a first-time setup (no KEK exists in the database yet).</summary>
    bool RequiresInitialization { get; }

    /// <summary>
    /// Initialize the secrets store for the first time.
    /// Generates a new KEK, encrypts it with a master key derived from the passphrase,
    /// and stores it in the database.
    /// </summary>
    Task InitializeAsync(string passphrase);

    /// <summary>
    /// Unseal the secrets store by providing the master passphrase.
    /// Derives the master key via Argon2id and decrypts the KEK from the database.
    /// </summary>
    /// <returns>True if unseal succeeded; false if the passphrase is wrong.</returns>
    Task<bool> UnsealAsync(string passphrase);

    /// <summary>
    /// Seal the secrets store, zeroing the KEK from memory.
    /// All subsequent secret operations will fail until unsealed again.
    /// </summary>
    void Seal();

    /// <summary>
    /// Get the current KEK bytes. Throws if sealed.
    /// This is called by EncryptionService to perform actual encryption/decryption.
    /// </summary>
    byte[] GetEncryptionKey();
}
