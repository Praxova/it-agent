using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class ServiceAccountEndpoints
{
    public static void MapServiceAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/service-accounts")
            .WithTags("Service Accounts")
            .RequireAuthorization();

        group.MapGet("/", async (IServiceAccountRepository repository) =>
        {
            var accounts = await repository.GetAllAsync();
            return Results.Ok(accounts.Select(MapToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, IServiceAccountRepository repository) =>
        {
            var account = await repository.GetByIdAsync(id);
            if (account == null)
            {
                throw new EntityNotFoundException("ServiceAccount", id);
            }
            return Results.Ok(MapToResponse(account));
        });

        group.MapGet("/provider/{provider}", async (string provider, IServiceAccountRepository repository) =>
        {
            var accounts = await repository.GetByProviderAsync(provider);
            return Results.Ok(accounts.Select(MapToResponse));
        });

        group.MapPost("/", async (
            [FromBody] CreateServiceAccountRequest request,
            IServiceAccountRepository repository,
            IAuditEventRepository auditRepository,
            IProviderRegistry providerRegistry) =>
        {
            // Validate provider exists
            var provider = providerRegistry.GetProvider(request.Provider);
            if (provider == null)
            {
                return Results.BadRequest(new { error = $"Unknown provider: {request.Provider}" });
            }

            // Only allow implemented providers
            if (!provider.IsImplemented)
            {
                return Results.BadRequest(new { error = $"Provider '{request.Provider}' is not yet implemented" });
            }

            // Validate configuration using provider
            var validation = provider.ValidateConfiguration(request.AccountType, request.Configuration);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { errors = validation.Errors });
            }

            // Check for duplicate name
            var existing = await repository.GetByNameAsync(request.Name);
            if (existing != null)
            {
                throw new DuplicateEntityException("ServiceAccount", request.Name);
            }

            var account = new ServiceAccount
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Provider = request.Provider,
                AccountType = request.AccountType,
                Configuration = request.Configuration,
                CredentialStorage = request.CredentialStorage,
                CredentialReference = request.CredentialReference,
                IsEnabled = true,
                HealthStatus = HealthStatus.Unknown
            };

            await repository.AddAsync(account);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.ServiceAccountCreated,
                PerformedBy = "System",
                TargetResource = account.Name,
                Success = true
            });

            return Results.Created($"/api/v1/service-accounts/{account.Id}", MapToResponse(account));
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateServiceAccountRequest request,
            IServiceAccountRepository repository,
            IAuditEventRepository auditRepository,
            IProviderRegistry providerRegistry) =>
        {
            var account = await repository.GetByIdAsync(id);
            if (account == null)
            {
                throw new EntityNotFoundException("ServiceAccount", id);
            }

            // If configuration is being updated, validate it
            if (request.Configuration != null)
            {
                var provider = providerRegistry.GetProvider(account.Provider);
                if (provider != null)
                {
                    var validation = provider.ValidateConfiguration(account.AccountType, request.Configuration);
                    if (!validation.IsValid)
                    {
                        return Results.BadRequest(new { errors = validation.Errors });
                    }
                }
            }

            if (request.DisplayName != null) account.DisplayName = request.DisplayName;
            if (request.Description != null) account.Description = request.Description;
            if (request.Configuration != null) account.Configuration = request.Configuration;
            if (request.CredentialStorage.HasValue) account.CredentialStorage = request.CredentialStorage.Value;
            if (request.CredentialReference != null) account.CredentialReference = request.CredentialReference;
            if (request.IsEnabled.HasValue) account.IsEnabled = request.IsEnabled.Value;

            await repository.UpdateAsync(account);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.ServiceAccountUpdated,
                PerformedBy = "System",
                TargetResource = account.Name,
                Success = true
            });

            return Results.Ok(MapToResponse(account));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IServiceAccountRepository repository,
            IAuditEventRepository auditRepository) =>
        {
            var account = await repository.GetByIdAsync(id);
            if (account == null)
            {
                throw new EntityNotFoundException("ServiceAccount", id);
            }

            await repository.DeleteAsync(id);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.ServiceAccountDeleted,
                PerformedBy = "System",
                TargetResource = account.Name,
                Success = true
            });

            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/test-connectivity", async (
            Guid id,
            IServiceAccountRepository repository,
            IToolServerRepository toolServerRepository,
            ICapabilityMappingRepository capabilityRepository,
            IProviderRegistry providerRegistry,
            IAuditEventRepository auditRepository,
            IHttpClientFactory httpClientFactory,
            ICredentialService credentialService,
            LucidDbContext db) =>
        {
            var account = await repository.GetByIdAsync(id);
            if (account == null)
            {
                throw new EntityNotFoundException("ServiceAccount", id);
            }

            var providerType = account.Provider.ToLower();

            // Determine if this provider requires delegated testing via Tool Server
            var requiresToolServer = providerType == "windows-ad";

            TestConnectivityResponse result;

            if (requiresToolServer)
            {
                // Delegate to Tool Server for AD/infrastructure tests
                result = await DelegateTestToToolServer(
                    account,
                    toolServerRepository,
                    capabilityRepository,
                    httpClientFactory,
                    credentialService,
                    db);
            }
            else
            {
                // Direct test for HTTP-based providers (ServiceNow, LLMs)
                var provider = providerRegistry.GetProvider(account.Provider);
                if (provider == null)
                {
                    return Results.BadRequest(new { error = $"Unknown provider: {account.Provider}" });
                }

                var testResult = await provider.TestConnectivityAsync(account);
                result = new TestConnectivityResponse(
                    Status: testResult.Status.ToString(),
                    Message: testResult.Message,
                    CheckedAt: testResult.CheckedAt,
                    Details: testResult.Details
                );
            }

            // Update account health status
            var healthStatus = result.Status.ToLower() switch
            {
                "healthy" => HealthStatus.Healthy,
                "unhealthy" => HealthStatus.Unhealthy,
                _ => HealthStatus.Unknown
            };

            account.HealthStatus = healthStatus;
            account.LastHealthCheck = result.CheckedAt;
            account.LastHealthMessage = result.Message;
            await repository.UpdateAsync(account);

            // Audit the test
            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.ServiceAccountConnectivityTest,
                PerformedBy = "System",
                TargetResource = account.Name,
                Success = healthStatus == HealthStatus.Healthy,
                ErrorMessage = healthStatus != HealthStatus.Healthy ? result.Message : null,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { action = "test-connectivity", result = result.Status })
            });

            return Results.Ok(result);
        });
    }

    private static ServiceAccountResponse MapToResponse(ServiceAccount account) => new(
        Id: account.Id,
        Name: account.Name,
        DisplayName: account.DisplayName,
        Description: account.Description,
        Provider: account.Provider,
        AccountType: account.AccountType,
        Configuration: account.Configuration,
        CredentialStorage: account.CredentialStorage.ToString(),
        CredentialReference: account.CredentialReference,
        IsEnabled: account.IsEnabled,
        HealthStatus: account.HealthStatus.ToString(),
        LastHealthCheck: account.LastHealthCheck,
        LastHealthMessage: account.LastHealthMessage,
        CreatedAt: account.CreatedAt,
        UpdatedAt: account.UpdatedAt
    );

    private static async Task<TestConnectivityResponse> DelegateTestToToolServer(
        ServiceAccount serviceAccount,
        IToolServerRepository toolServerRepository,
        ICapabilityMappingRepository capabilityRepository,
        IHttpClientFactory httpClientFactory,
        ICredentialService credentialService,
        LucidDbContext db)
    {
        // Find a tool server that can test this provider
        var lookup = await FindToolServerForProvider(
            serviceAccount.Provider,
            toolServerRepository,
            capabilityRepository,
            db);

        if (lookup.ToolServer == null)
        {
            var (message, recommendation) = lookup.Reason switch
            {
                ToolServerLookupReason.NoToolServer => (
                    "No tool server configured",
                    "Create a Tool Server first, then add a Capability Mapping for AD operations."
                ),
                ToolServerLookupReason.NoCapabilityMapping => (
                    "Tool server exists but no AD capability mapping configured",
                    "Add a Capability Mapping with an ad-* capability (e.g. ad-user-lookup) to your Tool Server."
                ),
                _ => (
                    "No tool server available to test this connection",
                    "Create a Tool Server and Capability Mapping for AD operations first."
                )
            };

            return new TestConnectivityResponse(
                Status: "warning",
                Message: message,
                CheckedAt: DateTime.UtcNow,
                Details: new Dictionary<string, object>
                {
                    ["recommendation"] = recommendation
                }
            );
        }

        var toolServer = lookup.ToolServer;

        // Parse configuration from JSON (keys are PascalCase: "Domain", "SamAccountName", etc.)
        Dictionary<string, string>? config = null;
        if (!string.IsNullOrEmpty(serviceAccount.Configuration))
        {
            try
            {
                config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                    serviceAccount.Configuration);
            }
            catch
            {
                // Configuration is not valid JSON, ignore
            }
        }

        // Extract AD-specific config values (PascalCase keys from ServiceAccountModels)
        string? domain = config?.GetValueOrDefault("Domain") ?? config?.GetValueOrDefault("domain");
        string? server = config?.GetValueOrDefault("Server") ?? config?.GetValueOrDefault("server");
        string? username = config?.GetValueOrDefault("SamAccountName")
                        ?? config?.GetValueOrDefault("Username")
                        ?? config?.GetValueOrDefault("username");
        string? password = null;

        // Get credentials from secure storage
        if (serviceAccount.CredentialStorage == CredentialStorageType.Database)
        {
            try
            {
                var credentials = await credentialService.GetCredentialsAsync(serviceAccount);
                if (credentials != null)
                {
                    password = credentials.Get(CredentialSet.Keys.Password);
                    // Also try to get username from credentials if not in config
                    username ??= credentials.Get(CredentialSet.Keys.Username);
                }
            }
            catch
            {
                return new TestConnectivityResponse(
                    Status: "unhealthy",
                    Message: "Failed to retrieve credentials from secure storage",
                    CheckedAt: DateTime.UtcNow,
                    Details: new Dictionary<string, object>
                    {
                        ["error"] = "Ensure credentials are stored and encrypted properly."
                    }
                );
            }
        }

        // Build the test request
        var testRequest = new
        {
            ProviderType = serviceAccount.Provider,
            Domain = domain,
            Server = server,
            Username = username,
            Password = password,
            AdditionalConfig = config
        };

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(toolServer.Endpoint.TrimEnd('/'));
        // AD authentication with invalid credentials can take 30-60s due to Kerberos/LDAP retries
        client.Timeout = TimeSpan.FromSeconds(90);

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/health/test-connection", testRequest);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ToolServerTestConnectionResponse>();
                if (result != null)
                {
                    Dictionary<string, object>? details = null;
                    if (!string.IsNullOrEmpty(result.Details))
                    {
                        details = new Dictionary<string, object> { ["info"] = result.Details };
                    }

                    return new TestConnectivityResponse(
                        Status: result.Success ? "healthy" : "unhealthy",
                        Message: result.Message,
                        CheckedAt: result.TestedAt,
                        Details: details
                    );
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            return new TestConnectivityResponse(
                Status: "unhealthy",
                Message: $"Tool server returned error: {response.StatusCode}",
                CheckedAt: DateTime.UtcNow,
                Details: new Dictionary<string, object>
                {
                    ["statusCode"] = (int)response.StatusCode,
                    ["error"] = errorContent
                }
            );
        }
        catch (TaskCanceledException)
        {
            return new TestConnectivityResponse(
                Status: "unhealthy",
                Message: $"Tool server request timed out after 90 seconds",
                CheckedAt: DateTime.UtcNow,
                Details: new Dictionary<string, object>
                {
                    ["endpoint"] = toolServer.Endpoint,
                    ["error"] = "The AD connectivity test took too long. This can happen with invalid credentials or unreachable domain controllers."
                }
            );
        }
        catch (HttpRequestException ex)
        {
            return new TestConnectivityResponse(
                Status: "unhealthy",
                Message: $"Cannot reach tool server at {toolServer.Endpoint}",
                CheckedAt: DateTime.UtcNow,
                Details: new Dictionary<string, object>
                {
                    ["endpoint"] = toolServer.Endpoint,
                    ["error"] = ex.Message
                }
            );
        }
        catch (Exception ex)
        {
            return new TestConnectivityResponse(
                Status: "unhealthy",
                Message: $"Connection test failed: {ex.Message}",
                CheckedAt: DateTime.UtcNow,
                Details: new Dictionary<string, object> { ["error"] = ex.Message }
            );
        }
    }

    private static async Task<ToolServerLookupResult> FindToolServerForProvider(
        string providerType,
        IToolServerRepository toolServerRepository,
        ICapabilityMappingRepository capabilityRepository,
        LucidDbContext db)
    {
        // For windows-ad, look for any tool server with ad-* capabilities
        if (providerType.ToLower() == "windows-ad")
        {
            // First check if any enabled tool servers exist at all
            var hasEnabledToolServer = await db.ToolServers
                .AnyAsync(ts => ts.IsEnabled);

            if (!hasEnabledToolServer)
            {
                return new ToolServerLookupResult(null, ToolServerLookupReason.NoToolServer);
            }

            // Tool server exists - check for ad-* capability mappings
            var mapping = await db.CapabilityMappings
                .Include(m => m.ToolServer)
                .Where(m => m.ToolServer != null &&
                            m.ToolServer.IsEnabled &&
                            m.CapabilityId.StartsWith("ad-"))
                .FirstOrDefaultAsync();

            if (mapping?.ToolServer == null)
            {
                return new ToolServerLookupResult(null, ToolServerLookupReason.NoCapabilityMapping);
            }

            return new ToolServerLookupResult(mapping.ToolServer, ToolServerLookupReason.Found);
        }

        return new ToolServerLookupResult(null, ToolServerLookupReason.NoToolServer);
    }

    private record ToolServerLookupResult(ToolServer? ToolServer, ToolServerLookupReason Reason);

    private enum ToolServerLookupReason
    {
        Found,
        NoToolServer,
        NoCapabilityMapping
    }

    // Response model for Tool Server's test-connection endpoint
    private record ToolServerTestConnectionResponse(
        bool Success,
        string Message,
        string? Details,
        DateTime TestedAt
    );
}
