using System.Net;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for Windows Active Directory service accounts
/// </summary>
public class WindowsAdProvider : BaseServiceAccountProvider
{
    private readonly ICredentialService? _credentialService;
    private readonly ILogger<WindowsAdProvider>? _logger;

    public WindowsAdProvider()
    {
    }

    public WindowsAdProvider(ICredentialService credentialService, ILogger<WindowsAdProvider> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    public override string ProviderId => "windows-ad";
    public override string DisplayName => "Windows Active Directory";
    public override string Description => "Service accounts in Windows Active Directory, including Group Managed Service Accounts (gMSA)";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("gmsa", "Group Managed Service Account (gMSA)",
            "Recommended for production. Password managed automatically by AD.", false),
        new AccountTypeInfo("traditional", "Traditional Service Account",
            "Standard AD user account with password. Requires credential storage.", true),
        new AccountTypeInfo("current-user", "Current User Context",
            "Development only. Uses the credentials of the running process.", false)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.None,         // For gMSA and current-user
        CredentialStorageType.Database,     // For traditional
        CredentialStorageType.Environment,  // For traditional
        CredentialStorageType.Vault         // For traditional
    };

    public override ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        var baseResult = base.ValidateConfiguration(accountType, configurationJson);
        if (!baseResult.IsValid) return baseResult;

        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return ValidationResult.Failure("Configuration is required for Windows AD accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<WindowsAdConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.Domain))
            {
                errors.Add("Domain is required");
            }

            if (string.IsNullOrWhiteSpace(config.SamAccountName))
            {
                errors.Add("SAM Account Name is required");
            }

            // gMSA accounts should end with $
            if (accountType == "gmsa" && !string.IsNullOrEmpty(config.SamAccountName) && !config.SamAccountName.EndsWith("$"))
            {
                errors.Add("gMSA account names should end with '$'");
            }

            return errors.Count > 0 ? ValidationResult.Failure(errors) : ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    public override string GetConfigurationSchema(string accountType)
    {
        return """
        {
            "type": "object",
            "properties": {
                "domain": { "type": "string", "description": "AD domain name (e.g., montanifarms.com)" },
                "samAccountName": { "type": "string", "description": "SAM Account Name (e.g., svc-lucid-pwreset$)" },
                "ouPath": { "type": "string", "description": "Optional OU path for the account" }
            },
            "required": ["domain", "samAccountName"]
        }
        """;
    }

    public override string GetConfigurationExample(string accountType)
    {
        return accountType switch
        {
            "gmsa" => """
                {
                    "domain": "montanifarms.com",
                    "samAccountName": "svc-lucid-pwreset$",
                    "ouPath": "OU=ServiceAccounts,DC=montanifarms,DC=com"
                }
                """,
            "traditional" => """
                {
                    "domain": "montanifarms.com",
                    "samAccountName": "svc-lucid-legacy",
                    "ouPath": "OU=ServiceAccounts,DC=montanifarms,DC=com"
                }
                """,
            "current-user" => """
                {
                    "domain": "montanifarms.com"
                }
                """,
            _ => "{}"
        };
    }

    public override async Task<HealthCheckResult> TestConnectivityAsync(
        ServiceAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<WindowsAdConfiguration>(account.Configuration ?? "{}");
            if (string.IsNullOrEmpty(config?.Domain))
                return HealthCheckResult.Unhealthy("Domain not configured");

            // Validate credentials are available for traditional accounts
            if (account.AccountType == "traditional")
            {
                string? password = null;

                if (_credentialService != null)
                {
                    var credentials = await _credentialService.GetCredentialsAsync(account, cancellationToken);
                    password = credentials?.Get(CredentialSet.Keys.Password);
                }

                if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(account.CredentialReference))
                {
                    password = Environment.GetEnvironmentVariable(account.CredentialReference);
                }

                if (string.IsNullOrEmpty(password))
                {
                    return HealthCheckResult.Unhealthy(
                        "Password not configured for traditional service account. Store credentials in the Admin Portal.");
                }
            }

            // Try DNS lookup to verify domain is reachable
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(config.Domain, cancellationToken);

                if (hostEntry.AddressList.Length == 0)
                    return HealthCheckResult.Unhealthy($"Domain '{config.Domain}' resolved but no addresses found");

                var message = account.AccountType switch
                {
                    "gmsa" => $"Domain '{config.Domain}' is resolvable ({hostEntry.AddressList[0]}). gMSA authentication will be used.",
                    "traditional" => $"Domain '{config.Domain}' is resolvable ({hostEntry.AddressList[0]}). Credentials are configured.",
                    "current-user" => $"Domain '{config.Domain}' is resolvable ({hostEntry.AddressList[0]}). Current user context will be used.",
                    _ => $"Domain '{config.Domain}' is resolvable ({hostEntry.AddressList[0]})."
                };

                return HealthCheckResult.Healthy(message + " Full AD connectivity testing requires Windows Tool Server.");
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                return HealthCheckResult.Unhealthy($"Cannot resolve domain '{config.Domain}': {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Configuration error: {ex.Message}");
        }
    }

    // Configuration class for deserialization
    private class WindowsAdConfiguration
    {
        public string? Domain { get; set; }
        public string? SamAccountName { get; set; }
        public string? OuPath { get; set; }
    }
}
