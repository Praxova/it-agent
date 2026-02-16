using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

/// <summary>
/// Represents a service account for any supported identity provider
/// </summary>
public class ServiceAccount : BaseEntity
{
    // === Identity ===
    /// <summary>
    /// Unique identifier for this service account (e.g., "svc-lucid-pwreset")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-friendly display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of this account's purpose
    /// </summary>
    public string? Description { get; set; }

    // === Provider System ===
    /// <summary>
    /// Identity provider identifier: "windows-ad", "linux", "servicenow", "aws", etc.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// Provider-specific account type: "gmsa", "traditional", "basic-auth", "oauth", etc.
    /// </summary>
    public required string AccountType { get; set; }

    /// <summary>
    /// Provider-specific configuration as JSON. Schema varies by provider.
    /// </summary>
    public string? Configuration { get; set; }

    // === Credential Storage ===
    /// <summary>
    /// How credentials are stored/retrieved for this account
    /// </summary>
    public CredentialStorageType CredentialStorage { get; set; } = CredentialStorageType.None;

    /// <summary>
    /// Reference to the credential (env var name, vault path, config key)
    /// </summary>
    public string? CredentialReference { get; set; }

    /// <summary>
    /// Encrypted credentials (for Database storage type) - AES-256-GCM encrypted JSON
    /// </summary>
    public byte[]? EncryptedCredentials { get; set; }

    /// <summary>
    /// Nonce/IV for AES-256-GCM decryption
    /// </summary>
    public byte[]? CredentialNonce { get; set; }

    /// <summary>
    /// When credentials were last updated
    /// </summary>
    public DateTime? CredentialsUpdatedAt { get; set; }

    /// <summary>
    /// When this credential expires (null = no expiration set)
    /// </summary>
    public DateTime? CredentialExpiresAt { get; set; }

    /// <summary>
    /// When credentials were last deliberately rotated (distinct from CredentialsUpdatedAt
    /// which tracks any update including initial creation)
    /// </summary>
    public DateTime? LastRotatedAt { get; set; }

    /// <summary>
    /// SHA-256 fingerprint of the plaintext credential for change detection.
    /// Allows checking if a credential changed without decrypting it.
    /// </summary>
    public string? CredentialFingerprint { get; set; }

    // === Status ===
    public bool IsEnabled { get; set; } = true;
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;
    public DateTime? LastHealthCheck { get; set; }
    public string? LastHealthMessage { get; set; }

    // === Navigation ===
    public ICollection<CapabilityMapping> CapabilityMappings { get; set; } = new List<CapabilityMapping>();
}
