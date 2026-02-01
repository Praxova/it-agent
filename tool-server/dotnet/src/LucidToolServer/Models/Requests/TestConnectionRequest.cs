namespace LucidToolServer.Models.Requests;

/// <summary>
/// Request model for testing connectivity to external systems.
/// Used by Admin Portal to delegate health checks to Tool Server.
/// </summary>
public record TestConnectionRequest(
    string ProviderType,                        // "windows-ad", "ldap", etc.
    string? Domain,                             // For AD: "montanifarms.com"
    string? Server,                             // Optional: specific server to test
    string? Username,                           // Service account username
    string? Password,                           // Service account password (for testing only)
    Dictionary<string, string>? AdditionalConfig // Provider-specific config
);
