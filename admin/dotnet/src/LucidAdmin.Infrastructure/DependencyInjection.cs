using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Providers;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Credentials;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Infrastructure.Data.Seeding;
using LucidAdmin.Infrastructure.Providers;
using LucidAdmin.Infrastructure.Providers.Capabilities;
using LucidAdmin.Infrastructure.Repositories;
using LucidAdmin.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var databaseProvider = configuration["Database:Provider"] ?? "Sqlite";

        services.AddDbContext<LucidDbContext>(options =>
        {
            if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString ?? "Data Source=lucid-admin-dev.db");

                // Add interceptor to ensure foreign keys are enabled for every SQLite connection
                options.AddInterceptors(new SqliteForeignKeysInterceptor());
            }
        });

        // Repositories
        services.AddScoped<IServiceAccountRepository, ServiceAccountRepository>();
        services.AddScoped<IToolServerRepository, ToolServerRepository>();
        services.AddScoped<ICapabilityRepository, CapabilityRepository>();
        services.AddScoped<ICapabilityMappingRepository, CapabilityMappingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IRulesetRepository, RulesetRepository>();
        services.AddScoped<IExampleSetRepository, ExampleSetRepository>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();

        // Services
        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();

        // Encryption
        services.AddSingleton<IEncryptionService, EncryptionService>();

        // Credential Providers
        services.AddScoped<ICredentialProvider, DatabaseCredentialProvider>();
        services.AddScoped<ICredentialProvider, NoneCredentialProvider>();
        services.AddScoped<ICredentialProvider, EnvironmentCredentialProvider>();

        // Credential Registry and Service
        services.AddScoped<ICredentialProviderRegistry, CredentialProviderRegistry>();
        services.AddScoped<ICredentialService, CredentialService>();

        // Service Account Providers (with credential service injection)
        services.AddScoped<IServiceAccountProvider>(sp =>
            new ServiceNowProvider(
                sp.GetRequiredService<ICredentialService>(),
                sp.GetRequiredService<ILogger<ServiceNowProvider>>()));

        services.AddScoped<IServiceAccountProvider>(sp =>
            new OpenAiProvider(
                sp.GetRequiredService<ICredentialService>(),
                sp.GetRequiredService<ILogger<OpenAiProvider>>()));

        services.AddScoped<IServiceAccountProvider>(sp =>
            new AnthropicProvider(
                sp.GetRequiredService<ICredentialService>(),
                sp.GetRequiredService<ILogger<AnthropicProvider>>()));

        services.AddScoped<IServiceAccountProvider>(sp =>
            new WindowsAdProvider(
                sp.GetRequiredService<ICredentialService>(),
                sp.GetRequiredService<ILogger<WindowsAdProvider>>()));

        services.AddScoped<IServiceAccountProvider>(sp =>
            new AzureProvider(
                sp.GetRequiredService<ICredentialService>(),
                sp.GetRequiredService<ILogger<AzureProvider>>()));

        // Providers that don't need credential service (changed to Scoped for lifetime consistency)
        services.AddScoped<IServiceAccountProvider, LinuxProvider>();
        services.AddScoped<IServiceAccountProvider, AwsProvider>();
        services.AddScoped<IServiceAccountProvider, OllamaProvider>();
        services.AddScoped<IServiceAccountProvider, AzureOpenAiProvider>();
        services.AddScoped<IServiceAccountProvider, BedrockProvider>();

        // Service Account Provider Registry (changed to Scoped to match provider lifetimes)
        services.AddScoped<IProviderRegistry, ProviderRegistry>();

        // Capability Providers
        services.AddSingleton<ICapabilityProvider, AdUserLookupCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, AdPasswordResetCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, AdGroupAddCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, AdGroupRemoveCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, NtfsPermissionGrantCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, NtfsPermissionRevokeCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, ServiceNowConnectorCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, AzureUserLookupCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, AzureVmLookupCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, AdComputerLookupCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider, RemoteSoftwareInstallCapabilityProvider>();

        // Capability Registry
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();

        return services;
    }
}
