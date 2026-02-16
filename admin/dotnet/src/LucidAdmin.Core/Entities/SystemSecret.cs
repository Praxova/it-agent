namespace LucidAdmin.Core.Entities;

/// <summary>
/// Stores system-level secrets (JWT signing keys, CA private keys, etc.)
/// encrypted using the EncryptionService. Designed for reuse across
/// ADR-014 (CA keys) and ADR-015 (JWT keys, future secrets).
/// </summary>
public class SystemSecret : BaseEntity
{
    /// <summary>
    /// Unique name identifying this secret (e.g., "jwt-signing-key")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// AES-256-GCM encrypted secret value
    /// </summary>
    public required byte[] EncryptedValue { get; set; }

    /// <summary>
    /// Nonce/IV used for AES-256-GCM encryption
    /// </summary>
    public required byte[] Nonce { get; set; }

    /// <summary>
    /// Human-readable description of this secret's purpose
    /// </summary>
    public string? Purpose { get; set; }

    /// <summary>
    /// When the secret was last deliberately rotated
    /// </summary>
    public DateTime? RotatedAt { get; set; }

    /// <summary>
    /// Optional metadata for this secret (e.g., Argon2id salt as hex for envelope KEK).
    /// Schema depends on the secret type.
    /// </summary>
    public string? Metadata { get; set; }
}
