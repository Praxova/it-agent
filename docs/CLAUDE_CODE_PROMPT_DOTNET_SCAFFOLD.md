# Claude Code Prompt: Scaffold .NET Tool Server

## Context

You are working on the Lucid IT Agent project. We are migrating the tool server from Python to .NET 8 to enable native Windows/Active Directory integration.

**Project location**: `/home/alton/Documents/lucid-it-agent`
**New tool server location**: `/home/alton/Documents/lucid-it-agent/tool-server/dotnet`

The existing Python API routes are in `/home/alton/Documents/lucid-it-agent/tool-server/python/src/tool_server/api/routes.py` and models are in `/home/alton/Documents/lucid-it-agent/tool-server/python/src/tool_server/api/models.py`. We need to maintain exact API compatibility.

## Task

Create the .NET 8 solution structure for the Lucid Tool Server with:

1. **Solution and Project Structure**
2. **API Models** (matching Python exactly)
3. **Service Interfaces** (for dependency injection and testing)
4. **Minimal API Endpoints** (matching Python routes)
5. **Dockerfile** for Windows containers
6. **Unit Test Project** with placeholder tests

## Requirements

### Technology Stack
- .NET 8 (LTS)
- ASP.NET Core Minimal APIs (NOT controllers)
- System.DirectoryServices.AccountManagement (for AD operations)
- System.Security.AccessControl (for NTFS operations)
- Serilog for structured logging
- xUnit for testing
- Moq for mocking

### Project Structure

```
tool-server/dotnet/
├── LucidToolServer.sln
├── src/
│   └── LucidToolServer/
│       ├── LucidToolServer.csproj
│       ├── Program.cs
│       ├── Configuration/
│       │   └── ToolServerSettings.cs
│       ├── Models/
│       │   ├── Requests/
│       │   │   ├── PasswordResetRequest.cs
│       │   │   ├── GroupMembershipRequest.cs
│       │   │   ├── FilePermissionRequest.cs
│       │   │   └── FilePermissionRevokeRequest.cs
│       │   └── Responses/
│       │       ├── PasswordResetResponse.cs
│       │       ├── GroupMembershipResponse.cs
│       │       ├── GroupInfoResponse.cs
│       │       ├── UserGroupsResponse.cs
│       │       ├── FilePermissionResponse.cs
│       │       ├── FilePermissionListResponse.cs
│       │       ├── HealthResponse.cs
│       │       └── ErrorResponse.cs
│       ├── Services/
│       │   ├── IActiveDirectoryService.cs
│       │   ├── ActiveDirectoryService.cs
│       │   ├── IFilePermissionService.cs
│       │   └── FilePermissionService.cs
│       ├── Exceptions/
│       │   ├── UserNotFoundException.cs
│       │   ├── GroupNotFoundException.cs
│       │   ├── PermissionDeniedException.cs
│       │   ├── PathNotAllowedException.cs
│       │   └── AdOperationException.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Dockerfile
└── tests/
    └── LucidToolServer.Tests/
        ├── LucidToolServer.Tests.csproj
        └── Services/
            ├── ActiveDirectoryServiceTests.cs
            └── FilePermissionServiceTests.cs
```

### API Routes (Must Match Python Exactly)

All routes are under `/api/v1/`:

| Method | Route | Request Model | Response Model |
|--------|-------|---------------|----------------|
| GET | /health | - | HealthResponse |
| POST | /password/reset | PasswordResetRequest | PasswordResetResponse |
| POST | /groups/add-member | GroupMembershipRequest | GroupMembershipResponse |
| POST | /groups/remove-member | GroupMembershipRequest | GroupMembershipResponse |
| GET | /groups/{groupName} | - | GroupInfoResponse |
| GET | /user/{username}/groups | - | UserGroupsResponse |
| POST | /permissions/grant | FilePermissionRequest | FilePermissionResponse |
| POST | /permissions/revoke | FilePermissionRevokeRequest | FilePermissionResponse |
| GET | /permissions/{*path} | - | FilePermissionListResponse |

### Model Definitions (Match Python Pydantic Models)

**PasswordResetRequest**
```csharp
public record PasswordResetRequest(
    string Username,
    string NewPassword
);
```

**PasswordResetResponse**
```csharp
public record PasswordResetResponse(
    bool Success,
    string Message,
    string Username,
    string? UserDn
);
```

**GroupMembershipRequest**
```csharp
public record GroupMembershipRequest(
    string Username,
    string GroupName,
    string TicketNumber
);
```

**GroupMembershipResponse**
```csharp
public record GroupMembershipResponse(
    bool Success,
    string Message,
    string Username,
    string GroupName,
    string TicketNumber
);
```

**GroupInfoResponse**
```csharp
public record GroupInfoResponse(
    bool Success,
    string GroupName,
    string GroupDn,
    string? Description,
    List<string> Members
);
```

**UserGroupsResponse**
```csharp
public record UserGroupsResponse(
    bool Success,
    string Username,
    List<string> Groups
);
```

**FilePermissionRequest**
```csharp
public record FilePermissionRequest(
    string Username,
    string Path,
    string Permission,  // "Read" or "Write"
    string TicketNumber
);
```

**FilePermissionRevokeRequest**
```csharp
public record FilePermissionRevokeRequest(
    string Username,
    string Path,
    string TicketNumber
);
```

**FilePermissionResponse**
```csharp
public record FilePermissionResponse(
    bool Success,
    string Username,
    string Path,
    string Action,  // "granted" or "revoked"
    string? Permission,
    string Message
);
```

**FilePermissionListResponse**
```csharp
public record FilePermissionListResponse(
    string Path,
    List<PermissionEntry> Permissions
);

public record PermissionEntry(
    string Identity,
    string AccessType,
    string Rights,
    bool IsInherited
);
```

**HealthResponse**
```csharp
public record HealthResponse(
    string Status,  // "healthy", "degraded", "unhealthy"
    bool AdConnected,
    string? Message
);
```

**ErrorResponse** (for exception handling)
```csharp
public record ErrorResponse(
    string Error,
    string Message,
    string? Detail
);
```

### Service Interfaces

**IActiveDirectoryService**
```csharp
public interface IActiveDirectoryService
{
    Task<PasswordResetResponse> ResetPasswordAsync(string username, string newPassword);
    Task<GroupMembershipResponse> AddUserToGroupAsync(string username, string groupName);
    Task<GroupMembershipResponse> RemoveUserFromGroupAsync(string username, string groupName);
    Task<GroupInfoResponse> GetGroupAsync(string groupName);
    Task<List<string>> GetUserGroupsAsync(string username);
    Task<bool> TestConnectionAsync();
}
```

**IFilePermissionService**
```csharp
public interface IFilePermissionService
{
    void GrantPermission(string path, string username, PermissionLevel permission);
    void RevokePermission(string path, string username);
    List<PermissionEntry> ListPermissions(string path);
    bool HealthCheck();
}

public enum PermissionLevel
{
    Read,
    Write
}
```

### Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ToolServer": {
    "ProtectedGroups": [
      "Domain Admins",
      "Enterprise Admins",
      "Schema Admins",
      "Administrators"
    ],
    "ProtectedAccounts": [
      "Administrator",
      "krbtgt"
    ],
    "AllowedPaths": [
      "\\\\*\\*share*"
    ],
    "ApiKey": ""
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
    ]
  }
}
```

### Program.cs Structure

Use Minimal API style:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((context, config) => 
    config.ReadFrom.Configuration(context.Configuration));

// Add services
builder.Services.Configure<ToolServerSettings>(
    builder.Configuration.GetSection("ToolServer"));
builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
builder.Services.AddScoped<IFilePermissionService, FilePermissionService>();

var app = builder.Build();

// Map routes under /api/v1
var api = app.MapGroup("/api/v1");

// Health
api.MapGet("/health", async (IActiveDirectoryService adService) => { ... });

// Password
api.MapPost("/password/reset", async (PasswordResetRequest request, IActiveDirectoryService adService) => { ... });

// Groups
api.MapPost("/groups/add-member", async (GroupMembershipRequest request, IActiveDirectoryService adService) => { ... });
// ... etc

app.Run();
```

### Dockerfile

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS build
WORKDIR /src
COPY ["src/LucidToolServer/LucidToolServer.csproj", "LucidToolServer/"]
RUN dotnet restore "LucidToolServer/LucidToolServer.csproj"
COPY src/ .
WORKDIR /src/LucidToolServer
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LucidToolServer.dll"]
```

### Exception Handling

Create a global exception handler that maps our custom exceptions to appropriate HTTP responses:

| Exception | HTTP Status | Error Code |
|-----------|-------------|------------|
| UserNotFoundException | 404 | UserNotFound |
| GroupNotFoundException | 404 | GroupNotFound |
| PermissionDeniedException | 403 | PermissionDenied |
| PathNotAllowedException | 403 | PathNotAllowed |
| PathNotFoundException | 404 | PathNotFound |
| AdOperationException | 500 | OperationFailed |
| Exception | 500 | InternalError |

### Service Implementations

For the **ActiveDirectoryService**, implement methods using `System.DirectoryServices.AccountManagement`:

```csharp
using System.DirectoryServices.AccountManagement;

public class ActiveDirectoryService : IActiveDirectoryService
{
    private readonly ToolServerSettings _settings;
    private readonly ILogger<ActiveDirectoryService> _logger;

    public async Task<PasswordResetResponse> ResetPasswordAsync(string username, string newPassword)
    {
        // Check protected accounts
        if (_settings.ProtectedAccounts.Contains(username, StringComparer.OrdinalIgnoreCase))
            throw new PermissionDeniedException($"Account '{username}' is protected");

        using var context = new PrincipalContext(ContextType.Domain);
        using var user = UserPrincipal.FindByIdentity(context, username);
        
        if (user == null)
            throw new UserNotFoundException($"User '{username}' not found");

        user.SetPassword(newPassword);
        user.ExpirePasswordNow();
        user.Save();

        return new PasswordResetResponse(
            Success: true,
            Message: $"Password reset for {username}",
            Username: username,
            UserDn: user.DistinguishedName
        );
    }
    
    // ... other methods
}
```

For **FilePermissionService**, use `System.Security.AccessControl`:

```csharp
using System.Security.AccessControl;
using System.Security.Principal;

public class FilePermissionService : IFilePermissionService
{
    public void GrantPermission(string path, string username, PermissionLevel permission)
    {
        var directoryInfo = new DirectoryInfo(path);
        var security = directoryInfo.GetAccessControl();
        
        var rights = permission == PermissionLevel.Write 
            ? FileSystemRights.Modify 
            : FileSystemRights.Read;
        
        var rule = new FileSystemAccessRule(
            username,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow
        );
        
        security.AddAccessRule(rule);
        directoryInfo.SetAccessControl(security);
    }
    
    // ... other methods
}
```

### Unit Tests

Create tests that mock the AD service (since we can't call real AD from Linux):

```csharp
public class ActiveDirectoryServiceTests
{
    [Fact]
    public async Task ResetPassword_ProtectedAccount_ThrowsPermissionDenied()
    {
        // Arrange
        var settings = Options.Create(new ToolServerSettings 
        { 
            ProtectedAccounts = new[] { "Administrator" } 
        });
        var logger = Mock.Of<ILogger<ActiveDirectoryService>>();
        var service = new ActiveDirectoryService(settings, logger);

        // Act & Assert
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => service.ResetPasswordAsync("Administrator", "NewPass123!")
        );
    }
}
```

## Deliverables

1. Create all files in the directory structure above
2. Ensure `dotnet build` succeeds (on Linux)
3. Ensure `dotnet test` runs (tests will be placeholders, but should compile)
4. JSON serialization should use camelCase to match Python API

## Notes

- The AD service methods should work but may fail at runtime on Linux (that's expected)
- Focus on getting the structure correct so we can build and iterate
- Use `record` types for request/response models (immutable, clean)
- Include XML documentation comments on public interfaces
- Add `[JsonPropertyName]` attributes if needed to ensure exact JSON field names match Python
