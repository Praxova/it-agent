namespace LucidAdmin.Core.Interfaces.Services;

/// <summary>
/// Manages the JWT signing key lifecycle. On first startup, generates a
/// cryptographically random key and stores it encrypted in the database.
/// On subsequent startups, retrieves and decrypts the existing key.
/// </summary>
public interface IJwtKeyManager
{
    /// <summary>
    /// Returns the JWT signing key bytes. The key is held in memory
    /// for the lifetime of the application.
    /// </summary>
    byte[] GetSigningKey();

    /// <summary>
    /// Initializes the key manager: loads or generates the JWT signing key.
    /// Must be called at startup before any JWT operations.
    /// </summary>
    Task InitializeAsync();
}
