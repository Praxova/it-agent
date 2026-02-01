# Lucid Admin Portal - Project Structure

## Overview

The Lucid Admin Portal provides centralized management for the Lucid IT Agent ecosystem. It combines a Config Service API with a Blazor Server web UI in a single deployment.

## Solution Structure

```
lucid-it-agent/
└── admin/
    └── dotnet/
        ├── LucidAdmin.sln
        ├── README.md
        ├── Dockerfile
        ├── docker-compose.yml
        ├── .dockerignore
        │
        ├── src/
        │   │
        │   ├── LucidAdmin.Core/                    # Domain Layer
        │   │   ├── LucidAdmin.Core.csproj
        │   │   │
        │   │   ├── Entities/
        │   │   │   ├── ServiceAccount.cs
        │   │   │   ├── ToolServer.cs
        │   │   │   ├── CapabilityMapping.cs
        │   │   │   ├── User.cs
        │   │   │   ├── AuditEvent.cs
        │   │   │   ├── RuleConfiguration.cs
        │   │   │   └── BaseEntity.cs               # Common fields (Id, CreatedAt, UpdatedAt)
        │   │   │
        │   │   ├── Enums/
        │   │   │   ├── CredentialType.cs           # gMSA, ServiceAccount, CurrentUser
        │   │   │   ├── ToolCapability.cs           # PasswordReset, GroupManagement, FilePermissions
        │   │   │   ├── HealthStatus.cs             # Healthy, Degraded, Unhealthy, Unknown
        │   │   │   ├── UserRole.cs                 # Admin, Operator, Viewer
        │   │   │   └── AuditAction.cs              # PasswordReset, GroupAdd, GroupRemove, etc.
        │   │   │
        │   │   ├── Interfaces/
        │   │   │   ├── Repositories/
        │   │   │   │   ├── IRepository.cs          # Generic base repository
        │   │   │   │   ├── IServiceAccountRepository.cs
        │   │   │   │   ├── IToolServerRepository.cs
        │   │   │   │   ├── ICapabilityMappingRepository.cs
        │   │   │   │   ├── IUserRepository.cs
        │   │   │   │   └── IAuditEventRepository.cs
        │   │   │   │
        │   │   │   └── Services/
        │   │   │       ├── IPasswordHasher.cs
        │   │   │       └── ITokenService.cs
        │   │   │
        │   │   ├── ValueObjects/
        │   │   │   ├── ScopeDefinition.cs          # Allowed/Denied scopes
        │   │   │   └── HealthCheckResult.cs
        │   │   │
        │   │   └── Exceptions/
        │   │       ├── LucidException.cs           # Base exception
        │   │       ├── EntityNotFoundException.cs
        │   │       ├── DuplicateEntityException.cs
        │   │       ├── ValidationException.cs
        │   │       └── AuthenticationException.cs
        │   │
        │   ├── LucidAdmin.Infrastructure/          # Data Access Layer
        │   │   ├── LucidAdmin.Infrastructure.csproj
        │   │   │
        │   │   ├── Data/
        │   │   │   ├── LucidDbContext.cs
        │   │   │   ├── DesignTimeDbContextFactory.cs   # For EF migrations
        │   │   │   │
        │   │   │   ├── Configurations/             # EF Core entity configurations
        │   │   │   │   ├── ServiceAccountConfiguration.cs
        │   │   │   │   ├── ToolServerConfiguration.cs
        │   │   │   │   ├── CapabilityMappingConfiguration.cs
        │   │   │   │   ├── UserConfiguration.cs
        │   │   │   │   └── AuditEventConfiguration.cs
        │   │   │   │
        │   │   │   └── Migrations/                 # EF Core migrations (auto-generated)
        │   │   │       └── ...
        │   │   │
        │   │   ├── Repositories/
        │   │   │   ├── RepositoryBase.cs           # Generic implementation
        │   │   │   ├── ServiceAccountRepository.cs
        │   │   │   ├── ToolServerRepository.cs
        │   │   │   ├── CapabilityMappingRepository.cs
        │   │   │   ├── UserRepository.cs
        │   │   │   └── AuditEventRepository.cs
        │   │   │
        │   │   ├── Services/
        │   │   │   ├── Argon2PasswordHasher.cs
        │   │   │   └── JwtTokenService.cs
        │   │   │
        │   │   └── DependencyInjection.cs          # Extension method for DI registration
        │   │
        │   └── LucidAdmin.Web/                     # Combined API + Blazor UI
        │       ├── LucidAdmin.Web.csproj
        │       ├── Program.cs
        │       ├── appsettings.json
        │       ├── appsettings.Development.json
        │       │
        │       ├── Api/                            # Config Service API
        │       │   ├── Endpoints/
        │       │   │   ├── AuthEndpoints.cs
        │       │   │   ├── ServiceAccountEndpoints.cs
        │       │   │   ├── ToolServerEndpoints.cs
        │       │   │   ├── CapabilityEndpoints.cs
        │       │   │   ├── AuditEndpoints.cs
        │       │   │   └── HealthEndpoints.cs
        │       │   │
        │       │   ├── Models/
        │       │   │   ├── Requests/
        │       │   │   │   ├── LoginRequest.cs
        │       │   │   │   ├── CreateServiceAccountRequest.cs
        │       │   │   │   ├── UpdateServiceAccountRequest.cs
        │       │   │   │   ├── RegisterToolServerRequest.cs
        │       │   │   │   ├── HeartbeatRequest.cs
        │       │   │   │   ├── CreateCapabilityMappingRequest.cs
        │       │   │   │   └── SubmitAuditEventRequest.cs
        │       │   │   │
        │       │   │   └── Responses/
        │       │   │       ├── AuthResponse.cs
        │       │   │       ├── ServiceAccountResponse.cs
        │       │   │       ├── ToolServerResponse.cs
        │       │   │       ├── ToolServerConfigResponse.cs
        │       │   │       ├── CapabilityMappingResponse.cs
        │       │   │       ├── AuditEventResponse.cs
        │       │   │       ├── HealthSummaryResponse.cs
        │       │   │       └── ErrorResponse.cs
        │       │   │
        │       │   └── Middleware/
        │       │       ├── ExceptionHandlingMiddleware.cs
        │       │       └── RequestLoggingMiddleware.cs
        │       │
        │       ├── Components/                     # Blazor Components
        │       │   ├── App.razor
        │       │   ├── Routes.razor
        │       │   │
        │       │   ├── Layout/
        │       │   │   ├── MainLayout.razor
        │       │   │   ├── MainLayout.razor.css
        │       │   │   ├── NavMenu.razor
        │       │   │   └── NavMenu.razor.css
        │       │   │
        │       │   ├── Pages/
        │       │   │   ├── Home.razor                      # Dashboard
        │       │   │   ├── Login.razor
        │       │   │   ├── Logout.razor
        │       │   │   │
        │       │   │   ├── ServiceAccounts/
        │       │   │   │   ├── Index.razor                 # List
        │       │   │   │   ├── Create.razor
        │       │   │   │   ├── Edit.razor
        │       │   │   │   └── _ServiceAccountForm.razor   # Shared form component
        │       │   │   │
        │       │   │   ├── ToolServers/
        │       │   │   │   ├── Index.razor                 # List with health status
        │       │   │   │   └── Details.razor               # Server details + capabilities
        │       │   │   │
        │       │   │   ├── Capabilities/
        │       │   │   │   ├── Index.razor                 # List all mappings
        │       │   │   │   ├── Create.razor
        │       │   │   │   └── Edit.razor
        │       │   │   │
        │       │   │   ├── AuditLog/
        │       │   │   │   └── Index.razor                 # Searchable audit log
        │       │   │   │
        │       │   │   └── Settings/
        │       │   │       ├── Index.razor
        │       │   │       ├── Users.razor                 # User management
        │       │   │       └── About.razor
        │       │   │
        │       │   └── Shared/
        │       │       ├── StatusBadge.razor               # Health status badge
        │       │       ├── ConfirmDialog.razor             # Confirmation modal
        │       │       ├── LoadingSpinner.razor
        │       │       ├── ErrorAlert.razor
        │       │       └── Pagination.razor
        │       │
        │       ├── Services/
        │       │   ├── AuthStateProvider.cs                # Blazor auth state
        │       │   └── ToastService.cs                     # Notifications
        │       │
        │       └── wwwroot/
        │           ├── css/
        │           │   ├── app.css
        │           │   └── bootstrap/                      # Or Tailwind
        │           ├── js/
        │           │   └── app.js
        │           ├── images/
        │           │   └── logo.svg
        │           └── favicon.ico
        │
        └── tests/
            ├── LucidAdmin.Core.Tests/
            │   ├── LucidAdmin.Core.Tests.csproj
            │   └── Entities/
            │       └── ServiceAccountTests.cs
            │
            ├── LucidAdmin.Infrastructure.Tests/
            │   ├── LucidAdmin.Infrastructure.Tests.csproj
            │   └── Repositories/
            │       ├── ServiceAccountRepositoryTests.cs
            │       └── ...
            │
            └── LucidAdmin.Web.Tests/
                ├── LucidAdmin.Web.Tests.csproj
                ├── Api/
                │   └── ServiceAccountEndpointsTests.cs
                └── Components/
                    └── ...                                  # bUnit tests
```

## Layer Dependencies

```
┌─────────────────────────────────────────────────────────────────────┐
│                        LucidAdmin.Web                                │
│              (API Endpoints + Blazor UI + Composition Root)          │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     LucidAdmin.Infrastructure                        │
│           (EF Core, Repositories, External Services)                 │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        LucidAdmin.Core                               │
│              (Entities, Interfaces, Exceptions - NO DEPENDENCIES)    │
└─────────────────────────────────────────────────────────────────────┘
```

## Key Design Patterns

### Repository Pattern

```csharp
// Core/Interfaces/Repositories/IRepository.cs
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// Core/Interfaces/Repositories/IServiceAccountRepository.cs
public interface IServiceAccountRepository : IRepository<ServiceAccount>
{
    Task<IEnumerable<ServiceAccount>> GetByDomainAsync(string domain, CancellationToken ct = default);
    Task<bool> ExistsAsync(string name, string domain, CancellationToken ct = default);
}

// Infrastructure/Repositories/RepositoryBase.cs
public abstract class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly LucidDbContext Context;
    protected readonly DbSet<T> DbSet;

    protected RepositoryBase(LucidDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await DbSet.FindAsync(new object[] { id }, ct);

    // ... other implementations
}
```

### Database Provider Abstraction

```csharp
// Program.cs
builder.Services.AddDbContext<LucidDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var provider = config["Database:Provider"] ?? "SQLite";
    var connectionString = config.GetConnectionString("DefaultConnection");

    _ = provider.ToLower() switch
    {
        "sqlite" => options.UseSqlite(connectionString),
        "sqlserver" => options.UseSqlServer(connectionString),
        "postgresql" => options.UseNpgsql(connectionString),
        _ => throw new InvalidOperationException($"Unsupported database provider: {provider}")
    };
});
```

### Configuration

```json
// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=lucidadmin.db"
  },
  "Database": {
    "Provider": "SQLite"
  },
  "Jwt": {
    "SecretKey": "CHANGE-THIS-IN-PRODUCTION-minimum-32-characters",
    "Issuer": "LucidAdmin",
    "Audience": "LucidAdminUsers",
    "ExpirationMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### Dependency Injection Registration

```csharp
// Infrastructure/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<LucidDbContext>((sp, options) =>
        {
            var provider = configuration["Database:Provider"] ?? "SQLite";
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            _ = provider.ToLower() switch
            {
                "sqlite" => options.UseSqlite(connectionString),
                "sqlserver" => options.UseSqlServer(connectionString),
                "postgresql" => options.UseNpgsql(connectionString),
                _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
            };
        });

        // Repositories
        services.AddScoped<IServiceAccountRepository, ServiceAccountRepository>();
        services.AddScoped<IToolServerRepository, ToolServerRepository>();
        services.AddScoped<ICapabilityMappingRepository, CapabilityMappingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();

        // Services
        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        return services;
    }
}

// Program.cs usage
builder.Services.AddInfrastructure(builder.Configuration);
```

## Running the Project

### Development

```bash
# From admin/dotnet directory
cd admin/dotnet

# Restore packages
dotnet restore

# Create initial migration
dotnet ef migrations add InitialCreate \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web \
    --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web

# Run the application
cd src/LucidAdmin.Web
dotnet run

# Access at https://localhost:5001
```

### Docker

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/LucidAdmin.Core/LucidAdmin.Core.csproj", "LucidAdmin.Core/"]
COPY ["src/LucidAdmin.Infrastructure/LucidAdmin.Infrastructure.csproj", "LucidAdmin.Infrastructure/"]
COPY ["src/LucidAdmin.Web/LucidAdmin.Web.csproj", "LucidAdmin.Web/"]
RUN dotnet restore "LucidAdmin.Web/LucidAdmin.Web.csproj"
COPY src/ .
WORKDIR "/src/LucidAdmin.Web"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "LucidAdmin.Web.dll"]
```

```bash
# Build and run
docker build -t lucid-admin:dev -f Dockerfile .
docker run -d -p 5000:5000 -v lucidadmin-data:/app/data --name lucid-admin lucid-admin:dev
```

## Testing Strategy

### Unit Tests (Core)
- Entity validation logic
- Value object behavior
- Domain exceptions

### Integration Tests (Infrastructure)
- Repository operations with in-memory SQLite
- Service implementations

### API Tests (Web)
- Endpoint behavior with WebApplicationFactory
- Authentication and authorization
- Request validation

### Component Tests (Blazor)
- bUnit for Razor component testing
- Page rendering and interaction

## NuGet Packages

### LucidAdmin.Core
```xml
<ItemGroup>
  <!-- No external dependencies - pure domain layer -->
</ItemGroup>
```

### LucidAdmin.Infrastructure
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.x" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.x" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.x" />
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.x" />
  <PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.3.x" />
</ItemGroup>
```

### LucidAdmin.Web
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.x" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="6.x.x" />
  <PackageReference Include="Serilog.AspNetCore" Version="8.x.x" />
</ItemGroup>
```
