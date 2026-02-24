# Lucid Tool Server (.NET 8)

.NET 8 implementation of the Praxova IT Agent Tool Server for native Windows/Active Directory integration.

## Project Structure

```
tool-server/dotnet/
├── LucidToolServer.sln             # Solution file
├── src/
│   └── LucidToolServer/            # Main web API project
│       ├── Configuration/          # Configuration classes
│       ├── Models/                 # Request/Response models
│       │   ├── Requests/
│       │   └── Responses/
│       ├── Services/               # Business logic services
│       ├── Exceptions/             # Custom exception types
│       ├── Program.cs              # Application entry point
│       ├── appsettings.json        # Configuration
│       └── Dockerfile              # Windows container
└── tests/
    └── LucidToolServer.Tests/      # Unit tests
        └── Services/
```

## Technology Stack

- **.NET 8 (LTS)** - Latest long-term support version
- **ASP.NET Core Minimal APIs** - Lightweight, performant API framework
- **System.DirectoryServices.AccountManagement** - Native AD integration
- **System.Security.AccessControl** - NTFS permission management
- **Serilog** - Structured logging
- **xUnit + Moq** - Testing framework

## API Endpoints

All endpoints are under `/api/v1/`:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/health` | Health check |
| POST | `/password/reset` | Reset user password |
| POST | `/groups/add-member` | Add user to group |
| POST | `/groups/remove-member` | Remove user from group |
| GET | `/groups/{groupName}` | Get group info |
| GET | `/user/{username}/groups` | Get user's groups |
| POST | `/permissions/grant` | Grant file permissions |
| POST | `/permissions/revoke` | Revoke file permissions |
| GET | `/permissions/{*path}` | List file permissions |

## Building

### Prerequisites

- .NET 8 SDK
- Windows Server 2022 (for AD/file operations)
- Active Directory domain membership (for production)

### Build Commands

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run application
dotnet run --project src/LucidToolServer/LucidToolServer.csproj

# Publish for deployment
dotnet publish -c Release -o ./publish
```

## Configuration

Configuration is managed through `appsettings.json` and environment variables:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  },
  "ToolServer": {
    "DomainName": "montanifarms.com",
    "ProtectedGroups": ["Domain Admins", "Enterprise Admins"],
    "ProtectedAccounts": ["Administrator", "krbtgt"],
    "AllowedPaths": ["\\\\*\\*share*"],
    "ApiKey": ""
  }
}
```

### Server Binding

By default, the server listens on `http://0.0.0.0:5000`, making it accessible from remote machines. To bind to a different address or port, update the `Kestrel:Endpoints:Http:Url` setting in `appsettings.json` or pass `--urls` on the command line:

```bash
dotnet run --project src/LucidToolServer/LucidToolServer.csproj --urls "http://localhost:8080"
```

## Docker

Build Windows container:

```powershell
docker build -t lucid-tool-server:latest -f src/LucidToolServer/Dockerfile .
```

Run container (must be domain-joined Windows host):

```powershell
docker run -d -p 8080:8080 `
  -e ToolServer__ApiKey="your-api-key" `
  lucid-tool-server:latest
```

## Security

- Protected accounts/groups cannot be modified
- Path validation for file operations
- API key authentication (optional)
- Structured logging for audit trail

## API Compatibility

This .NET implementation maintains exact API compatibility with the Python version:

- Same endpoint paths and HTTP methods
- Same JSON request/response formats (camelCase)
- Same error codes and status codes
- Same business logic (protected accounts, path validation)

## Development Notes

### Testing on Linux/macOS

The solution can be built and tested on non-Windows platforms, but:

- AD operations will fail (requires Windows + domain membership)
- File permission operations will fail (requires NTFS)
- Tests verify business logic only (mocked AD/file operations)

### Integration Testing

Full integration tests require:

- Windows Server 2022 or later
- Active Directory domain controller
- Domain-joined server
- Test user accounts and groups

## Migration from Python

Key differences from the Python implementation:

| Python | .NET |
|--------|------|
| ldap3 | System.DirectoryServices.AccountManagement |
| WinRM | Direct NTFS API calls |
| FastAPI | ASP.NET Core Minimal APIs |
| Pydantic | Record types with JsonPropertyName |
| pytest | xUnit |

## License

Part of the Praxova IT Agent project.
