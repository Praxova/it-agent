# ADR-005: Windows-Native Tool Server with .NET and Containers

## Status

**Accepted** - January 2025

## Context

The Lucid IT Agent tool server was initially implemented in Python running on Linux to accelerate MVP development. This approach used:

- `ldap3` library for Active Directory operations via LDAP/LDAPS
- `pywinrm` for remote PowerShell execution to Windows servers
- SSH/SMB bridges for file permission operations

This cross-platform approach has proven problematic:

1. **Password Reset Barrier**: Active Directory requires LDAPS (TLS) for password modifications via the `unicodePwd` attribute. Configuring LDAPS certificates from Linux is complex and error-prone.

2. **Authentication Complexity**: Service account credentials must be stored in configuration files. No support for Group Managed Service Accounts (gMSA).

3. **Double-Hop Issues**: WinRM introduces Kerberos double-hop authentication challenges that require complex delegation configuration.

4. **NTFS ACL Limitations**: Native NTFS ACL manipulation is not possible from Linux; workarounds via PowerShell remoting add latency and complexity.

5. **Operational Overhead**: Two distinct technology stacks (Python/Linux for agent, PowerShell/Windows for AD operations) increases maintenance burden.

Password reset automation represents approximately 50% of Level 1 IT tickets, making this a critical path for the product's value proposition.

## Decision

**Migrate the tool server to a Windows-native implementation using .NET 8 running in Windows containers.**

### Technology Choices

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Runtime | .NET 8 (LTS) | Cross-platform development, native Windows integration, LTS until Nov 2026 |
| API Framework | ASP.NET Core Minimal APIs | Lightweight, high performance, less ceremony than MVC controllers |
| AD Integration | System.DirectoryServices.AccountManagement | High-level API with native Kerberos authentication |
| File Permissions | System.Security.AccessControl | Native NTFS ACL manipulation |
| Authentication | Group Managed Service Account (gMSA) | Passwordless, auto-rotating credentials |
| Deployment | Windows Container (Process Isolation) | Consistent deployment, isolation, easy updates |
| Base Image | mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022 | Official Microsoft LTS image |

### Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                     AGENT HOST (Linux or Windows)                          │
│  ┌────────────────────┐  ┌────────────────────┐                           │
│  │   Griptape Agent   │  │      Ollama        │                           │
│  │   (Python)         │  │   (Llama 3.1)      │                           │
│  └─────────┬──────────┘  └────────────────────┘                           │
└────────────┼──────────────────────────────────────────────────────────────┘
             │ HTTPS (REST API)
             ▼
┌────────────────────────────────────────────────────────────────────────────┐
│              WINDOWS SERVER (Domain-Joined Container Host)                  │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │              Windows Container (Process Isolation)                    │ │
│  │  ┌────────────────────────────────────────────────────────────────┐  │ │
│  │  │           .NET 8 Tool Server (ASP.NET Core Minimal API)         │  │ │
│  │  │                                                                  │  │ │
│  │  │   Services:                                                      │  │ │
│  │  │   - ActiveDirectoryService (password, groups, user queries)     │  │ │
│  │  │   - FilePermissionService (NTFS ACL operations)                 │  │ │
│  │  │                                                                  │  │ │
│  │  │   Security:                                                      │  │ │
│  │  │   - API Key authentication                                       │  │ │
│  │  │   - Protected groups/accounts deny list                          │  │ │
│  │  │   - Structured audit logging (Serilog)                          │  │ │
│  │  └────────────────────────────────────────────────────────────────┘  │ │
│  │                          │                                            │ │
│  │              gMSA Credential Spec (Kerberos)                         │ │
│  └──────────────────────────┼───────────────────────────────────────────┘ │
│                             ▼                                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐           │
│  │ Active Directory│  │  File Servers   │  │   Future:       │           │
│  │ Domain Services │  │  (NTFS ACLs)    │  │   Exchange/M365 │           │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘           │
└────────────────────────────────────────────────────────────────────────────┘
```

## Consequences

### Benefits

1. **Native Password Reset**: `UserPrincipal.SetPassword()` works without LDAPS configuration complexity.

2. **Automatic Authentication**: gMSA provides passwordless service authentication with automatic credential rotation.

3. **Native NTFS Operations**: `System.Security.AccessControl` provides full ACL manipulation without remote execution.

4. **Simplified Operations**: Single technology stack for Windows operations; no WinRM or remote PowerShell.

5. **Better Security Posture**: No stored credentials, gMSA audit trail, compiled code.

6. **Customer-Friendly Deployment**: Windows containers are familiar to Windows admins; `docker run` is simpler than manual service installation.

### Trade-offs

1. **Development Environment**: Requires Windows Server for integration testing (unit tests work on Linux).

2. **Learning Curve**: Team must learn Windows container concepts and gMSA configuration.

3. **Container Host Requirements**: Requires Windows Server 2019+ with Containers feature and domain membership.

4. **Image Size**: Windows container images are larger (~5GB vs ~100MB for Linux).

### Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| gMSA configuration complexity | Provide detailed setup documentation and validation scripts |
| Windows container unfamiliarity | Document Docker commands; provide docker-compose for development |
| OS version compatibility | Use LTSC 2022 base image; document supported host OS versions |
| Debugging containers | Include health endpoints; structured logging with log level config |

## API Compatibility

The .NET implementation will maintain exact API compatibility with the Python version:

- Same route paths (`/api/v1/...`)
- Same request/response JSON schemas
- Same HTTP status codes and error formats

This allows the agent to switch tool servers by changing a URL configuration.

## Implementation Plan

### Phase 1: Foundation & Password Reset (Week 1-2)
- .NET 8 solution structure
- ASP.NET Core Minimal API scaffold
- ActiveDirectoryService with password reset
- Dockerfile for Windows container
- Health endpoint
- Integration tests against development DC

### Phase 2: Group Operations (Week 3)
- Group membership add/remove
- Group info query
- User groups query
- Protected groups deny list

### Phase 3: File Permissions (Week 4)
- Grant permission (Read/Write)
- Revoke permission
- List permissions
- Allowed paths configuration

### Phase 4: Hardening & Documentation (Week 5)
- API key authentication
- Structured logging (Serilog)
- gMSA setup documentation
- Customer deployment guide
- Deprecate Python tool server

## Alternatives Considered

### PowerShell-based Tool Server (Pode)
- **Pros**: No C# required, PowerShell expertise common in Windows shops
- **Cons**: Less performant for high volume, less "enterprise" appearance, harder to unit test

### Python on Windows
- **Pros**: Keep existing codebase, team familiarity
- **Cons**: pywin32/pyad still require complex AD configuration, doesn't solve password reset issue

### WinRM from Linux (Current)
- **Pros**: Agent and tool server on same Linux host
- **Cons**: Double-hop issues, credential management, not a production-quality solution

## References

- [Microsoft: gMSA for Windows Containers](https://learn.microsoft.com/en-us/virtualization/windowscontainers/manage-containers/manage-serviceaccounts)
- [.NET 8 Windows Container Support](https://learn.microsoft.com/en-us/dotnet/core/docker/introduction)
- [System.DirectoryServices.AccountManagement](https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.accountmanagement)
- [Windows Container Version Compatibility](https://learn.microsoft.com/en-us/virtualization/windowscontainers/deploy-containers/version-compatibility)
