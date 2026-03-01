using System.Text.Json.Serialization;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Models;

public record CreateServiceAccountRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("account_type")] string AccountType,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("credential_storage")] CredentialStorageType CredentialStorage,
    [property: JsonPropertyName("credential_reference")] string? CredentialReference
);

public record UpdateServiceAccountRequest(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("credential_storage")] CredentialStorageType? CredentialStorage,
    [property: JsonPropertyName("credential_reference")] string? CredentialReference,
    [property: JsonPropertyName("is_enabled")] bool? IsEnabled
);

public record ServiceAccountResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("account_type")] string AccountType,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("credential_storage")] string CredentialStorage,
    [property: JsonPropertyName("credential_reference")] string? CredentialReference,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("health_status")] string HealthStatus,
    [property: JsonPropertyName("last_health_check")] DateTime? LastHealthCheck,
    [property: JsonPropertyName("last_health_message")] string? LastHealthMessage,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

public record ProviderDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("account_types")] IEnumerable<AccountTypeDto> AccountTypes
);

public record AccountTypeDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("requires_credentials")] bool RequiresCredentials
);

// Form model for Blazor component binding
public class ServiceAccountFormModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public CredentialStorageType CredentialStorage { get; set; } = CredentialStorageType.None;
    public string? CredentialReference { get; set; }
    public bool IsEnabled { get; set; } = true;

    // Windows AD specific fields
    public string? Domain { get; set; }
    public string? SamAccountName { get; set; }
    public string? OuPath { get; set; }

    // ServiceNow specific fields
    public string? InstanceUrl { get; set; }
    public string? Username { get; set; }
    public string? ClientId { get; set; }
    public string? TokenEndpoint { get; set; }

    // LLM specific fields
    public string? Endpoint { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; } = 0.1;
    public string? DeploymentName { get; set; }  // Azure OpenAI
    public string? ApiVersion { get; set; }      // Azure OpenAI
    public string? Region { get; set; }          // AWS Bedrock

    public CreateServiceAccountRequest ToCreateRequest()
    {
        var config = BuildConfigJson();
        return new CreateServiceAccountRequest(
            Name,
            DisplayName,
            Description,
            Provider,
            AccountType,
            config,
            CredentialStorage,
            CredentialReference
        );
    }

    public UpdateServiceAccountRequest ToUpdateRequest()
    {
        var config = BuildConfigJson();
        return new UpdateServiceAccountRequest(
            DisplayName,
            Description,
            config,
            CredentialStorage,
            CredentialReference,
            IsEnabled
        );
    }

    private string? BuildConfigJson()
    {
        var config = new Dictionary<string, string>();

        if (Provider == "windows-ad")
        {
            if (!string.IsNullOrWhiteSpace(Domain))
                config["Domain"] = Domain;
            if (!string.IsNullOrWhiteSpace(SamAccountName))
                config["SamAccountName"] = SamAccountName;
            if (!string.IsNullOrWhiteSpace(OuPath))
                config["OuPath"] = OuPath;
        }
        else if (Provider == "servicenow")
        {
            if (!string.IsNullOrWhiteSpace(InstanceUrl))
                config["InstanceUrl"] = InstanceUrl;

            if (AccountType == "basic-auth" && !string.IsNullOrWhiteSpace(Username))
            {
                config["Username"] = Username;
            }
            else if (AccountType == "oauth")
            {
                if (!string.IsNullOrWhiteSpace(ClientId))
                    config["ClientId"] = ClientId;
                if (!string.IsNullOrWhiteSpace(TokenEndpoint))
                    config["TokenEndpoint"] = TokenEndpoint;
            }
        }
        else if (Provider == "llm-ollama")
        {
            if (!string.IsNullOrWhiteSpace(Endpoint))
                config["base_url"] = Endpoint;
            if (!string.IsNullOrWhiteSpace(Model))
                config["model"] = Model;
            if (Temperature.HasValue)
                config["temperature"] = Temperature.Value.ToString("F1");
        }
        else if (Provider == "llm-llamacpp")
        {
            if (!string.IsNullOrWhiteSpace(Endpoint))
                config["base_url"] = Endpoint;
            if (!string.IsNullOrWhiteSpace(Model))
                config["model"] = Model;
            if (Temperature.HasValue)
                config["temperature"] = Temperature.Value.ToString("F1");
        }
        else if (Provider == "llm-openai")
        {
            if (!string.IsNullOrWhiteSpace(Model))
                config["model"] = Model;
            if (Temperature.HasValue)
                config["temperature"] = Temperature.Value.ToString("F1");
            if (!string.IsNullOrWhiteSpace(Endpoint))
                config["base_url"] = Endpoint;
        }
        else if (Provider == "llm-anthropic")
        {
            if (!string.IsNullOrWhiteSpace(Model))
                config["model"] = Model;
            if (Temperature.HasValue)
                config["temperature"] = Temperature.Value.ToString("F1");
        }
        else if (Provider == "llm-azure-openai")
        {
            if (!string.IsNullOrWhiteSpace(Endpoint))
                config["endpoint"] = Endpoint;
            if (!string.IsNullOrWhiteSpace(DeploymentName))
                config["deployment_name"] = DeploymentName;
            if (!string.IsNullOrWhiteSpace(ApiVersion))
                config["api_version"] = ApiVersion;
            if (Temperature.HasValue)
                config["temperature"] = Temperature.Value.ToString("F1");
        }
        else if (Provider == "llm-bedrock")
        {
            if (!string.IsNullOrWhiteSpace(Region))
                config["region"] = Region;
            if (!string.IsNullOrWhiteSpace(Model))
                config["model_id"] = Model;
            if (Temperature.HasValue)
                config["temperature"] = Temperature.Value.ToString("F1");
        }

        return config.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(config) : null;
    }

    public static ServiceAccountFormModel FromResponse(ServiceAccountResponse response)
    {
        var model = new ServiceAccountFormModel
        {
            Id = response.Id,
            Name = response.Name,
            DisplayName = response.DisplayName,
            Description = response.Description,
            Provider = response.Provider,
            AccountType = response.AccountType,
            CredentialReference = response.CredentialReference,
            IsEnabled = response.IsEnabled
        };

        // Parse CredentialStorage enum
        if (Enum.TryParse<CredentialStorageType>(response.CredentialStorage, true, out var credStorage))
        {
            model.CredentialStorage = credStorage;
        }

        // Parse configuration JSON
        if (!string.IsNullOrWhiteSpace(response.Configuration))
        {
            try
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(response.Configuration);
                if (config != null)
                {
                    if (response.Provider == "windows-ad")
                    {
                        model.Domain = config.TryGetValue("Domain", out var domain) ? domain : null;
                        model.SamAccountName = config.TryGetValue("SamAccountName", out var sam) ? sam : null;
                        model.OuPath = config.TryGetValue("OuPath", out var ou) ? ou : null;
                    }
                    else if (response.Provider == "servicenow")
                    {
                        model.InstanceUrl = config.TryGetValue("InstanceUrl", out var url) ? url : null;
                        model.Username = config.TryGetValue("Username", out var user) ? user : null;
                        model.ClientId = config.TryGetValue("ClientId", out var client) ? client : null;
                        model.TokenEndpoint = config.TryGetValue("TokenEndpoint", out var token) ? token : null;
                    }
                    else if (response.Provider.StartsWith("llm-"))
                    {
                        // Common LLM fields
                        model.Model = config.TryGetValue("model", out var mdl) ? mdl :
                                     config.TryGetValue("model_id", out var mdlId) ? mdlId : null;

                        if (config.TryGetValue("temperature", out var temp) && double.TryParse(temp, out var tempVal))
                            model.Temperature = tempVal;

                        // Provider-specific fields
                        if (response.Provider == "llm-ollama" || response.Provider == "llm-llamacpp" || response.Provider == "llm-openai")
                        {
                            model.Endpoint = config.TryGetValue("base_url", out var baseUrl) ? baseUrl : null;
                        }
                        else if (response.Provider == "llm-azure-openai")
                        {
                            model.Endpoint = config.TryGetValue("endpoint", out var ep) ? ep : null;
                            model.DeploymentName = config.TryGetValue("deployment_name", out var dep) ? dep : null;
                            model.ApiVersion = config.TryGetValue("api_version", out var ver) ? ver : null;
                        }
                        else if (response.Provider == "llm-bedrock")
                        {
                            model.Region = config.TryGetValue("region", out var reg) ? reg : null;
                        }
                    }
                }
            }
            catch
            {
                // Ignore JSON parsing errors
            }
        }

        return model;
    }
}
