using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Models;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Providers;

/// <summary>
/// Provider for ServiceNow integration accounts
/// </summary>
public class ServiceNowProvider : BaseServiceAccountProvider
{
    private readonly ICredentialService? _credentialService;
    private readonly ILogger<ServiceNowProvider>? _logger;

    // Parameterless constructor for backward compatibility
    public ServiceNowProvider()
    {
    }

    // Constructor with credential service for health checks
    public ServiceNowProvider(ICredentialService credentialService, ILogger<ServiceNowProvider> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    public override string ProviderId => "servicenow";
    public override string DisplayName => "ServiceNow";
    public override string Description => "Integration accounts for ServiceNow ITSM platform";
    public override bool IsImplemented => true;

    public override IEnumerable<AccountTypeInfo> SupportedAccountTypes => new[]
    {
        new AccountTypeInfo("basic-auth", "Basic Authentication",
            "Username and password authentication", true),
        new AccountTypeInfo("oauth", "OAuth 2.0",
            "OAuth client credentials flow", true)
    };

    public override IEnumerable<CredentialStorageType> SupportedCredentialStorage => new[]
    {
        CredentialStorageType.Database,
        CredentialStorageType.Environment,
        CredentialStorageType.Vault
    };

    public override ValidationResult ValidateConfiguration(string accountType, string? configurationJson)
    {
        var baseResult = base.ValidateConfiguration(accountType, configurationJson);
        if (!baseResult.IsValid) return baseResult;

        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return ValidationResult.Failure("Configuration is required for ServiceNow accounts");
        }

        try
        {
            var config = JsonSerializer.Deserialize<ServiceNowConfiguration>(configurationJson);
            var errors = new List<string>();

            if (config == null)
            {
                return ValidationResult.Failure("Invalid configuration JSON");
            }

            if (string.IsNullOrWhiteSpace(config.InstanceUrl))
            {
                errors.Add("Instance URL is required");
            }
            else if (!Uri.TryCreate(config.InstanceUrl, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                errors.Add("Instance URL must be a valid HTTP/HTTPS URL");
            }

            if (accountType == "basic-auth" && string.IsNullOrWhiteSpace(config.Username))
            {
                errors.Add("Username is required for basic authentication");
            }

            if (accountType == "oauth" && string.IsNullOrWhiteSpace(config.ClientId))
            {
                errors.Add("Client ID is required for OAuth authentication");
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
            "basic-auth" => """
                {
                    "type": "object",
                    "properties": {
                        "instanceUrl": { "type": "string", "description": "ServiceNow instance URL" },
                        "username": { "type": "string", "description": "Service account username" }
                    },
                    "required": ["instanceUrl", "username"]
                }
                """,
            "oauth" => """
                {
                    "type": "object",
                    "properties": {
                        "instanceUrl": { "type": "string", "description": "ServiceNow instance URL" },
                        "clientId": { "type": "string", "description": "OAuth Client ID" },
                        "tokenEndpoint": { "type": "string", "description": "OAuth token endpoint URL" }
                    },
                    "required": ["instanceUrl", "clientId"]
                }
                """,
            _ => "{}"
        };
    }

    public override string GetConfigurationExample(string accountType)
    {
        return accountType switch
        {
            "basic-auth" => """
                {
                    "instanceUrl": "https://dev12345.service-now.com",
                    "username": "lucid-integration"
                }
                """,
            "oauth" => """
                {
                    "instanceUrl": "https://dev12345.service-now.com",
                    "clientId": "abc123def456",
                    "tokenEndpoint": "https://dev12345.service-now.com/oauth_token.do"
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
            var config = JsonSerializer.Deserialize<ServiceNowConfiguration>(account.Configuration ?? "{}");
            if (string.IsNullOrEmpty(config?.InstanceUrl))
                return HealthCheckResult.Unhealthy("Instance URL not configured");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            if (account.AccountType == "basic-auth")
            {
                if (string.IsNullOrEmpty(config.Username))
                    return HealthCheckResult.Unhealthy("Username not configured");

                // Get password from credential service
                string? password = null;

                if (_credentialService != null)
                {
                    var credentials = await _credentialService.GetCredentialsAsync(account, cancellationToken);
                    password = credentials?.Get(CredentialSet.Keys.Password);
                }

                // Fallback to environment variable if credential service not available or no credentials stored
                if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(account.CredentialReference))
                {
                    password = Environment.GetEnvironmentVariable(account.CredentialReference);
                }

                if (string.IsNullOrEmpty(password))
                    return HealthCheckResult.Unhealthy("Password not configured. Store credentials in the Admin Portal or set the environment variable.");

                var credentialBytes = Encoding.ASCII.GetBytes($"{config.Username}:{password}");
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialBytes));
            }
            else if (account.AccountType == "oauth")
            {
                // OAuth would require token exchange - return note for now
                return HealthCheckResult.Unknown("OAuth connectivity test not yet implemented");
            }

            var url = $"{config.InstanceUrl.TrimEnd('/')}/api/now/table/sys_user?sysparm_limit=1";
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return HealthCheckResult.Unhealthy("Invalid credentials");

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return HealthCheckResult.Unhealthy("Access denied - check user permissions");

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"ServiceNow returned {response.StatusCode}");

            return HealthCheckResult.Healthy($"Connected to ServiceNow at {config.InstanceUrl}");
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy($"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Error: {ex.Message}");
        }
    }

    private class ServiceNowConfiguration
    {
        public string? InstanceUrl { get; set; }
        public string? Username { get; set; }
        public string? ClientId { get; set; }
        public string? TokenEndpoint { get; set; }
    }
}
