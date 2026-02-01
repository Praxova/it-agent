# Claude Code Prompt: Lucid Admin Portal - Foundation Scaffold

## Overview

Create the Lucid Admin Portal solution - a centralized management system for the Lucid IT Agent ecosystem. This includes a Config Service API and the foundation for a Blazor Server web UI.

**Location**: `/home/alton/Documents/lucid-it-agent/admin/dotnet/`

**Reference Documentation**:
- `docs/adr/ADR-006-admin-portal-config-service.md` - Architecture decisions
- `docs/ADMIN_PROJECT_STRUCTURE.md` - Detailed project structure

## Solution Structure

Create the following solution structure:

```
admin/dotnet/
├── LucidAdmin.sln
├── README.md
├── .gitignore
│
├── src/
│   ├── LucidAdmin.Core/
│   ├── LucidAdmin.Infrastructure/
│   └── LucidAdmin.Web/
│
└── tests/
    ├── LucidAdmin.Core.Tests/
    ├── LucidAdmin.Infrastructure.Tests/
    └── LucidAdmin.Web.Tests/
```

---

## Project 1: LucidAdmin.Core

**Purpose**: Domain layer with entities, interfaces, and exceptions. NO external dependencies.

### LucidAdmin.Core.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>LucidAdmin.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

### Entities/BaseEntity.cs

```csharp
namespace LucidAdmin.Core.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### Enums/CredentialType.cs

```csharp
namespace LucidAdmin.Core.Enums;

public enum CredentialType
{
    /// <summary>
    /// Group Managed Service Account - preferred for production
    /// </summary>
    GroupManagedServiceAccount,
    
    /// <summary>
    /// Traditional service account with password
    /// </summary>
    ServiceAccount,
    
    /// <summary>
    /// Current user context - for development/testing only
    /// </summary>
    CurrentUser
}
```

### Enums/ToolCapability.cs

```csharp
namespace LucidAdmin.Core.Enums;

public enum ToolCapability
{
    PasswordReset,
    GroupManagement,
    FilePermissions
}
```

### Enums/HealthStatus.cs

```csharp
namespace LucidAdmin.Core.Enums;

public enum HealthStatus
{
    /// <summary>
    /// All systems operational
    /// </summary>
    Healthy,
    
    /// <summary>
    /// Some capabilities impaired but operational
    /// </summary>
    Degraded,
    
    /// <summary>
    /// Major functionality broken
    /// </summary>
    Unhealthy,
    
    /// <summary>
    /// No recent heartbeat or status unknown
    /// </summary>
    Unknown
}
```

### Enums/UserRole.cs

```csharp
namespace LucidAdmin.Core.Enums;

public enum UserRole
{
    /// <summary>
    /// Full access to all features
    /// </summary>
    Admin,
    
    /// <summary>
    /// Can view and perform operations, cannot change settings
    /// </summary>
    Operator,
    
    /// <summary>
    /// Read-only access
    /// </summary>
    Viewer
}
```

### Enums/AuditAction.cs

```csharp
namespace LucidAdmin.Core.Enums;

public enum AuditAction
{
    // Password operations
    PasswordReset,
    PasswordResetFailed,
    
    // Group operations
    GroupMemberAdded,
    GroupMemberRemoved,
    GroupOperationFailed,
    
    // File permission operations
    PermissionGranted,
    PermissionRevoked,
    PermissionOperationFailed,
    
    // Admin operations
    ServiceAccountCreated,
    ServiceAccountUpdated,
    ServiceAccountDeleted,
    ToolServerRegistered,
    ToolServerDeregistered,
    CapabilityMappingCreated,
    CapabilityMappingUpdated,
    CapabilityMappingDeleted,
    UserLogin,
    UserLogout,
    UserCreated,
    UserUpdated,
    UserDeleted
}
```

### Entities/ServiceAccount.cs

```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class ServiceAccount : BaseEntity
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public required string Domain { get; set; }
    public CredentialType CredentialType { get; set; } = CredentialType.GroupManagedServiceAccount;
    
    /// <summary>
    /// For ServiceAccount type: "environment", "vault", or "config"
    /// </summary>
    public string? CredentialSource { get; set; }
    
    /// <summary>
    /// For ServiceAccount type: environment variable name or vault path
    /// </summary>
    public string? CredentialKey { get; set; }
    
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    
    // Health tracking
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;
    public DateTime? LastHealthCheck { get; set; }
    public string? LastHealthMessage { get; set; }
    
    // Navigation
    public ICollection<CapabilityMapping> CapabilityMappings { get; set; } = new List<CapabilityMapping>();
}
```

### Entities/ToolServer.cs

```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class ToolServer : BaseEntity
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public required string Endpoint { get; set; }
    public required string Domain { get; set; }
    public string? Description { get; set; }
    
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public DateTime? LastHeartbeat { get; set; }
    public string? Version { get; set; }
    
    /// <summary>
    /// API key for tool server authentication (hashed)
    /// </summary>
    public string? ApiKeyHash { get; set; }
    
    public bool IsEnabled { get; set; } = true;
    
    // Navigation
    public ICollection<CapabilityMapping> CapabilityMappings { get; set; } = new List<CapabilityMapping>();
    public ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();
}
```

### Entities/CapabilityMapping.cs

```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class CapabilityMapping : BaseEntity
{
    public Guid ServiceAccountId { get; set; }
    public Guid ToolServerId { get; set; }
    public ToolCapability Capability { get; set; }
    
    /// <summary>
    /// JSON array of allowed scopes (OUs, group patterns, paths)
    /// </summary>
    public string? AllowedScopesJson { get; set; }
    
    /// <summary>
    /// JSON array of denied scopes (explicit denies)
    /// </summary>
    public string? DeniedScopesJson { get; set; }
    
    public bool IsEnabled { get; set; } = true;
    
    // Navigation
    public ServiceAccount? ServiceAccount { get; set; }
    public ToolServer? ToolServer { get; set; }
}
```

### Entities/User.cs

```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class User : BaseEntity
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEnd { get; set; }
}
```

### Entities/AuditEvent.cs

```csharp
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Entities;

public class AuditEvent : BaseEntity
{
    public Guid? ToolServerId { get; set; }
    public AuditAction Action { get; set; }
    public ToolCapability? Capability { get; set; }
    
    /// <summary>
    /// The user or service that performed the action
    /// </summary>
    public string? PerformedBy { get; set; }
    
    /// <summary>
    /// Target of the action (username, group name, path, etc.)
    /// </summary>
    public string? TargetResource { get; set; }
    
    /// <summary>
    /// Associated ticket number if applicable
    /// </summary>
    public string? TicketNumber { get; set; }
    
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Additional details as JSON
    /// </summary>
    public string? DetailsJson { get; set; }
    
    // Navigation
    public ToolServer? ToolServer { get; set; }
}
```

### Interfaces/Repositories/IRepository.cs

```csharp
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
```

### Interfaces/Repositories/IServiceAccountRepository.cs

```csharp
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IServiceAccountRepository : IRepository<ServiceAccount>
{
    Task<IEnumerable<ServiceAccount>> GetByDomainAsync(string domain, CancellationToken ct = default);
    Task<ServiceAccount?> GetByNameAndDomainAsync(string name, string domain, CancellationToken ct = default);
    Task<IEnumerable<ServiceAccount>> GetEnabledAsync(CancellationToken ct = default);
}
```

### Interfaces/Repositories/IToolServerRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IToolServerRepository : IRepository<ToolServer>
{
    Task<ToolServer?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<ToolServer?> GetByEndpointAsync(string endpoint, CancellationToken ct = default);
    Task<IEnumerable<ToolServer>> GetByStatusAsync(HealthStatus status, CancellationToken ct = default);
    Task<IEnumerable<ToolServer>> GetEnabledAsync(CancellationToken ct = default);
    Task UpdateHeartbeatAsync(Guid id, HealthStatus status, CancellationToken ct = default);
}
```

### Interfaces/Repositories/ICapabilityMappingRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface ICapabilityMappingRepository : IRepository<CapabilityMapping>
{
    Task<IEnumerable<CapabilityMapping>> GetByToolServerIdAsync(Guid toolServerId, CancellationToken ct = default);
    Task<IEnumerable<CapabilityMapping>> GetByServiceAccountIdAsync(Guid serviceAccountId, CancellationToken ct = default);
    Task<CapabilityMapping?> GetByToolServerAndCapabilityAsync(Guid toolServerId, ToolCapability capability, CancellationToken ct = default);
}
```

### Interfaces/Repositories/IUserRepository.cs

```csharp
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IEnumerable<User>> GetEnabledAsync(CancellationToken ct = default);
    Task UpdateLastLoginAsync(Guid id, CancellationToken ct = default);
    Task IncrementFailedLoginAsync(Guid id, CancellationToken ct = default);
    Task ResetFailedLoginAsync(Guid id, CancellationToken ct = default);
}
```

### Interfaces/Repositories/IAuditEventRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IAuditEventRepository : IRepository<AuditEvent>
{
    Task<IEnumerable<AuditEvent>> GetByToolServerIdAsync(Guid toolServerId, int limit = 100, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> GetByActionAsync(AuditAction action, int limit = 100, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> GetByDateRangeAsync(DateTime from, DateTime to, int limit = 1000, CancellationToken ct = default);
    Task<IEnumerable<AuditEvent>> SearchAsync(string? targetResource, AuditAction? action, Guid? toolServerId, DateTime? from, DateTime? to, int limit = 100, CancellationToken ct = default);
}
```

### Interfaces/Services/IPasswordHasher.cs

```csharp
namespace LucidAdmin.Core.Interfaces.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}
```

### Interfaces/Services/ITokenService.cs

```csharp
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Services;

public interface ITokenService
{
    string GenerateToken(User user);
    bool ValidateToken(string token);
    Guid? GetUserIdFromToken(string token);
}
```

### Exceptions/LucidException.cs

```csharp
namespace LucidAdmin.Core.Exceptions;

public class LucidException : Exception
{
    public LucidException(string message) : base(message) { }
    public LucidException(string message, Exception inner) : base(message, inner) { }
}
```

### Exceptions/EntityNotFoundException.cs

```csharp
namespace LucidAdmin.Core.Exceptions;

public class EntityNotFoundException : LucidException
{
    public string EntityType { get; }
    public Guid EntityId { get; }

    public EntityNotFoundException(string entityType, Guid entityId)
        : base($"{entityType} with ID {entityId} was not found.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
```

### Exceptions/DuplicateEntityException.cs

```csharp
namespace LucidAdmin.Core.Exceptions;

public class DuplicateEntityException : LucidException
{
    public string EntityType { get; }
    public string DuplicateKey { get; }

    public DuplicateEntityException(string entityType, string duplicateKey)
        : base($"{entityType} with key '{duplicateKey}' already exists.")
    {
        EntityType = entityType;
        DuplicateKey = duplicateKey;
    }
}
```

### Exceptions/ValidationException.cs

```csharp
namespace LucidAdmin.Core.Exceptions;

public class ValidationException : LucidException
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : base($"Validation error: {error}")
    {
        Errors = new Dictionary<string, string[]> { { field, new[] { error } } };
    }
}
```

### Exceptions/AuthenticationException.cs

```csharp
namespace LucidAdmin.Core.Exceptions;

public class AuthenticationException : LucidException
{
    public AuthenticationException(string message) : base(message) { }
}
```

---

## Project 2: LucidAdmin.Infrastructure

**Purpose**: Data access layer with EF Core and repository implementations.

### LucidAdmin.Infrastructure.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>LucidAdmin.Infrastructure</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.3.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LucidAdmin.Core\LucidAdmin.Core.csproj" />
  </ItemGroup>
</Project>
```

### Data/LucidDbContext.cs

```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Data;

public class LucidDbContext : DbContext
{
    public LucidDbContext(DbContextOptions<LucidDbContext> options) : base(options) { }

    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();
    public DbSet<ToolServer> ToolServers => Set<ToolServer>();
    public DbSet<CapabilityMapping> CapabilityMappings => Set<CapabilityMapping>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LucidDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
```

### Data/Configurations/ServiceAccountConfiguration.cs

```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ServiceAccountConfiguration : IEntityTypeConfiguration<ServiceAccount>
{
    public void Configure(EntityTypeBuilder<ServiceAccount> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);
            
        builder.Property(e => e.Domain)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(e => e.CredentialSource)
            .HasMaxLength(50);
            
        builder.Property(e => e.CredentialKey)
            .HasMaxLength(500);
            
        builder.Property(e => e.Description)
            .HasMaxLength(1000);
            
        builder.Property(e => e.LastHealthMessage)
            .HasMaxLength(1000);

        builder.HasIndex(e => new { e.Name, e.Domain }).IsUnique();
    }
}
```

### Data/Configurations/ToolServerConfiguration.cs

```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ToolServerConfiguration : IEntityTypeConfiguration<ToolServer>
{
    public void Configure(EntityTypeBuilder<ToolServer> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);
            
        builder.Property(e => e.Endpoint)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(e => e.Domain)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(e => e.Description)
            .HasMaxLength(1000);
            
        builder.Property(e => e.Version)
            .HasMaxLength(50);
            
        builder.Property(e => e.ApiKeyHash)
            .HasMaxLength(500);

        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.Endpoint).IsUnique();
    }
}
```

### Data/Configurations/CapabilityMappingConfiguration.cs

```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class CapabilityMappingConfiguration : IEntityTypeConfiguration<CapabilityMapping>
{
    public void Configure(EntityTypeBuilder<CapabilityMapping> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.AllowedScopesJson)
            .HasMaxLength(4000);
            
        builder.Property(e => e.DeniedScopesJson)
            .HasMaxLength(4000);

        builder.HasOne(e => e.ServiceAccount)
            .WithMany(s => s.CapabilityMappings)
            .HasForeignKey(e => e.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ToolServer)
            .WithMany(t => t.CapabilityMappings)
            .HasForeignKey(e => e.ToolServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.ToolServerId, e.Capability }).IsUnique();
    }
}
```

### Data/Configurations/UserConfiguration.cs

```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Username)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(e => e.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(e => e.Username).IsUnique();
        builder.HasIndex(e => e.Email).IsUnique();
    }
}
```

### Data/Configurations/AuditEventConfiguration.cs

```csharp
using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.PerformedBy)
            .HasMaxLength(255);
            
        builder.Property(e => e.TargetResource)
            .HasMaxLength(500);
            
        builder.Property(e => e.TicketNumber)
            .HasMaxLength(100);
            
        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);
            
        builder.Property(e => e.DetailsJson)
            .HasMaxLength(4000);

        builder.HasOne(e => e.ToolServer)
            .WithMany(t => t.AuditEvents)
            .HasForeignKey(e => e.ToolServerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.ToolServerId);
    }
}
```

### Repositories/RepositoryBase.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

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
    {
        return await DbSet.FindAsync(new object[] { id }, ct);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
    {
        return await DbSet.ToListAsync(ct);
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await DbSet.AddAsync(entity, ct);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public virtual async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        DbSet.Update(entity);
        await Context.SaveChangesAsync(ct);
    }

    public virtual async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity != null)
        {
            DbSet.Remove(entity);
            await Context.SaveChangesAsync(ct);
        }
    }

    public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet.AnyAsync(e => e.Id == id, ct);
    }
}
```

### Repositories/ServiceAccountRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class ServiceAccountRepository : RepositoryBase<ServiceAccount>, IServiceAccountRepository
{
    public ServiceAccountRepository(LucidDbContext context) : base(context) { }

    public async Task<IEnumerable<ServiceAccount>> GetByDomainAsync(string domain, CancellationToken ct = default)
    {
        return await DbSet
            .Where(s => s.Domain == domain)
            .ToListAsync(ct);
    }

    public async Task<ServiceAccount?> GetByNameAndDomainAsync(string name, string domain, CancellationToken ct = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(s => s.Name == name && s.Domain == domain, ct);
    }

    public async Task<IEnumerable<ServiceAccount>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await DbSet
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);
    }
}
```

### Repositories/ToolServerRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class ToolServerRepository : RepositoryBase<ToolServer>, IToolServerRepository
{
    public ToolServerRepository(LucidDbContext context) : base(context) { }

    public async Task<ToolServer?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await DbSet.FirstOrDefaultAsync(t => t.Name == name, ct);
    }

    public async Task<ToolServer?> GetByEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        return await DbSet.FirstOrDefaultAsync(t => t.Endpoint == endpoint, ct);
    }

    public async Task<IEnumerable<ToolServer>> GetByStatusAsync(HealthStatus status, CancellationToken ct = default)
    {
        return await DbSet.Where(t => t.Status == status).ToListAsync(ct);
    }

    public async Task<IEnumerable<ToolServer>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await DbSet.Where(t => t.IsEnabled).ToListAsync(ct);
    }

    public async Task UpdateHeartbeatAsync(Guid id, HealthStatus status, CancellationToken ct = default)
    {
        var server = await GetByIdAsync(id, ct);
        if (server != null)
        {
            server.Status = status;
            server.LastHeartbeat = DateTime.UtcNow;
            await Context.SaveChangesAsync(ct);
        }
    }
}
```

### Repositories/CapabilityMappingRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class CapabilityMappingRepository : RepositoryBase<CapabilityMapping>, ICapabilityMappingRepository
{
    public CapabilityMappingRepository(LucidDbContext context) : base(context) { }

    public override async Task<CapabilityMapping?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet
            .Include(m => m.ServiceAccount)
            .Include(m => m.ToolServer)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<IEnumerable<CapabilityMapping>> GetByToolServerIdAsync(Guid toolServerId, CancellationToken ct = default)
    {
        return await DbSet
            .Include(m => m.ServiceAccount)
            .Where(m => m.ToolServerId == toolServerId)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<CapabilityMapping>> GetByServiceAccountIdAsync(Guid serviceAccountId, CancellationToken ct = default)
    {
        return await DbSet
            .Include(m => m.ToolServer)
            .Where(m => m.ServiceAccountId == serviceAccountId)
            .ToListAsync(ct);
    }

    public async Task<CapabilityMapping?> GetByToolServerAndCapabilityAsync(Guid toolServerId, ToolCapability capability, CancellationToken ct = default)
    {
        return await DbSet
            .Include(m => m.ServiceAccount)
            .FirstOrDefaultAsync(m => m.ToolServerId == toolServerId && m.Capability == capability, ct);
    }
}
```

### Repositories/UserRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class UserRepository : RepositoryBase<User>, IUserRepository
{
    public UserRepository(LucidDbContext context) : base(context) { }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        return await DbSet.FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return await DbSet.FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<IEnumerable<User>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await DbSet.Where(u => u.IsEnabled).ToListAsync(ct);
    }

    public async Task UpdateLastLoginAsync(Guid id, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(id, ct);
        if (user != null)
        {
            user.LastLogin = DateTime.UtcNow;
            await Context.SaveChangesAsync(ct);
        }
    }

    public async Task IncrementFailedLoginAsync(Guid id, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(id, ct);
        if (user != null)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
            }
            await Context.SaveChangesAsync(ct);
        }
    }

    public async Task ResetFailedLoginAsync(Guid id, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(id, ct);
        if (user != null)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await Context.SaveChangesAsync(ct);
        }
    }
}
```

### Repositories/AuditEventRepository.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class AuditEventRepository : RepositoryBase<AuditEvent>, IAuditEventRepository
{
    public AuditEventRepository(LucidDbContext context) : base(context) { }

    public async Task<IEnumerable<AuditEvent>> GetByToolServerIdAsync(Guid toolServerId, int limit = 100, CancellationToken ct = default)
    {
        return await DbSet
            .Where(e => e.ToolServerId == toolServerId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> GetByActionAsync(AuditAction action, int limit = 100, CancellationToken ct = default)
    {
        return await DbSet
            .Where(e => e.Action == action)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> GetByDateRangeAsync(DateTime from, DateTime to, int limit = 1000, CancellationToken ct = default)
    {
        return await DbSet
            .Where(e => e.CreatedAt >= from && e.CreatedAt <= to)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditEvent>> SearchAsync(
        string? targetResource, 
        AuditAction? action, 
        Guid? toolServerId, 
        DateTime? from, 
        DateTime? to, 
        int limit = 100, 
        CancellationToken ct = default)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(targetResource))
            query = query.Where(e => e.TargetResource != null && e.TargetResource.Contains(targetResource));

        if (action.HasValue)
            query = query.Where(e => e.Action == action.Value);

        if (toolServerId.HasValue)
            query = query.Where(e => e.ToolServerId == toolServerId.Value);

        if (from.HasValue)
            query = query.Where(e => e.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
```

### Services/Argon2PasswordHasher.cs

```csharp
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LucidAdmin.Core.Interfaces.Services;

namespace LucidAdmin.Infrastructure.Services;

public class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 4;
    private const int MemorySize = 65536; // 64 MB
    private const int DegreeOfParallelism = 2;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashWithArgon2(password, salt);
        
        // Combine salt and hash
        var result = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, result, SaltSize, HashSize);
        
        return Convert.ToBase64String(result);
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var hashBytes = Convert.FromBase64String(storedHash);
            if (hashBytes.Length != SaltSize + HashSize)
                return false;

            var salt = new byte[SaltSize];
            var storedHashBytes = new byte[HashSize];
            Buffer.BlockCopy(hashBytes, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(hashBytes, SaltSize, storedHashBytes, 0, HashSize);

            var computedHash = HashWithArgon2(password, salt);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHashBytes);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashWithArgon2(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemorySize,
            DegreeOfParallelism = DegreeOfParallelism
        };
        
        return argon2.GetBytes(HashSize);
    }
}
```

### Services/JwtTokenService.cs

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LucidAdmin.Infrastructure.Services;

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        _secretKey = _configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        _issuer = _configuration["Jwt:Issuer"] ?? "LucidAdmin";
        _audience = _configuration["Jwt:Audience"] ?? "LucidAdminUsers";
        _expirationMinutes = int.Parse(_configuration["Jwt:ExpirationMinutes"] ?? "60");
    }

    public string GenerateToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);

        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public Guid? GetUserIdFromToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var jwtToken = tokenHandler.ReadJwtToken(token);
            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }
        }
        catch
        {
            // Token parsing failed
        }
        return null;
    }
}
```

### DependencyInjection.cs

```csharp
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Infrastructure.Repositories;
using LucidAdmin.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LucidAdmin.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database
        var provider = configuration["Database:Provider"] ?? "SQLite";
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=lucidadmin.db";

        services.AddDbContext<LucidDbContext>(options =>
        {
            _ = provider.ToLower() switch
            {
                "sqlite" => options.UseSqlite(connectionString),
                "sqlserver" => options.UseSqlServer(connectionString),
                _ => throw new InvalidOperationException($"Unsupported database provider: {provider}")
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
```

---

## Project 3: LucidAdmin.Web

**Purpose**: Combined API and Blazor UI.

### LucidAdmin.Web.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>LucidAdmin.Web</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LucidAdmin.Core\LucidAdmin.Core.csproj" />
    <ProjectReference Include="..\LucidAdmin.Infrastructure\LucidAdmin.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=lucidadmin.db"
  },
  "Database": {
    "Provider": "SQLite"
  },
  "Jwt": {
    "SecretKey": "CHANGE-THIS-IN-PRODUCTION-USE-AT-LEAST-32-CHARACTERS-HERE!",
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
  },
  "AllowedHosts": "*"
}
```

### appsettings.Development.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

### Program.cs

```csharp
using System.Text;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Enums;
using LucidAdmin.Infrastructure;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Components;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add Infrastructure services (repositories, DbContext)
builder.Services.AddInfrastructure(builder.Configuration);

// Add Authentication
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] 
    ?? throw new InvalidOperationException("JWT SecretKey not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Add Razor Components (Blazor Server)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Lucid Admin API", Version = "v1" });
});

var app = builder.Build();

// Apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
    db.Database.Migrate();
    
    // Seed default admin user if no users exist
    var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var users = await userRepo.GetAllAsync();
    if (!users.Any())
    {
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@localhost",
            PasswordHash = passwordHasher.HashPassword("admin"),  // Change immediately!
            Role = UserRole.Admin,
            IsEnabled = true
        };
        await userRepo.AddAsync(adminUser);
        Console.WriteLine("Created default admin user (username: admin, password: admin) - CHANGE THIS IMMEDIATELY!");
    }
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// Map API endpoints
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapServiceAccountEndpoints();
app.MapToolServerEndpoints();
app.MapCapabilityEndpoints();
app.MapAuditEndpoints();

// Map Blazor
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

### Api/Endpoints/HealthEndpoints.cs

```csharp
using LucidAdmin.Core.Interfaces.Repositories;

namespace LucidAdmin.Web.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/health").WithTags("Health");

        group.MapGet("/", async (IToolServerRepository toolServerRepo) =>
        {
            var servers = await toolServerRepo.GetEnabledAsync();
            var serverList = servers.ToList();
            
            var healthy = serverList.Count(s => s.Status == Core.Enums.HealthStatus.Healthy);
            var degraded = serverList.Count(s => s.Status == Core.Enums.HealthStatus.Degraded);
            var unhealthy = serverList.Count(s => s.Status == Core.Enums.HealthStatus.Unhealthy);
            var unknown = serverList.Count(s => s.Status == Core.Enums.HealthStatus.Unknown);

            return Results.Ok(new
            {
                status = unhealthy > 0 ? "unhealthy" : (degraded > 0 ? "degraded" : "healthy"),
                timestamp = DateTime.UtcNow,
                toolServers = new
                {
                    total = serverList.Count,
                    healthy,
                    degraded,
                    unhealthy,
                    unknown
                }
            });
        });
    }
}
```

### Api/Endpoints/AuthEndpoints.cs

```csharp
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Api.Endpoints;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string Role, DateTime ExpiresAt);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/login", async (LoginRequest request, IUserRepository userRepo, IPasswordHasher passwordHasher, ITokenService tokenService) =>
        {
            var user = await userRepo.GetByUsernameAsync(request.Username);
            
            if (user == null)
                return Results.Unauthorized();

            if (!user.IsEnabled)
                return Results.Problem("Account is disabled", statusCode: 403);

            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
                return Results.Problem($"Account is locked until {user.LockoutEnd:u}", statusCode: 403);

            if (!passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                await userRepo.IncrementFailedLoginAsync(user.Id);
                return Results.Unauthorized();
            }

            await userRepo.ResetFailedLoginAsync(user.Id);
            await userRepo.UpdateLastLoginAsync(user.Id);

            var token = tokenService.GenerateToken(user);
            var expiresAt = DateTime.UtcNow.AddMinutes(60); // Match JWT config

            return Results.Ok(new LoginResponse(token, user.Username, user.Role.ToString(), expiresAt));
        });
    }
}
```

### Api/Endpoints/ServiceAccountEndpoints.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace LucidAdmin.Web.Api.Endpoints;

public record CreateServiceAccountRequest(
    string Name,
    string Domain,
    CredentialType CredentialType,
    string? DisplayName = null,
    string? Description = null,
    string? CredentialSource = null,
    string? CredentialKey = null);

public record UpdateServiceAccountRequest(
    string? DisplayName = null,
    string? Description = null,
    CredentialType? CredentialType = null,
    string? CredentialSource = null,
    string? CredentialKey = null,
    bool? IsEnabled = null);

public record ServiceAccountResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string Domain,
    CredentialType CredentialType,
    string? CredentialSource,
    string? Description,
    bool IsEnabled,
    HealthStatus HealthStatus,
    DateTime? LastHealthCheck,
    string? LastHealthMessage,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class ServiceAccountEndpoints
{
    public static void MapServiceAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/service-accounts")
            .WithTags("Service Accounts")
            .RequireAuthorization();

        // List all
        group.MapGet("/", async (IServiceAccountRepository repo) =>
        {
            var accounts = await repo.GetAllAsync();
            return Results.Ok(accounts.Select(ToResponse));
        });

        // Get by ID
        group.MapGet("/{id:guid}", async (Guid id, IServiceAccountRepository repo) =>
        {
            var account = await repo.GetByIdAsync(id);
            if (account == null)
                return Results.NotFound(new { error = "ServiceAccountNotFound", message = $"Service account {id} not found" });
            
            return Results.Ok(ToResponse(account));
        });

        // Create
        group.MapPost("/", async (CreateServiceAccountRequest request, IServiceAccountRepository repo) =>
        {
            // Check for duplicate
            var existing = await repo.GetByNameAndDomainAsync(request.Name, request.Domain);
            if (existing != null)
                return Results.Conflict(new { error = "DuplicateServiceAccount", message = $"Service account {request.Name}@{request.Domain} already exists" });

            var account = new ServiceAccount
            {
                Name = request.Name,
                Domain = request.Domain,
                CredentialType = request.CredentialType,
                DisplayName = request.DisplayName,
                Description = request.Description,
                CredentialSource = request.CredentialSource,
                CredentialKey = request.CredentialKey
            };

            await repo.AddAsync(account);
            return Results.Created($"/api/v1/service-accounts/{account.Id}", ToResponse(account));
        });

        // Update
        group.MapPut("/{id:guid}", async (Guid id, UpdateServiceAccountRequest request, IServiceAccountRepository repo) =>
        {
            var account = await repo.GetByIdAsync(id);
            if (account == null)
                return Results.NotFound(new { error = "ServiceAccountNotFound", message = $"Service account {id} not found" });

            if (request.DisplayName != null) account.DisplayName = request.DisplayName;
            if (request.Description != null) account.Description = request.Description;
            if (request.CredentialType.HasValue) account.CredentialType = request.CredentialType.Value;
            if (request.CredentialSource != null) account.CredentialSource = request.CredentialSource;
            if (request.CredentialKey != null) account.CredentialKey = request.CredentialKey;
            if (request.IsEnabled.HasValue) account.IsEnabled = request.IsEnabled.Value;

            await repo.UpdateAsync(account);
            return Results.Ok(ToResponse(account));
        });

        // Delete
        group.MapDelete("/{id:guid}", async (Guid id, IServiceAccountRepository repo) =>
        {
            var account = await repo.GetByIdAsync(id);
            if (account == null)
                return Results.NotFound(new { error = "ServiceAccountNotFound", message = $"Service account {id} not found" });

            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
    }

    private static ServiceAccountResponse ToResponse(ServiceAccount a) => new(
        a.Id,
        a.Name,
        a.DisplayName,
        a.Domain,
        a.CredentialType,
        a.CredentialSource,
        a.Description,
        a.IsEnabled,
        a.HealthStatus,
        a.LastHealthCheck,
        a.LastHealthMessage,
        a.CreatedAt,
        a.UpdatedAt
    );
}
```

### Api/Endpoints/ToolServerEndpoints.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;

namespace LucidAdmin.Web.Api.Endpoints;

public record RegisterToolServerRequest(
    string Name,
    string Endpoint,
    string Domain,
    string? DisplayName = null,
    string? Description = null,
    string? Version = null);

public record UpdateToolServerRequest(
    string? DisplayName = null,
    string? Description = null,
    string? Endpoint = null,
    bool? IsEnabled = null);

public record HeartbeatRequest(
    HealthStatus Status,
    string? Version = null,
    Dictionary<string, object>? CapabilityStatuses = null);

public record ToolServerResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string Endpoint,
    string Domain,
    string? Description,
    HealthStatus Status,
    DateTime? LastHeartbeat,
    string? Version,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class ToolServerEndpoints
{
    public static void MapToolServerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/tool-servers")
            .WithTags("Tool Servers")
            .RequireAuthorization();

        // List all
        group.MapGet("/", async (IToolServerRepository repo) =>
        {
            var servers = await repo.GetAllAsync();
            return Results.Ok(servers.Select(ToResponse));
        });

        // Get by ID
        group.MapGet("/{id:guid}", async (Guid id, IToolServerRepository repo) =>
        {
            var server = await repo.GetByIdAsync(id);
            if (server == null)
                return Results.NotFound(new { error = "ToolServerNotFound", message = $"Tool server {id} not found" });
            
            return Results.Ok(ToResponse(server));
        });

        // Register (create)
        group.MapPost("/register", async (RegisterToolServerRequest request, IToolServerRepository repo) =>
        {
            // Check for duplicate name
            var existingName = await repo.GetByNameAsync(request.Name);
            if (existingName != null)
                return Results.Conflict(new { error = "DuplicateToolServer", message = $"Tool server with name '{request.Name}' already exists" });

            // Check for duplicate endpoint
            var existingEndpoint = await repo.GetByEndpointAsync(request.Endpoint);
            if (existingEndpoint != null)
                return Results.Conflict(new { error = "DuplicateToolServer", message = $"Tool server with endpoint '{request.Endpoint}' already exists" });

            var server = new ToolServer
            {
                Name = request.Name,
                Endpoint = request.Endpoint,
                Domain = request.Domain,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Version = request.Version,
                Status = HealthStatus.Unknown,
                LastHeartbeat = DateTime.UtcNow
            };

            await repo.AddAsync(server);
            return Results.Created($"/api/v1/tool-servers/{server.Id}", ToResponse(server));
        });

        // Update
        group.MapPut("/{id:guid}", async (Guid id, UpdateToolServerRequest request, IToolServerRepository repo) =>
        {
            var server = await repo.GetByIdAsync(id);
            if (server == null)
                return Results.NotFound(new { error = "ToolServerNotFound", message = $"Tool server {id} not found" });

            if (request.DisplayName != null) server.DisplayName = request.DisplayName;
            if (request.Description != null) server.Description = request.Description;
            if (request.Endpoint != null) server.Endpoint = request.Endpoint;
            if (request.IsEnabled.HasValue) server.IsEnabled = request.IsEnabled.Value;

            await repo.UpdateAsync(server);
            return Results.Ok(ToResponse(server));
        });

        // Heartbeat
        group.MapPost("/{id:guid}/heartbeat", async (Guid id, HeartbeatRequest request, IToolServerRepository repo) =>
        {
            var server = await repo.GetByIdAsync(id);
            if (server == null)
                return Results.NotFound(new { error = "ToolServerNotFound", message = $"Tool server {id} not found" });

            server.Status = request.Status;
            server.LastHeartbeat = DateTime.UtcNow;
            if (request.Version != null) server.Version = request.Version;

            await repo.UpdateAsync(server);
            return Results.Ok(new { received = true, timestamp = DateTime.UtcNow });
        });

        // Get config for tool server
        group.MapGet("/{id:guid}/config", async (Guid id, IToolServerRepository toolRepo, ICapabilityMappingRepository mappingRepo) =>
        {
            var server = await toolRepo.GetByIdAsync(id);
            if (server == null)
                return Results.NotFound(new { error = "ToolServerNotFound", message = $"Tool server {id} not found" });

            var mappings = await mappingRepo.GetByToolServerIdAsync(id);
            
            return Results.Ok(new
            {
                toolServerId = server.Id,
                name = server.Name,
                domain = server.Domain,
                capabilities = mappings.Where(m => m.IsEnabled).Select(m => new
                {
                    capability = m.Capability.ToString(),
                    serviceAccountId = m.ServiceAccountId,
                    serviceAccountName = m.ServiceAccount?.Name,
                    serviceAccountDomain = m.ServiceAccount?.Domain,
                    credentialType = m.ServiceAccount?.CredentialType.ToString(),
                    allowedScopes = m.AllowedScopesJson,
                    deniedScopes = m.DeniedScopesJson
                })
            });
        });

        // Delete
        group.MapDelete("/{id:guid}", async (Guid id, IToolServerRepository repo) =>
        {
            var server = await repo.GetByIdAsync(id);
            if (server == null)
                return Results.NotFound(new { error = "ToolServerNotFound", message = $"Tool server {id} not found" });

            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
    }

    private static ToolServerResponse ToResponse(ToolServer s) => new(
        s.Id,
        s.Name,
        s.DisplayName,
        s.Endpoint,
        s.Domain,
        s.Description,
        s.Status,
        s.LastHeartbeat,
        s.Version,
        s.IsEnabled,
        s.CreatedAt,
        s.UpdatedAt
    );
}
```

### Api/Endpoints/CapabilityEndpoints.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;

namespace LucidAdmin.Web.Api.Endpoints;

public record CreateCapabilityMappingRequest(
    Guid ServiceAccountId,
    Guid ToolServerId,
    ToolCapability Capability,
    string? AllowedScopesJson = null,
    string? DeniedScopesJson = null);

public record UpdateCapabilityMappingRequest(
    string? AllowedScopesJson = null,
    string? DeniedScopesJson = null,
    bool? IsEnabled = null);

public record CapabilityMappingResponse(
    Guid Id,
    Guid ServiceAccountId,
    string? ServiceAccountName,
    Guid ToolServerId,
    string? ToolServerName,
    ToolCapability Capability,
    string? AllowedScopesJson,
    string? DeniedScopesJson,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class CapabilityEndpoints
{
    public static void MapCapabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/capabilities")
            .WithTags("Capability Mappings")
            .RequireAuthorization();

        // List all
        group.MapGet("/", async (ICapabilityMappingRepository repo) =>
        {
            var mappings = await repo.GetAllAsync();
            return Results.Ok(mappings.Select(ToResponse));
        });

        // Get by tool server
        group.MapGet("/tool-server/{toolServerId:guid}", async (Guid toolServerId, ICapabilityMappingRepository repo) =>
        {
            var mappings = await repo.GetByToolServerIdAsync(toolServerId);
            return Results.Ok(mappings.Select(ToResponse));
        });

        // Get by ID
        group.MapGet("/{id:guid}", async (Guid id, ICapabilityMappingRepository repo) =>
        {
            var mapping = await repo.GetByIdAsync(id);
            if (mapping == null)
                return Results.NotFound(new { error = "CapabilityMappingNotFound", message = $"Capability mapping {id} not found" });
            
            return Results.Ok(ToResponse(mapping));
        });

        // Create
        group.MapPost("/", async (CreateCapabilityMappingRequest request, ICapabilityMappingRepository mappingRepo, IServiceAccountRepository accountRepo, IToolServerRepository serverRepo) =>
        {
            // Validate service account exists
            var account = await accountRepo.GetByIdAsync(request.ServiceAccountId);
            if (account == null)
                return Results.BadRequest(new { error = "ServiceAccountNotFound", message = $"Service account {request.ServiceAccountId} not found" });

            // Validate tool server exists
            var server = await serverRepo.GetByIdAsync(request.ToolServerId);
            if (server == null)
                return Results.BadRequest(new { error = "ToolServerNotFound", message = $"Tool server {request.ToolServerId} not found" });

            // Check for duplicate mapping
            var existing = await mappingRepo.GetByToolServerAndCapabilityAsync(request.ToolServerId, request.Capability);
            if (existing != null)
                return Results.Conflict(new { error = "DuplicateCapabilityMapping", message = $"Tool server already has a mapping for {request.Capability}" });

            var mapping = new CapabilityMapping
            {
                ServiceAccountId = request.ServiceAccountId,
                ToolServerId = request.ToolServerId,
                Capability = request.Capability,
                AllowedScopesJson = request.AllowedScopesJson,
                DeniedScopesJson = request.DeniedScopesJson
            };

            await mappingRepo.AddAsync(mapping);
            
            // Reload to get navigation properties
            mapping = await mappingRepo.GetByIdAsync(mapping.Id);
            return Results.Created($"/api/v1/capabilities/{mapping!.Id}", ToResponse(mapping));
        });

        // Update
        group.MapPut("/{id:guid}", async (Guid id, UpdateCapabilityMappingRequest request, ICapabilityMappingRepository repo) =>
        {
            var mapping = await repo.GetByIdAsync(id);
            if (mapping == null)
                return Results.NotFound(new { error = "CapabilityMappingNotFound", message = $"Capability mapping {id} not found" });

            if (request.AllowedScopesJson != null) mapping.AllowedScopesJson = request.AllowedScopesJson;
            if (request.DeniedScopesJson != null) mapping.DeniedScopesJson = request.DeniedScopesJson;
            if (request.IsEnabled.HasValue) mapping.IsEnabled = request.IsEnabled.Value;

            await repo.UpdateAsync(mapping);
            return Results.Ok(ToResponse(mapping));
        });

        // Delete
        group.MapDelete("/{id:guid}", async (Guid id, ICapabilityMappingRepository repo) =>
        {
            var mapping = await repo.GetByIdAsync(id);
            if (mapping == null)
                return Results.NotFound(new { error = "CapabilityMappingNotFound", message = $"Capability mapping {id} not found" });

            await repo.DeleteAsync(id);
            return Results.NoContent();
        });
    }

    private static CapabilityMappingResponse ToResponse(CapabilityMapping m) => new(
        m.Id,
        m.ServiceAccountId,
        m.ServiceAccount?.Name,
        m.ToolServerId,
        m.ToolServer?.Name,
        m.Capability,
        m.AllowedScopesJson,
        m.DeniedScopesJson,
        m.IsEnabled,
        m.CreatedAt,
        m.UpdatedAt
    );
}
```

### Api/Endpoints/AuditEndpoints.cs

```csharp
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;

namespace LucidAdmin.Web.Api.Endpoints;

public record SubmitAuditEventRequest(
    AuditAction Action,
    ToolCapability? Capability = null,
    string? PerformedBy = null,
    string? TargetResource = null,
    string? TicketNumber = null,
    bool Success = true,
    string? ErrorMessage = null,
    string? DetailsJson = null);

public record AuditEventResponse(
    Guid Id,
    Guid? ToolServerId,
    string? ToolServerName,
    AuditAction Action,
    ToolCapability? Capability,
    string? PerformedBy,
    string? TargetResource,
    string? TicketNumber,
    bool Success,
    string? ErrorMessage,
    string? DetailsJson,
    DateTime CreatedAt);

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/audit")
            .WithTags("Audit Logs")
            .RequireAuthorization();

        // Search/list
        group.MapGet("/", async (
            string? targetResource,
            AuditAction? action,
            Guid? toolServerId,
            DateTime? from,
            DateTime? to,
            int limit,
            IAuditEventRepository repo) =>
        {
            limit = Math.Clamp(limit == 0 ? 100 : limit, 1, 1000);
            var events = await repo.SearchAsync(targetResource, action, toolServerId, from, to, limit);
            return Results.Ok(events.Select(ToResponse));
        });

        // Get by ID
        group.MapGet("/{id:guid}", async (Guid id, IAuditEventRepository repo) =>
        {
            var evt = await repo.GetByIdAsync(id);
            if (evt == null)
                return Results.NotFound(new { error = "AuditEventNotFound", message = $"Audit event {id} not found" });
            
            return Results.Ok(ToResponse(evt));
        });

        // Submit audit event (called by Tool Servers)
        group.MapPost("/", async (SubmitAuditEventRequest request, Guid? toolServerId, IAuditEventRepository repo) =>
        {
            var evt = new AuditEvent
            {
                ToolServerId = toolServerId,
                Action = request.Action,
                Capability = request.Capability,
                PerformedBy = request.PerformedBy,
                TargetResource = request.TargetResource,
                TicketNumber = request.TicketNumber,
                Success = request.Success,
                ErrorMessage = request.ErrorMessage,
                DetailsJson = request.DetailsJson
            };

            await repo.AddAsync(evt);
            return Results.Created($"/api/v1/audit/{evt.Id}", ToResponse(evt));
        });
    }

    private static AuditEventResponse ToResponse(AuditEvent e) => new(
        e.Id,
        e.ToolServerId,
        e.ToolServer?.Name,
        e.Action,
        e.Capability,
        e.PerformedBy,
        e.TargetResource,
        e.TicketNumber,
        e.Success,
        e.ErrorMessage,
        e.DetailsJson,
        e.CreatedAt
    );
}
```

### Components/App.razor

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="stylesheet" href="css/bootstrap/bootstrap.min.css" />
    <link rel="stylesheet" href="css/app.css" />
    <link rel="icon" type="image/png" href="favicon.png" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

### Components/Routes.razor

```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

### Components/Layout/MainLayout.razor

```razor
@inherits LayoutComponentBase

<div class="page">
    <div class="sidebar">
        <NavMenu />
    </div>

    <main>
        <div class="top-row px-4">
            <a href="https://github.com/your-org/lucid-it-agent" target="_blank">About</a>
        </div>

        <article class="content px-4">
            @Body
        </article>
    </main>
</div>
```

### Components/Layout/NavMenu.razor

```razor
<div class="top-row ps-3 navbar navbar-dark">
    <div class="container-fluid">
        <a class="navbar-brand" href="">Lucid Admin</a>
    </div>
</div>

<input type="checkbox" title="Navigation menu" class="navbar-toggler" />

<div class="nav-scrollable">
    <nav class="flex-column">
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="" Match="NavLinkMatch.All">
                <span class="bi bi-house-door-fill" aria-hidden="true"></span> Dashboard
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="service-accounts">
                <span class="bi bi-person-badge-fill" aria-hidden="true"></span> Service Accounts
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="tool-servers">
                <span class="bi bi-server" aria-hidden="true"></span> Tool Servers
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="capabilities">
                <span class="bi bi-diagram-3-fill" aria-hidden="true"></span> Capabilities
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="audit">
                <span class="bi bi-journal-text" aria-hidden="true"></span> Audit Log
            </NavLink>
        </div>
    </nav>
</div>
```

### Components/Pages/Home.razor

```razor
@page "/"

<PageTitle>Dashboard - Lucid Admin</PageTitle>

<h1>Dashboard</h1>

<p>Welcome to the Lucid Admin Portal.</p>

<div class="alert alert-info">
    <strong>Getting Started:</strong> Use the navigation menu to manage service accounts, tool servers, and capability mappings.
</div>

<div class="row">
    <div class="col-md-4">
        <div class="card">
            <div class="card-body">
                <h5 class="card-title">Service Accounts</h5>
                <p class="card-text">Configure accounts used for AD and file operations.</p>
                <a href="service-accounts" class="btn btn-primary">Manage</a>
            </div>
        </div>
    </div>
    <div class="col-md-4">
        <div class="card">
            <div class="card-body">
                <h5 class="card-title">Tool Servers</h5>
                <p class="card-text">Monitor and configure tool server instances.</p>
                <a href="tool-servers" class="btn btn-primary">Manage</a>
            </div>
        </div>
    </div>
    <div class="col-md-4">
        <div class="card">
            <div class="card-body">
                <h5 class="card-title">Audit Log</h5>
                <p class="card-text">View history of all agent actions.</p>
                <a href="audit" class="btn btn-primary">View</a>
            </div>
        </div>
    </div>
</div>
```

### wwwroot/css/app.css

```css
html, body {
    font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif;
}

h1:focus {
    outline: none;
}

a, .btn-link {
    color: #0071c1;
}

.btn-primary {
    color: #fff;
    background-color: #1b6ec2;
    border-color: #1861ac;
}

.content {
    padding-top: 1.1rem;
}

.valid.modified:not([type=checkbox]) {
    outline: 1px solid #26b050;
}

.invalid {
    outline: 1px solid red;
}

.validation-message {
    color: red;
}

.page {
    position: relative;
    display: flex;
    flex-direction: column;
}

main {
    flex: 1;
}

.sidebar {
    background-image: linear-gradient(180deg, rgb(5, 39, 103) 0%, #3a0647 70%);
}

.top-row {
    background-color: #f7f7f7;
    border-bottom: 1px solid #d6d5d5;
    justify-content: flex-end;
    height: 3.5rem;
    display: flex;
    align-items: center;
}

.top-row a {
    margin-left: 1.5rem;
}

@media (min-width: 641px) {
    .page {
        flex-direction: row;
    }

    .sidebar {
        width: 250px;
        height: 100vh;
        position: sticky;
        top: 0;
    }

    .top-row {
        position: sticky;
        top: 0;
        z-index: 1;
    }

    .top-row, article {
        padding-left: 2rem !important;
        padding-right: 1.5rem !important;
    }
}
```

---

## Test Projects

Create minimal test project scaffolds:

### LucidAdmin.Core.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LucidAdmin.Core\LucidAdmin.Core.csproj" />
  </ItemGroup>
</Project>
```

### LucidAdmin.Infrastructure.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LucidAdmin.Infrastructure\LucidAdmin.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

### LucidAdmin.Web.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LucidAdmin.Web\LucidAdmin.Web.csproj" />
  </ItemGroup>
</Project>
```

---

## Solution File

Create `LucidAdmin.sln` with all projects properly referenced.

---

## README.md

Create a README with:
1. Project overview
2. Prerequisites (.NET 8 SDK)
3. How to build and run
4. Default admin credentials warning
5. API documentation link (Swagger at /swagger)

---

## Verification Steps

After scaffolding:

```bash
cd /home/alton/Documents/lucid-it-agent/admin/dotnet

# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Run the application
cd src/LucidAdmin.Web
dotnet run

# Verify:
# 1. Browse to https://localhost:5001 - should see Blazor UI
# 2. Browse to https://localhost:5001/swagger - should see API docs
# 3. POST to /api/v1/auth/login with {"username":"admin","password":"admin"}
# 4. Use returned token to call other endpoints
```

---

## Notes

- The default admin password is "admin" - this is printed to console on first run as a reminder to change it
- SQLite database file will be created as `lucidadmin.db` in the working directory
- All API endpoints except /api/v1/auth/login require JWT authentication
- Blazor pages are placeholders - full UI implementation is a separate task
