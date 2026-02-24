# Lucid Admin Portal

A .NET 8 web application for managing the Praxova IT Agent system, including service accounts, tool servers, capability mappings, and audit logging.

## Architecture

The solution follows Clean Architecture principles with three main projects:

- **LucidAdmin.Core**: Domain entities, enums, interfaces, and exceptions
- **LucidAdmin.Infrastructure**: Data access with EF Core, repositories, and services (Argon2, JWT)
- **LucidAdmin.Web**: ASP.NET Core Minimal APIs and Blazor Server UI

## Prerequisites

- .NET 8.0 SDK or later
- SQLite (default) or SQL Server

## Getting Started

### 1. Build the Solution

```bash
cd admin/dotnet
dotnet build
```

### 2. Run Database Migrations

```bash
cd src/LucidAdmin.Web
dotnet ef database update
```

### 3. Run the Application

```bash
dotnet run --project src/LucidAdmin.Web
```

The application will be available at:
- Web UI: http://localhost:5000
- API: http://localhost:5000/api
- Swagger: http://localhost:5000/swagger

## Configuration

Edit `src/LucidAdmin.Web/appsettings.json` or `appsettings.Development.json`:

### Database

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=lucid-admin.db"
  },
  "Database": {
    "Provider": "Sqlite"
  }
}
```

For SQL Server:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=LucidAdmin;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Database": {
    "Provider": "SqlServer"
  }
}
```

### JWT Authentication

```json
{
  "Jwt": {
    "SecretKey": "your-secret-key-min-32-chars-change-in-production!",
    "Issuer": "LucidAdmin",
    "Audience": "LucidAdmin",
    "ExpirationMinutes": "60"
  }
}
```

**IMPORTANT**: Change the `SecretKey` in production to a secure random value.

## API Endpoints

### Health
- `GET /api/health` - Health check

### Authentication
- `POST /api/auth/login` - User login
- `POST /api/auth/users` - Create user (requires auth)
- `GET /api/auth/users` - List users (requires auth)

### Service Accounts
- `GET /api/service-accounts` - List all service accounts
- `GET /api/service-accounts/{id}` - Get service account by ID
- `GET /api/service-accounts/username/{username}` - Get by username
- `GET /api/service-accounts/unhealthy` - List unhealthy accounts
- `POST /api/service-accounts` - Create service account
- `PUT /api/service-accounts/{id}` - Update service account
- `DELETE /api/service-accounts/{id}` - Delete service account

### Tool Servers
- `GET /api/tool-servers` - List all tool servers
- `GET /api/tool-servers/{id}` - Get tool server by ID
- `GET /api/tool-servers/name/{name}` - Get by name
- `GET /api/tool-servers/unhealthy` - List unhealthy servers
- `POST /api/tool-servers` - Register tool server
- `PUT /api/tool-servers/{id}` - Update tool server
- `DELETE /api/tool-servers/{id}` - Deregister tool server
- `POST /api/tool-servers/{id}/heartbeat` - Send heartbeat

### Capability Mappings
- `GET /api/capability-mappings` - List all mappings
- `GET /api/capability-mappings/{id}` - Get mapping by ID
- `GET /api/capability-mappings/service-account/{id}` - Get by service account
- `GET /api/capability-mappings/tool-server/{id}` - Get by tool server
- `POST /api/capability-mappings` - Create mapping
- `PUT /api/capability-mappings/{id}` - Update mapping
- `DELETE /api/capability-mappings/{id}` - Delete mapping

### Audit Events
- `GET /api/audit-events` - List all events
- `GET /api/audit-events/recent?count=100` - Get recent events
- `GET /api/audit-events/user/{userId}` - Get by user
- `GET /api/audit-events/action/{action}` - Get by action
- `GET /api/audit-events/target/{type}/{id}` - Get by target

## Testing

Run all tests:

```bash
dotnet test
```

Run specific test project:

```bash
dotnet test tests/LucidAdmin.Core.Tests
dotnet test tests/LucidAdmin.Infrastructure.Tests
dotnet test tests/LucidAdmin.Web.Tests
```

## Development

### Creating Migrations

```bash
cd src/LucidAdmin.Web
dotnet ef migrations add MigrationName
```

### Project Structure

```
admin/dotnet/
├── src/
│   ├── LucidAdmin.Core/           # Domain layer
│   │   ├── Entities/              # Domain entities
│   │   ├── Enums/                 # Domain enums
│   │   ├── Interfaces/            # Repository and service interfaces
│   │   └── Exceptions/            # Custom exceptions
│   ├── LucidAdmin.Infrastructure/ # Infrastructure layer
│   │   ├── Data/                  # DbContext and configurations
│   │   ├── Repositories/          # Repository implementations
│   │   └── Services/              # Service implementations
│   └── LucidAdmin.Web/            # Presentation layer
│       ├── Components/            # Blazor components
│       ├── Endpoints/             # API endpoints
│       ├── Models/                # Request/response models
│       └── wwwroot/               # Static files
└── tests/                         # Unit tests
    ├── LucidAdmin.Core.Tests/
    ├── LucidAdmin.Infrastructure.Tests/
    └── LucidAdmin.Web.Tests/
```

## Security

- Passwords are hashed using Argon2id
- JWT tokens for API authentication
- All API endpoints (except health and login) require authentication
- Audit logging for all operations
- HTTPS recommended for production

## License

Proprietary - Praxova IT Agent System
