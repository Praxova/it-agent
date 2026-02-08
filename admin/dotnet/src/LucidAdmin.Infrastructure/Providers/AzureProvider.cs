using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for Microsoft Azure / Entra ID service accounts
/// </summary>
public class AzureProvider : BaseServiceAccountProvider
{
    private readonly ICredentialService? _credentialService;
    private readonly ILogger<AzureProvider>? _logger;

    public AzureProvider()
    {
    }

    public AzureProvider(ICredentialService credentialService, ILogger<AzureProvider> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    public override string ProviderId => "azure";
    public override string DisplayName => "Microsoft Azure / Entra ID";
    public override string Description => "Azure App Registrations for Microsoft Graph and Azure Resource Manager APIs";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("app-registration", "App Registration (Client Credentials)",
            "OAuth2 client credentials flow. Requires TenantId, ClientId, and ClientSecret.", true),
        new AccountTypeInfo("managed-identity", "Managed Identity",
            "For tool servers running in Azure. No credentials needed.", false)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.None,         // For managed-identity
        CredentialStorageType.Database,     // For app-registration (ClientSecret)
        CredentialStorageType.Environment,  // For app-registration
        CredentialStorageType.Vault         // For app-registration
    };

    public override ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        var baseResult = base.ValidateConfiguration(accountType, configurationJson);
        if (!baseResult.IsValid) return baseResult;

        if (accountType == "managed-identity")
        {
            // Managed identity requires no configuration beyond optional subscription ID
            return ValidationResult.Success();
        }

        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return ValidationResult.Failure("Configuration is required for Azure app-registration accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<AzureConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.TenantId))
            {
                errors.Add("TenantId is required");
            }
            else if (!Guid.TryParse(config.TenantId, out _))
            {
                errors.Add("TenantId must be a valid GUID");
            }

            if (string.IsNullOrWhiteSpace(config.ClientId))
            {
                errors.Add("ClientId is required");
            }
            else if (!Guid.TryParse(config.ClientId, out _))
            {
                errors.Add("ClientId must be a valid GUID");
            }

            if (!string.IsNullOrEmpty(config.DefaultSubscriptionId) && !Guid.TryParse(config.DefaultSubscriptionId, out _))
            {
                errors.Add("DefaultSubscriptionId must be a valid GUID");
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
        return accountType switch
        {
            "app-registration" => """
                {
                    "type": "object",
                    "properties": {
                        "tenantId": { "type": "string", "description": "Azure AD / Entra ID Tenant ID (GUID)" },
                        "clientId": { "type": "string", "description": "App Registration Client ID (GUID)" },
                        "defaultSubscriptionId": { "type": "string", "description": "Optional default Azure Subscription ID for resource lookups" }
                    },
                    "required": ["tenantId", "clientId"]
                }
                """,
            "managed-identity" => """
                {
                    "type": "object",
                    "properties": {
                        "defaultSubscriptionId": { "type": "string", "description": "Optional default Azure Subscription ID for resource lookups" }
                    }
                }
                """,
            _ => "{}"
        };
    }

    public override string GetConfigurationExample(string accountType)
    {
        return accountType switch
        {
            "app-registration" => """
                {
                    "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
                    "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
                    "defaultSubscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                }
                """,
            "managed-identity" => """
                {
                    "defaultSubscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
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
            if (account.AccountType == "managed-identity")
            {
                return HealthCheckResult.Healthy("Managed Identity will be used at runtime");
            }

            var config = JsonSerializer.Deserialize<AzureConfiguration>(account.Configuration ?? "{}");
            if (string.IsNullOrEmpty(config?.TenantId) || string.IsNullOrEmpty(config?.ClientId))
                return HealthCheckResult.Unhealthy("TenantId and ClientId are required");

            // Get client secret from credentials
            string? clientSecret = null;

            if (_credentialService != null)
            {
                var credentials = await _credentialService.GetCredentialsAsync(account, cancellationToken);
                clientSecret = credentials?.Get(CredentialSet.Keys.Password)
                            ?? credentials?.Get(CredentialSet.Keys.ApiKey);
            }

            if (string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(account.CredentialReference))
            {
                clientSecret = Environment.GetEnvironmentVariable(account.CredentialReference);
            }

            if (string.IsNullOrEmpty(clientSecret))
            {
                return HealthCheckResult.Unhealthy(
                    "ClientSecret not configured. Store credentials in the Admin Portal.");
            }

            // Try to acquire a token via client credentials flow
            using var httpClient = new HttpClient();
            var tokenUrl = $"https://login.microsoftonline.com/{config.TenantId}/oauth2/v2.0/token";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = config.ClientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "https://graph.microsoft.com/.default"
            });

            var response = await httpClient.PostAsync(tokenUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy(
                    $"Successfully acquired token for tenant '{config.TenantId}'. Azure connectivity verified.");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return HealthCheckResult.Unhealthy($"Token acquisition failed ({response.StatusCode}): {error}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure connectivity test failed");
            return HealthCheckResult.Unhealthy($"Connectivity test failed: {ex.Message}");
        }
    }

    private class AzureConfiguration
    {
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? DefaultSubscriptionId { get; set; }
    }
}
