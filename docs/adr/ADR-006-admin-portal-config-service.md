# ADR-006: Lucid Admin Portal and Config Service

## Status

**Accepted** - January 2025

## Context

The Lucid IT Agent requires enterprise-grade management capabilities:

1. **Distributed Tool Servers** - Organizations may deploy multiple tool servers across regions, domains, or security zones
2. **Service Account Management** - Multiple accounts with different permissions, scopes, and credential types
3. **Centralized Configuration** - Rules, prompts, and settings managed from one location
4. **Operational Visibility** - Health monitoring, audit logs, and alerting
5. **Compliance Requirements** - Enterprise IT needs audit trails and access controls

Running as Domain Admin (current development approach) is not acceptable for production. IT security teams require:
- Principle of least privilege
- Separation of duties
- Auditable service accounts
- Credential management without stored passwords (gMSA)

## Decision

**Create a Lucid Admin Portal with integrated Config Service for centralized management of the Lucid IT Agent ecosystem.**

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                         LUCID ADMIN PORTAL                                       │
│                    (Web UI + Config Service API)                                 │
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                         Web UI (Blazor Server)                           │   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐      │   │
│  │  │Dashboard │ │Service   │ │Tool      │ │Rules &   │ │Audit     │      │   │
│  │  │          │ │Accounts  │ │Servers   │ │Prompts   │ │Logs      │      │   │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘      │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                      │                                          │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                      Config Service API (ASP.NET Core)                   │   │
│  │                                                                          │   │
│  │  Endpoints:                                                              │   │
│  │  - /api/auth/*              (authentication)                            │   │
│  │  - /api/service-accounts/*  (CRUD + mappings)                           │   │
│  │  - /api/tool-servers/*      (registration, health, config)              │   │
│  │  - /api/capabilities/*      (capability mappings)                       │   │
│  │  - /api/rules/*             (rulesets, prompts)                         │   │
│  │  - /api/audit/*             (audit log queries)                         │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                      │                                          │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                      Data Access Layer (Repository Pattern)              │   │
│  │                                                                          │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                     │   │
│  │  │ SQLite      │  │ SQL Server  │  │ PostgreSQL  │                     │   │
│  │  │ (Default)   │  │ (Enterprise)│  │ (Future)    │                     │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘                     │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
└─────────────────────────────────────────────┬───────────────────────────────────┘
                                              │
                          HTTPS (Config Pull / Health Push)
                                              │
         ┌────────────────────────────────────┼────────────────────────────────────┐
         │                                    │                                    │
         ▼                                    ▼                                    ▼
┌─────────────────────┐          ┌─────────────────────┐          ┌─────────────────────┐
│  TOOL SERVER (US)   │          │  TOOL SERVER (EU)   │          │  TOOL SERVER (APAC) │
│                     │          │                     │          │                     │
│  Windows Container  │          │  Windows Container  │          │  Windows Container  │
│  + gMSA             │          │  + gMSA             │          │  + gMSA             │
└──────────┬──────────┘          └──────────┬──────────┘          └──────────┬──────────┘
           │                                │                                │
           ▼                                ▼                                ▼
    US AD Domain                     EU AD Domain                    APAC AD Domain
```

### Component Details

#### Admin Portal (Web UI)

| Feature | Description | MVP | Future |
|---------|-------------|-----|--------|
| Dashboard | Health overview, alerts, recent activity | ✅ | Metrics, trends |
| Service Accounts | Create, edit, delete, test accounts | ✅ | Bulk import |
| Capability Mappings | Map accounts to capabilities with scopes | ✅ | Visual scope builder |
| Tool Servers | Register, monitor, configure | ✅ | Auto-discovery |
| Rules & Prompts | Customize agent behavior | ⬜ | Version control |
| Audit Logs | Search and filter action history | ✅ | Export, retention policies |
| Settings | Global configuration | ✅ | Backup/restore |

**Technology Choice: Blazor Server**
- Stays within .NET ecosystem (consistent with Tool Server)
- Real-time updates via SignalR (built-in)
- Server-side rendering (no WASM download delay)
- Full access to .NET libraries
- Simpler authentication integration

#### Config Service API

RESTful API for Tool Servers and Admin Portal:

```
Authentication:
  POST   /api/auth/login
  POST   /api/auth/logout
  POST   /api/auth/refresh
  GET    /api/auth/me

Service Accounts:
  GET    /api/service-accounts
  GET    /api/service-accounts/{id}
  POST   /api/service-accounts
  PUT    /api/service-accounts/{id}
  DELETE /api/service-accounts/{id}
  POST   /api/service-accounts/{id}/test

Tool Servers:
  GET    /api/tool-servers
  GET    /api/tool-servers/{id}
  POST   /api/tool-servers/register
  PUT    /api/tool-servers/{id}
  DELETE /api/tool-servers/{id}
  GET    /api/tool-servers/{id}/config
  POST   /api/tool-servers/{id}/heartbeat

Capability Mappings:
  GET    /api/capabilities
  GET    /api/capabilities/{toolServerId}
  POST   /api/capabilities
  PUT    /api/capabilities/{id}
  DELETE /api/capabilities/{id}

Audit:
  GET    /api/audit
  GET    /api/audit/{id}
  POST   /api/audit  (Tool Servers post events here)
```

#### Database Abstraction

**Pattern:** Repository + Unit of Work with EF Core

```csharp
// Repository interface - database agnostic
public interface IServiceAccountRepository
{
    Task<ServiceAccount?> GetByIdAsync(Guid id);
    Task<IEnumerable<ServiceAccount>> GetAllAsync();
    Task<ServiceAccount> AddAsync(ServiceAccount account);
    Task UpdateAsync(ServiceAccount account);
    Task DeleteAsync(Guid id);
    Task<IEnumerable<ServiceAccount>> GetByDomainAsync(string domain);
}

// EF Core implementation works with any provider
public class ServiceAccountRepository : IServiceAccountRepository
{
    private readonly LucidDbContext _context;
    // Implementation uses _context which could be SQLite, SQL Server, etc.
}
```

**Database Providers:**
- **MVP:** SQLite (zero configuration, file-based, good for single-node)
- **Enterprise:** SQL Server (familiar to Windows shops, HA options)
- **Future:** PostgreSQL (open source option for Linux-preferred shops)

Switching providers requires only:
1. Change connection string
2. Change EF Core provider NuGet package
3. Run migrations

### Authentication Strategy

**Phase 1 (MVP):** Local accounts
- Built-in user store in database
- Username/password with secure hashing (Argon2 or bcrypt)
- JWT tokens for API authentication
- Role-based access control (Admin, Operator, Viewer)

**Phase 2:** Active Directory
- LDAP bind authentication
- Map AD groups to application roles
- Optional: AD user sync for audit attribution

**Phase 3:** Entra ID (Azure AD)
- OpenID Connect / OAuth 2.0
- Support for MFA
- Conditional access policies honored

### Deployment Model

**Container-based (Recommended):**
```
┌─────────────────────────────────────────────────────────┐
│                    Linux Host (Docker)                   │
│  ┌───────────────────────────────────────────────────┐  │
│  │        lucid-admin-portal:latest                   │  │
│  │        (ASP.NET Core + Blazor Server)              │  │
│  │                                                     │  │
│  │        Ports: 443 (HTTPS), 80 (redirect)          │  │
│  │        Volume: /data (SQLite database)            │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

**Standalone (Alternative):**
- Windows or Linux server
- .NET 8 runtime
- Reverse proxy (nginx, IIS, Caddy) for TLS termination

## Consequences

### Benefits

1. **Centralized Management** - Single UI for all Lucid IT Agent configuration
2. **Enterprise Ready** - Audit logging, RBAC, health monitoring
3. **Scalable** - Supports multiple Tool Servers across regions/domains
4. **Secure** - No more Domain Admin; proper service account management
5. **Flexible Auth** - Local, AD, and Entra ID support planned
6. **Database Agnostic** - Start simple (SQLite), scale up (SQL Server) without code changes

### Trade-offs

1. **Additional Component** - One more thing to deploy and maintain
2. **Network Dependency** - Tool Servers need connectivity to Config Service
3. **Complexity** - More moving parts than a single monolithic application

### Mitigations

| Trade-off | Mitigation |
|-----------|------------|
| Additional component | Single container deployment, minimal config |
| Network dependency | Tool Servers cache config; graceful degradation if Config Service unreachable |
| Complexity | Clear separation of concerns; each component has focused responsibility |

## Data Model

### Core Entities

```
┌─────────────────────┐       ┌─────────────────────┐
│   ServiceAccount    │       │    ToolServer       │
├─────────────────────┤       ├─────────────────────┤
│ Id                  │       │ Id                  │
│ Name                │       │ Name                │
│ DisplayName         │       │ DisplayName         │
│ Type (gMSA/Service) │       │ Endpoint            │
│ Domain              │       │ Domain              │
│ CredentialSource    │       │ Status              │
│ CredentialKey       │       │ LastHeartbeat       │
│ HealthStatus        │       │ Version             │
│ LastHealthCheck     │       │ CreatedAt           │
│ CreatedAt           │       │ UpdatedAt           │
│ UpdatedAt           │       └──────────┬──────────┘
└──────────┬──────────┘                  │
           │                             │
           │    ┌────────────────────────┘
           │    │
           ▼    ▼
┌─────────────────────────────────┐
│      CapabilityMapping          │
├─────────────────────────────────┤
│ Id                              │
│ ServiceAccountId (FK)           │
│ ToolServerId (FK)               │
│ Capability (enum)               │
│ AllowedScopes (JSON)            │
│ DeniedScopes (JSON)             │
│ IsEnabled                       │
│ CreatedAt                       │
│ UpdatedAt                       │
└─────────────────────────────────┘

┌─────────────────────┐       ┌─────────────────────┐
│       User          │       │     AuditEvent      │
├─────────────────────┤       ├─────────────────────┤
│ Id                  │       │ Id                  │
│ Username            │       │ Timestamp           │
│ Email               │       │ ToolServerId (FK)   │
│ PasswordHash        │       │ Capability          │
│ Role                │       │ Action              │
│ IsEnabled           │       │ TargetUser          │
│ LastLogin           │       │ TargetResource      │
│ CreatedAt           │       │ TicketNumber        │
│ UpdatedAt           │       │ Success             │
└─────────────────────┘       │ ErrorMessage        │
                              │ Details (JSON)      │
                              └─────────────────────┘
```

## Implementation Plan

### Phase 1: Core Infrastructure
- Solution structure and project scaffolding
- Database abstraction layer with EF Core
- SQLite provider implementation
- Core entity models and repositories
- Basic API endpoints (CRUD)

### Phase 2: Tool Server Integration
- Tool Server registration endpoint
- Configuration pull endpoint
- Heartbeat endpoint
- Update Tool Server to use Config Service

### Phase 3: Admin Portal MVP
- Blazor Server project setup
- Local authentication (login/logout)
- Dashboard with health overview
- Service Account management pages
- Tool Server management pages
- Basic audit log viewer

### Phase 4: Enhanced Features
- Capability mapping UI
- Rules and prompts configuration
- AD authentication integration
- Alerting and notifications

### Phase 5: Enterprise Features
- Entra ID authentication
- SQL Server provider
- Advanced audit search and export
- Backup and restore
- Multi-tenancy (future consideration)

## References

- [Blazor Server Documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models#blazor-server)
- [EF Core Database Providers](https://learn.microsoft.com/en-us/ef/core/providers/)
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [Repository Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)
