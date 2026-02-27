namespace LucidAdmin.Core.Interfaces.Services;

public interface IInternalPkiService
{
    /// <summary>Whether the internal CA has been initialized.</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initialize the internal CA. Generates RSA 4096 root CA keypair,
    /// stores private key encrypted in SystemSecrets.
    /// Only called once — subsequent startups use LoadAsync.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Load the existing CA from SystemSecrets. Called on subsequent startups.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Issue a TLS server certificate signed by the internal CA.
    /// </summary>
    /// <param name="name">Logical name (e.g., "admin-portal")</param>
    /// <param name="commonName">Subject CN</param>
    /// <param name="sanDnsNames">Subject Alternative Name DNS entries</param>
    /// <param name="sanIpAddresses">Subject Alternative Name IP entries</param>
    /// <param name="lifetimeDays">Certificate lifetime in days (default: 90)</param>
    /// <returns>Tuple of (certPem, keyPem) as strings</returns>
    Task<(string CertPem, string KeyPem)> IssueCertificateAsync(
        string name,
        string commonName,
        string[]? sanDnsNames = null,
        string[]? sanIpAddresses = null,
        int lifetimeDays = 90);

    /// <summary>
    /// Get the CA public certificate in PEM format (trust bundle).
    /// </summary>
    string GetCaCertificatePem();

    /// <summary>
    /// Check if a certificate needs renewal (expires within thresholdDays).
    /// </summary>
    Task<bool> NeedsRenewalAsync(string name, int thresholdDays = 30);

    /// <summary>
    /// Renew a certificate — issues a new one with the same parameters,
    /// marks the old one as inactive.
    /// </summary>
    Task<(string CertPem, string KeyPem)> RenewCertificateAsync(string name, int lifetimeDays = 90);

    /// <summary>
    /// Retrieve or issue a client authentication certificate for an agent.
    /// On first call: issues a new cert (EKU: clientAuth), stores encrypted
    /// private key in SystemSecrets, and records in IssuedCertificates.
    /// On subsequent calls: returns the existing cert+key if valid (NotAfter > now + 30d).
    /// If within 30 days of expiry: auto-renews and stores the new key.
    /// </summary>
    /// <param name="agentName">Agent's registered name (e.g., "agent-helpdesk-01")</param>
    /// <returns>Tuple of (certPem, keyPem, expiresAt)</returns>
    Task<(string CertPem, string KeyPem, DateTime ExpiresAt)> GetOrIssueAgentClientCertAsync(
        string agentName);
}
