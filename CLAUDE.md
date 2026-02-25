# Praxova IT Agent — Claude Code Instructions

## What This System Is

**Praxova IT Agent** is an enterprise AI-powered IT helpdesk automation platform. It
monitors IT ticket queues (ServiceNow), classifies tickets using an LLM, routes them
through configurable visual workflows, optionally holds for human approval, then
executes the resolution against Active Directory or other target systems via a
Windows Tool Server.

The project is approaching a v1 public release (Apache 2.0). The business model is
open source core with paid implementation services and annual support contracts.

---

## Deployment Topology (Current — Know This Before Touching Security Code)

```
┌─────────────────────────────────────── Docker Host (Ubuntu, Proxmox VM) ───────────────────────────────────┐
│                                        docker-compose stack: praxova                                        │
│                                                                                                             │
│  ┌──────────────────────┐   HTTPS    ┌──────────────────────┐   HTTP     ┌────────────────────────┐        │
│  │  admin-portal        │◄──────────►│  agent               │◄──────────►│  ollama                │        │
│  │  Blazor Server       │            │  Python / Griptape   │            │  llama3.1              │        │
│  │  .NET 8              │            │                      │            │  (local LLM)           │        │
│  │  Port 443 (ext)      │            │  Polls ServiceNow    │            │  Port 11434            │        │
│  │  Internal CA cert    │            │  Runs workflows      │            │                        │        │
│  └──────────┬───────────┘            └──────────┬───────────┘            └────────────────────────┘        │
│             │                                   │                                                           │
│             │ SQLite / SQL Server                │ HTTPS + mTLS (internal CA)                              │
│             │ (mounted volume)                   │                                                           │
└─────────────┼───────────────────────────────────┼───────────────────────────────────────────────────────────┘
              │                                   │
              │ REST API calls (HTTPS)             │ Capability routing via portal API
              │                                   │ then direct HTTPS to tool server
              ▼                                   ▼
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  tool01.montanifarms.com  (Windows Server 2022, domain-joined, Proxmox VM)           │
│                                                                                      │
│  ┌────────────────────────────────────────────────────────────────────────────────┐  │
│  │  Praxova Tool Server  (.NET 8, Windows Service)                                │  │
│  │  Port 8443 (HTTPS, internal CA cert)                                           │  │
│  │  mTLS: validates agent client certificate                                      │  │
│  │                                                                                │  │
│  │  Capabilities: ad-password-reset, ad-group-add, ad-group-remove,              │  │
│  │                ntfs-permission-grant, ntfs-permission-revoke                  │  │
│  └──────────────────────────────────────┬─────────────────────────────────────────┘  │
│                                         │ LDAPS (port 636) or Kerberos               │
│                                         ▼                                            │
│  ┌────────────────────────────────────────────────────────────────────────────────┐  │
│  │  dc01.montanifarms.com  (Windows Server 2022 DC)                               │  │
│  │  Domain: montanifarms.com                                                      │  │
│  │  AD DS, DNS                                                                    │  │
│  └────────────────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

**Key facts:**
- Docker containers reference each other by container name (e.g., `admin-portal`, `agent`, `ollama`)
- The admin portal's URL from the agent's perspective is `https://admin-portal/`
- The tool server hostname and port are **configurable in the portal** — do not hardcode
- All inter-component TLS uses the **Praxova internal CA** (generated at install time, ADR-014)
- The agent holds a **client certificate** for mTLS with the tool server
- The tool server runs as a **Windows Service** (not containerized)
- Active Directory operations are performed by the **tool server**, not the agent directly
- The agent never holds AD credentials — capability routing abstracts this entirely

---

## Security Architecture (ADR-014 + ADR-015 — Fully Implemented)

### Certificate Trust Chain

```
Praxova Root CA  (RSA 4096, auto-generated at install, stored encrypted)
├── admin-portal.crt    (HTTPS server cert, SAN: admin-portal, <host-ip>)
├── agent-client.crt    (mTLS client cert for agent → tool server calls)
├── tool-server.crt     (HTTPS server cert for tool server)
└── ollama.crt          (if Ollama is HTTPS — currently HTTP internally)
```

The CA certificate is distributed to:
- Docker containers via volume mount at `/etc/ssl/certs/praxova-ca.pem`
- The .NET runtime trust store via bootstrap script at container startup
- The Windows tool server via manual install into Local Machine\Trusted Root CA

### Secrets Storage (Envelope Encryption)

Credentials (LDAP bind password, API keys, ServiceNow password) are stored with:
- AES-256-GCM encryption
- Per-secret Data Encryption Keys (DEKs)
- DEKs encrypted by a Key Encryption Key (KEK)
- KEK derived from master passphrase via Argon2id (held in memory only, never persisted)

The admin portal must **unseal** on startup before secrets are available. Unseal
source is configured via `PRAXOVA_MASTER_KEY` environment variable (Docker secret
in production, env var in dev).

**Never log secret values. `SecretString` type overrides `ToString()` → `[REDACTED]`.**

### Tool Server Authentication Flow

```
Agent                    Admin Portal              Tool Server              Active Directory
  │                           │                         │                         │
  │  1. GET /api/capabilities/│                         │                         │
  │     ad-password-reset/    │                         │                         │
  │     servers               │                         │                         │
  │──────────────────────────►│                         │                         │
  │                           │                         │                         │
  │  2. Returns tool server   │                         │                         │
  │     URL + service account │                         │                         │
  │◄──────────────────────────│                         │                         │
  │                           │                         │                         │
  │  3. POST /api/v1/tools/   │                         │                         │
  │     password/reset        │                         │                         │
  │     [mTLS: agent-client.crt]                        │                         │
  │────────────────────────────────────────────────────►│                         │
  │                           │                         │                         │
  │                           │                         │  4. LDAPS bind as       │
  │                           │                         │     svc-praxova@        │
  │                           │                         │     montanifarms.com    │
  │                           │                         │────────────────────────►│
  │                           │                         │                         │
  │                           │                         │  5. SetPassword(user)   │
  │                           │                         │────────────────────────►│
```

**Current issue (as of 2026-02-25):** The DC is rejecting the password reset
operation at step 5. The agent-trust-bootstrap work (mTLS) is functioning correctly.
The failure is in the tool server's AD permissions — specifically the `svc-praxova`
service account's delegation rights in Active Directory. Do not attempt to work
around this by bypassing mTLS or reverting to plaintext LDAP.

---

## Core Architecture: Composable Workflows (ADR-011 — Fully Implemented)

### The Dispatcher Pattern

Classification runs **once** in a dispatcher workflow, then routes to a specialized
sub-workflow. Sub-workflows are reusable building blocks.

```
Incoming Ticket
      │
      ▼
┌─────────────────────────────────────────────────────┐
│  DISPATCHER WORKFLOW                                │
│                                                     │
│  [Trigger] → [Classify] → (routes by ticket type)  │
│                               │                     │
│               ┌───────────────┼──────────────────┐  │
│               │               │                  │  │
│               ▼               ▼                  ▼  │
│    [SubWorkflow:        [SubWorkflow:    [SubWorkflow: │
│     Password Reset]     Group Access]   Escalate]   │
└─────────────────────────────────────────────────────┘
```

Each sub-workflow is a separate `WorkflowDefinition` stored in the portal. The
`SubWorkflowExecutor` in the Python agent fetches the sub-workflow definition at
runtime and creates a child `WorkflowEngine` instance to run it.

### Step Types (StepType enum)

| StepType | Description |
|----------|-------------|
| `Trigger` | Entry point — ServiceNow poll, manual, webhook, email (T1-T5) |
| `Classify` | LLM classification — runs once in dispatcher |
| `ToolCall` | Calls a capability on a tool server via CapabilityRouter |
| `Approval` | Human-in-the-loop gate — pauses execution (ADR-013) |
| `Notify` | Sends notification (ServiceNow comment, email, Teams) |
| `Condition` | Branching logic based on context variables |
| `SetVariable` | Sets a key in the ExecutionContext |
| `SubWorkflow` | Invokes another WorkflowDefinition as a child |
| `Escalate` | Terminal — escalates to human, closes workflow |
| `End` | Terminal — successful completion |

### Human Approval (ADR-013 — Fully Implemented)

When an `Approval` step is reached:
1. Execution **pauses** — the WorkflowExecution is persisted with status `AwaitingApproval`
2. The admin portal creates a `PendingApproval` record and shows it in the UI
3. A human approves or rejects in the portal
4. The agent's approval poller detects the decision and **resumes** the workflow
5. Approved → execution continues to the next step
6. Rejected → execution routes to the rejection branch (typically `Escalate`)

Approval state is stored in the database and survives agent restarts.

---

## Repository Structure

```
praxova-it-agent/
├── agent/                          # Python agent (Griptape framework)
│   └── src/agent/
│       ├── classifier/             # LLM-powered ticket classification
│       ├── pipeline/               # TicketExecutor, WorkflowEngine
│       │   ├── executors/          # Step executors (ToolCallExecutor, ApprovalExecutor, etc.)
│       │   └── handlers/           # Legacy handlers (being replaced by workflows)
│       ├── tools/                  # Griptape tool wrappers → tool server HTTP calls
│       ├── routing/                # CapabilityRouter — queries portal for tool server URL
│       └── drivers/                # LLM driver factory (Ollama, OpenAI, Anthropic, etc.)
│
├── admin/dotnet/                   # Blazor Server Admin Portal (.NET 8)
│   └── src/
│       ├── LucidAdmin.Core/        # Domain entities, interfaces (no dependencies)
│       ├── LucidAdmin.Infrastructure/  # EF Core, secrets service, cert manager
│       └── LucidAdmin.Web/         # Blazor UI + REST API (Minimal APIs)
│           ├── Components/Pages/   # Blazor pages
│           └── Endpoints/          # REST API endpoints
│
├── tool-server/dotnet/             # .NET 8 Windows Tool Server
│   └── src/LucidToolServer/
│       ├── Services/               # IActiveDirectoryService, IFilePermissionService
│       └── Program.cs              # Minimal API entry point
│
├── docker/                         # Docker Compose, Dockerfiles, cert volumes
├── infra/                          # Packer templates, OpenTofu configs (Proxmox)
├── docs/
│   ├── adr/                        # Architecture Decision Records (ADR-005 to ADR-015)
│   ├── infra/                      # Proxmox and Windows infra runbooks
│   └── prompts/                    # Active Claude Code build prompts (unshipped features only)
├── scripts/                        # Utility scripts (cert provisioning, etc.)
├── env-setup/                      # DC and ServiceNow test environment setup
└── CLAUDE.md                       # This file
```

---

## Technology Stack

| Component | Technology |
|-----------|------------|
| Admin Portal | Blazor Server, .NET 8, MudBlazor, EF Core, SQLite (dev) |
| Agent | Python 3.11+, Griptape 1.9.0 |
| Tool Server | .NET 8 Minimal API, System.DirectoryServices, Windows Service |
| LLM (default) | Ollama + llama3.1:latest |
| Container runtime | Docker Compose (Linux host) |
| Lab infrastructure | Proxmox VE, OpenTofu, Packer |

---

## Test Environment

- **Domain**: `montanifarms.com`
- **DC**: `dc01.montanifarms.com`
- **Tool Server**: `tool01.montanifarms.com` (port 8443)
- **Test users**: Star Wars themed — Luke Skywalker (`lskywalker`), Han Solo (`hsolo`), etc.
- **ServiceNow PDI**: `dev341394.service-now.com`, assignment group `Help Desk`
- **Service account**: `svc-praxova@montanifarms.com` (tool server runs as this account)

---

## Coding Standards

**C# (.NET)**
- Record types for DTOs (immutable, `init` properties)
- XML doc comments on public interfaces
- Minimal APIs (not MVC controllers) for REST endpoints
- `IOptions<T>` for config binding
- `ILogger<T>` for logging
- `SecretString` type — never log actual secret values

**Python**
- Type hints on all public functions
- Pydantic models for all data structures
- Google-style docstrings
- Griptape 1.9.0 import paths: `from griptape.drivers.prompt.ollama import OllamaPromptDriver`

---

## Git Workflow

- **Always commit after completing work** with a descriptive conventional commit message
- Format: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`, `security:`
- Current active branch: `feature/agent-trust-bootstrap` (security work in progress)
- Do **NOT** push without explicit permission

---

## Architecture Decision Records

| ADR | Title | Status |
|-----|-------|--------|
| ADR-005 | Windows-Native Tool Server (.NET) | Implemented |
| ADR-006 | Admin Portal as Config Service | Implemented |
| ADR-009 | LLM Reasons, Tools Execute | Implemented |
| ADR-010 | Visual Workflow Designer | Implemented |
| ADR-011 | Composable Workflows & Pluggable Triggers | Implemented (E2E validated 2026-02-06) |
| ADR-012 | Dynamic Classification Training | Implemented |
| ADR-013 | Human-in-the-Loop Approval | Implemented (all phases) |
| ADR-014 | Certificate Management & Internal PKI | Implemented |
| ADR-015 | Secrets Management & Credential Security | Implemented |

Full ADR documents: `docs/adr/`
