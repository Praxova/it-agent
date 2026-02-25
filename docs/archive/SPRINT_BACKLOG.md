# Sprint Backlog

## Overview

This document tracks the development sprints for Lucid IT Agent. Each sprint is approximately 1-2 weeks depending on complexity.

**Updated**: January 2025 - Revised to reflect .NET Tool Server migration and Admin Portal architecture.

**MVP Target**: Enterprise-ready IT automation agent with centralized management.

---

## Completed Work

### Sprint 0: Foundation ✅

- [x] Project structure created
- [x] Documentation framework (README, ARCHITECTURE, CLAUDE_CONTEXT)
- [x] License selection (Apache 2.0)
- [x] Local development environment (Ollama + Llama 3.1 8B)
- [x] Basic agent scaffold with tool calling test (5/5 tests passing)
- [x] DC test environment PowerShell script

### Python Tool Server MVP ✅ (Deprecated)

- [x] FastAPI application with health endpoint
- [x] Password reset via LDAP (blocked by LDAPS requirement)
- [x] Group membership operations
- [x] File permissions via WinRM
- **Status**: Deprecated per ADR-005. Replaced by .NET Tool Server.

### .NET Tool Server ✅

- [x] ASP.NET Core Minimal API scaffold
- [x] All 9 API endpoints matching Python version
- [x] Native AD operations via System.DirectoryServices.AccountManagement
- [x] Native NTFS operations via System.Security.AccessControl
- [x] Windows container support (Dockerfile)
- [x] Password reset working (the killer feature!)
- [x] Group operations working
- [x] File permissions working
- [x] Accessible from Linux workstation

---

## Current Sprint: Service Account & Multi-Account Support

**Goal**: Proper service account configuration and multi-account architecture

**Status**: 🟡 In Progress

### Tasks

- [ ] **Service Account Setup (gMSA)**
  - [ ] Create gMSA on Domain Controller
  - [ ] Grant AD permissions (password reset, group management)
  - [ ] Grant file share permissions
  - [ ] Install gMSA on Tool Server host
  - [ ] Test running app as gMSA
  - [ ] Document setup process

- [ ] **Multi-Account Support in Tool Server**
  - [ ] Update ToolServerSettings to support multiple accounts
  - [ ] Create CredentialProvider abstraction
    - [ ] gMSA provider
    - [ ] Service account provider (with password)
    - [ ] Current user provider (for dev/testing)
  - [ ] Map capabilities to accounts in configuration
  - [ ] Update services to use mapped accounts
  - [ ] Per-capability health checks

- [ ] **Configuration Schema**
  - [ ] Define YAML/JSON schema for service accounts
  - [ ] Define capability mapping schema
  - [ ] Validation on startup
  - [ ] Helpful error messages for misconfiguration

### Acceptance Criteria

1. Tool server runs as gMSA (not Domain Admin)
2. Configuration supports multiple accounts per capability
3. Health endpoint reports status per capability
4. Clear documentation for service account setup

---

## Next Sprint: Admin Portal - Foundation

**Goal**: Config Service API and database layer

**Status**: ⬜ Not Started

### Tasks

- [ ] **Project Setup**
  - [ ] Create LucidAdmin.sln solution structure
  - [ ] LucidAdmin.Core (domain layer)
  - [ ] LucidAdmin.Infrastructure (data layer)
  - [ ] LucidAdmin.Api (Config Service)
  - [ ] Configure dependency injection

- [ ] **Database Layer**
  - [ ] Define entity classes
  - [ ] EF Core DbContext with provider abstraction
  - [ ] SQLite provider configuration
  - [ ] Repository implementations
  - [ ] Initial migration

- [ ] **Core Entities**
  - [ ] ServiceAccount entity
  - [ ] CapabilityMapping entity
  - [ ] ToolServerRegistration entity
  - [ ] User entity (for local auth)
  - [ ] AuditLogEntry entity

- [ ] **Config Service API - CRUD**
  - [ ] Service account endpoints
  - [ ] Capability mapping endpoints
  - [ ] Tool server registration endpoints
  - [ ] Basic error handling

### Acceptance Criteria

1. API runs and connects to SQLite database
2. Can CRUD service accounts via API
3. Can register/list tool servers
4. Database migrations work correctly

---

## Sprint: Admin Portal - Tool Server Integration

**Goal**: Tool servers pull config from and report to Admin

**Status**: ⬜ Not Started

### Tasks

- [ ] **Tool Server Registration Flow**
  - [ ] Tool server registers on startup
  - [ ] Config Service stores registration
  - [ ] Tool server pulls configuration

- [ ] **Heartbeat & Health**
  - [ ] Tool server sends periodic heartbeat
  - [ ] Config Service tracks last seen
  - [ ] Health status aggregation
  - [ ] Capability-level health tracking

- [ ] **Configuration Push/Pull**
  - [ ] Tool server polls for config changes
  - [ ] Or: webhook notification on config change
  - [ ] Config version tracking
  - [ ] Graceful handling of Config Service unavailability

- [ ] **Audit Log Collection**
  - [ ] Tool server sends audit entries
  - [ ] Config Service stores and indexes
  - [ ] Query endpoint for audit logs

### Acceptance Criteria

1. Tool server registers with Config Service
2. Tool server gets configuration from Config Service
3. Health dashboard shows tool server status
4. Audit logs flow from Tool Server to Config Service

---

## Sprint: Admin Portal - Web UI

**Goal**: Blazor-based admin interface

**Status**: ⬜ Not Started

### Tasks

- [ ] **Project Setup**
  - [ ] LucidAdmin.Web Blazor Server project
  - [ ] Shared layout and navigation
  - [ ] CSS/styling framework (Bootstrap or Tailwind)
  - [ ] API client service

- [ ] **Authentication**
  - [ ] Login page
  - [ ] Local user authentication
  - [ ] JWT token handling
  - [ ] Protected routes

- [ ] **Dashboard**
  - [ ] Tool server health cards (green/yellow/red)
  - [ ] Recent activity feed
  - [ ] Quick stats (tickets processed, success rate)

- [ ] **Service Account Management**
  - [ ] List service accounts
  - [ ] Create new account form
  - [ ] Edit account
  - [ ] Delete with confirmation
  - [ ] Test account connectivity

- [ ] **Tool Server Management**
  - [ ] List registered tool servers
  - [ ] View details and health
  - [ ] Capability status per server

### Acceptance Criteria

1. Can log in to Admin Portal
2. Dashboard shows tool server health
3. Can manage service accounts through UI
4. Can view tool server status

---

## Sprint: Admin Portal - Capability Mappings & Audit

**Goal**: Complete the management UI

**Status**: ⬜ Not Started

### Tasks

- [ ] **Capability Mapping UI**
  - [ ] List all mappings
  - [ ] Create mapping (tool + capability + account)
  - [ ] Edit scope restrictions
  - [ ] Enable/disable mappings

- [ ] **Audit Log Viewer**
  - [ ] Searchable audit log table
  - [ ] Filter by date, tool server, action type
  - [ ] Export to CSV
  - [ ] Detail view for entries

- [ ] **Settings Pages**
  - [ ] Protected groups/accounts configuration
  - [ ] Global settings
  - [ ] About/version info

### Acceptance Criteria

1. Can create and manage capability mappings
2. Can search and filter audit logs
3. Can configure protected groups
4. All CRUD operations work through UI

---

## Future Sprints (Backlog)

### Authentication Enhancements
- Active Directory authentication
- Entra ID (Azure AD) integration
- Role-based access control
- MFA support

### Multi-Domain Support
- Multiple AD domain configuration
- Cross-domain operations
- Forest trust handling

### Agent Integration (Full Loop)
- ServiceNow connector completion
- Ticket classification
- Execution pipeline
- User communication
- End-to-end testing

### Production Hardening
- Structured logging throughout
- Metrics and monitoring
- Alerting integration
- Performance optimization
- Security audit

### Advanced Features
- Rules/prompts configuration UI
- Approval workflows
- Scheduled tasks
- Reporting and analytics

### Container Improvements
- Tool Server with gMSA in container
- Admin Portal container
- Docker Compose for full stack
- Kubernetes manifests

---

## Architecture Documents

| Document | Purpose |
|----------|---------|
| ADR-005 | Windows-native Tool Server with .NET |
| ADR-006 | Admin Portal and Config Service |
| WINDOWS_CONTAINERS_PRIMER.md | Guide for Windows containers |
| ADMIN_PROJECT_STRUCTURE.md | Admin component structure |

---

## Sprint Metrics Template

Track these for each completed sprint:

| Metric | Target | Actual |
|--------|--------|--------|
| Tasks Completed | 100% | - |
| Test Coverage | >80% | - |
| Bugs Found | <3 | - |
| Bugs Fixed | 100% | - |
| Documentation Updated | Yes | - |

---

## Notes

- Service account setup is critical path for production readiness
- Admin Portal can be developed in parallel with service account work
- Database abstraction layer is foundational - get it right early
- SQLite for MVP, plan for SQL Server migration
