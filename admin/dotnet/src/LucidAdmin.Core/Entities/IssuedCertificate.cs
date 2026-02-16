namespace LucidAdmin.Core.Entities;

/// <summary>
/// Tracks a certificate issued by the internal PKI.
/// The actual cert/key PEM is stored on disk (data volume);
/// this entity tracks metadata, lifecycle, and health.
/// </summary>
public class IssuedCertificate : BaseEntity
{
    /// <summary>Unique name for this cert (e.g., "admin-portal", "agent-helpdesk-01")</summary>
    public required string Name { get; set; }

    /// <summary>Certificate subject CN</summary>
    public required string SubjectCN { get; set; }

    /// <summary>Comma-separated Subject Alternative Names (DNS names, IPs)</summary>
    public string? SubjectAlternativeNames { get; set; }

    /// <summary>SHA-256 thumbprint of the certificate (hex, lowercase)</summary>
    public required string Thumbprint { get; set; }

    /// <summary>Serial number (hex)</summary>
    public required string SerialNumber { get; set; }

    /// <summary>When the certificate becomes valid</summary>
    public required DateTime NotBefore { get; set; }

    /// <summary>When the certificate expires</summary>
    public required DateTime NotAfter { get; set; }

    /// <summary>Certificate usage: "server-tls", "client-mtls", "ca-root"</summary>
    public required string Usage { get; set; }

    /// <summary>Which component this cert was issued to</summary>
    public string? IssuedTo { get; set; }

    /// <summary>Whether this is the currently active cert for its purpose (vs. a previous/rotated cert)</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>File path where the PEM cert is written (relative to data dir)</summary>
    public string? CertPath { get; set; }

    /// <summary>File path where the PEM private key is written (relative to data dir)</summary>
    public string? KeyPath { get; set; }

    /// <summary>When this cert was renewed/replaced (null if still active original)</summary>
    public DateTime? RenewedAt { get; set; }

    /// <summary>Thumbprint of the replacement cert (if renewed)</summary>
    public string? ReplacedByThumbprint { get; set; }
}
