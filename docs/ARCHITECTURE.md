# Praxova IT Agent — Architecture

## System Overview

Praxova IT Agent is an enterprise AI-powered IT helpdesk automation platform. It
monitors IT ticket queues, classifies tickets using an LLM, routes them through
configurable visual workflows with optional human approval gates, then executes
resolutions against target systems via authenticated tool servers.

**Design priorities:**
- Security-first — every inter-component connection is authenticated and encrypted
- Supervised learning — the system improves through human feedback, not autonomous drift
- Separation of concerns — the agent never holds target system credentials directly
- Composability — workflows are built from reusable steps and sub-workflows
- Operator control — the admin portal is the single source of truth for all configuration

---

## Deployment Topology

```
┌────────────────────────────────── Docker Host (Ubuntu, Proxmox VM) ──────────────────────────────────┐
│                                   docker-compose stack: praxova                                       │
│                                                                                                       │
│  ┌─────────────────────┐  HTTPS  ┌─────────────────────┐  HTTP  ┌──────────────────────────────┐    │
│  │  admin-portal       │◄───────►│  agent              │◄──────►│  ollama                      │    │
│  │  Blazor Server      │         │  Python / Griptape  │        │  llama3.1:latest             │    │
│  │  .NET 8             │         │                     │        │  Port 11434                  │    │
│  │  Port 443 (ext)     │         │  Polls ServiceNow   │        │                              │    │
│  │  Internal CA cert   │         │  Runs workflows     │        │  Accessible only within      │    │
│  └──────────┬──────────┘         └──────────┬──────────┘        │  Docker network              │    │
│             │                               │                   └──────────────────────────────┘    │
│             │ SQLite / SQL Server            │ HTTPS (internal CA)                                   │
│             │ (mounted volume)               │ + mTLS client cert                                    │
└─────────────┼───────────────────────────────┼───────────────────────────────────────────────────────┘
              │ REST API (HTTPS)               │ Capability routing:
              │                               │   1. Agent asks portal for tool server URL
              │                               │   2. Portal returns URL + service account ref
              │                               │   3. Agent calls tool server directly (mTLS)
              ▼                               ▼
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  tool01.montanifarms.com  (Windows Server 2022, domain-joined)                       │
│                                                                                      │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │  Praxova Tool Server  (.NET 8 Windows Service)                               │   │
│  │  Port 8443 — HTTPS with internal CA cert                                     │   │
│  │  mTLS: validates incoming agent client certificate                           │   │
│  └──────────────────────────────────┬───────────────────────────────────────────┘   │
│                                     │ LDAPS port 636 (Kerberos in future)            │
│                                     ▼                                                │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │  dc01.montanifarms.com  (Windows Server 2022 Domain Controller)              │   │
│  │  Domain: montanifarms.com  |  AD DS, DNS                                     │   │
│  └──────────────────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

**Topology notes:**
- Containers reference each other by container name (`admin-portal`, `agent`, `ollama`)
- Tool server hostname and port are configured in the admin portal, not hardcoded
- The tool server is a Windows Service on a domain-joined VM, not containerized
- The agent never communicates with Active Directory directly

---

## Security Architecture

### Certificate Trust Chain (ADR-014)

At install time, Praxova generates a private RSA 4096 root CA. All inter-component
TLS uses certificates issued from this CA. No external CA is required.

```
Praxova Root CA  (RSA 4096, generated at install, private key encrypted at rest)
├── admin-portal.crt    (HTTPS server cert — SAN includes container name and host IP)
├── agent-client.crt    (mTLS client cert — agent presents this to tool server)
├── tool-server.crt     (HTTPS server cert for tool server)
└── ollama.crt          (if Ollama TLS is enabled — currently HTTP within Docker network)
```

The CA certificate is distributed to all components at startup:
- Docker containers: volume-mounted to `/etc/ssl/certs/praxova-ca.pem`, loaded into
  the .NET trust store by a bootstrap script before the main process starts
- Tool server (Windows): installed into `LocalMachine\Root` certificate store manually
  (automated via `scripts/provision-toolserver-certs.ps1`)

Enterprise CA integration (AD CS, Vault PKI, Step CA) is a planned post-launch
feature. See ADR-014 for the full roadmap.

### Secrets Storage (ADR-015)

Credentials — LDAP bind passwords, API keys, ServiceNow passwords — are stored using
envelope encryption. A database dump alone exposes nothing.

```
Master Key (MK)
  Derived via Argon2id from PRAXOVA_MASTER_KEY env var
  Never persisted — held in memory only after unseal
  │
  └─ encrypts ──► Key Encryption Key (KEK)
                  Generated once at install, stored encrypted in DB
                  │
                  └─ encrypts ──► Data Encryption Keys (DEKs, one per secret)
                                  Stored encrypted alongside each secret record
                                  │
                                  └─ encrypts ──► Secret values (AES-256-GCM)
                                                  Stored as (ciphertext, IV, auth tag)
```

The admin portal must **unseal** on startup before any credential operations are
possible. If the portal starts sealed, it serves the UI but rejects all operations
that require secrets access.

`SecretString` is a C# type that wraps sensitive values. Its `ToString()` returns
`[REDACTED]`. Secrets must never appear in logs, stack traces, or audit records.

### Tool Server Authentication and AD Delegation

The full chain for an AD operation (e.g., password reset):

```
Agent                      Admin Portal               Tool Server           Active Directory
  │                             │                          │                      │
  │  1. Capability request:     │                          │                      │
  │     GET /api/capabilities/  │                          │                      │
  │     ad-password-reset/servers                          │                      │
  │────────────────────────────►│                          │                      │
  │                             │                          │                      │
  │  2. Returns tool server URL │                          │                      │
  │     + service account ref   │                          │                      │
  │◄────────────────────────────│                          │                      │
  │                             │                          │                      │
  │  3. POST /api/v1/tools/     │                          │                      │
  │     password/reset          │                          │                      │
  │     [mTLS: agent-client.crt]│                          │                      │
  │──────────────────────────────────────────────────────► │                      │
  │                             │                          │                      │
  │                             │                          │  4. LDAPS bind:      │
  │                             │                          │     svc-praxova@     │
  │                             │                          │     montanifarms.com │
  │                             │                          │─────────────────────►│
  │                             │                          │                      │
  │                             │                          │  5. SetPassword()    │
  │                             │                          │─────────────────────►│
  │                             │                          │                      │
  │  6. Result returned         │                          │                      │
  │◄──────────────────────────────────────────────────────│                      │
```

**Required AD delegation for `svc-praxova`:**

The service account running the tool server requires specific delegated permissions
in Active Directory. These are *not* Domain Admin rights — least privilege applies.

| Operation | Required AD Permission | Scope |
|-----------|----------------------|-------|
| Password reset | `Reset Password` extended right | Target OUs containing managed users |
| Force password change at next logon | `Write pwdLastSet` | Same OUs |
| Add user to group | `Write Member` on group objects | Target groups |
| Remove user from group | `Write Member` on group objects | Target groups |
| Read user attributes | `Read` on user objects | Same OUs |
| Unlock account | `Write lockoutTime` | Same OUs |

These permissions are granted via AD delegation, not by placing `svc-praxova` in
privileged groups. The delegation should be scoped to the specific OUs that contain
the users Praxova manages — not the entire directory.

**LDAPS requirement:** The tool server connects to AD on port 636 (LDAPS) with TLS.
Plain LDAP (port 389) is not acceptable for connections carrying credentials.
The DC's LDAPS certificate must be trusted by the tool server — either by being
issued from the DC's own CA (then install that CA cert into the tool server's trust
store) or by configuring the DC to use a cert from the Praxova internal CA.

---

## Core Concepts

### Service Accounts — Unified External Connection Pattern (ADR-006)

Every external system connection — Active Directory, ServiceNow, LLM providers, CAs —
is represented as a `ServiceAccount` entity in the admin portal. This provides
centralized credential management, a consistent configuration structure, and a
complete audit trail.

| Provider Type | Purpose |
|---------------|---------|
| `windows-ad` | Active Directory — LDAPS bind for AD operations |
| `servicenow-basic` | ServiceNow API with Basic Auth |
| `servicenow-oauth` | ServiceNow API with OAuth |
| `llm-ollama` | Ollama local LLM |
| `llm-openai` | OpenAI API |
| `llm-anthropic` | Anthropic API |
| `llm-azure-openai` | Azure OpenAI |
| `llm-bedrock` | AWS Bedrock |
| `ca-adcs` | AD Certificate Services (post-launch) |
| `ca-vault-pki` | HashiCorp Vault PKI (post-launch) |

### Capability Routing (ADR-007)

Agents request **capabilities** by name — they never reference tool servers directly.
The `CapabilityRouter` in the agent queries the admin portal at runtime to discover
which tool server provides a given capability, then calls that server.

```
Agent requests "ad-password-reset"
        │
        ▼
CapabilityRouter
  GET /api/capabilities/ad-password-reset/servers?status=online
        │
        ▼
Admin Portal returns: [ { url: "https://tool01.montanifarms.com:8443", priority: 1, ... } ]
        │
        ▼
Agent calls tool server directly (mTLS)
  POST https://tool01.montanifarms.com:8443/api/v1/tools/password/reset
```

This decoupling means the tool server URL, port, and even which Windows VM handles
AD operations can all change without touching agent code — only the portal
configuration changes.

### Composable Workflows — Dispatcher Pattern (ADR-011)

Ticket classification runs **once** in a dispatcher workflow, which then routes to
the appropriate specialized sub-workflow. Sub-workflows are independent
`WorkflowDefinition` records that can be developed, tested, and reused separately.

```
Incoming Trigger (ServiceNow ticket, manual, webhook)
        │
        ▼
┌───────────────────────────────────────────────────────────┐
│  DISPATCHER WORKFLOW                                      │
│                                                           │
│  [Trigger] → [Classify] → branches on ticket_type        │
│                   │                                       │
│     ┌─────────────┼──────────────┬────────────────────┐  │
│     ▼             ▼              ▼                    ▼  │
│  [SubWorkflow: [SubWorkflow: [SubWorkflow:  [SubWorkflow: │
│   Password     Group         Software       Escalate]   │
│   Reset]       Access]       Install]                   │
└───────────────────────────────────────────────────────────┘
```

The `SubWorkflowExecutor` fetches the referenced workflow definition from the portal
at runtime and creates a child `WorkflowEngine` instance with a shared
`ExecutionContext`. The parent workflow resumes when the sub-workflow reaches a
terminal step (`End` or `Escalate`).

**Step types:**

| StepType | Description |
|----------|-------------|
| `Trigger` | Entry point — ServiceNow poll, manual, webhook, email |
| `Classify` | LLM classification — runs once in dispatcher |
| `ToolCall` | Calls a capability via CapabilityRouter |
| `Approval` | Human-in-the-loop gate — pauses execution |
| `Notify` | ServiceNow comment, email, Teams message |
| `Condition` | Branching logic on context variables |
| `SetVariable` | Writes a value into ExecutionContext |
| `SubWorkflow` | Invokes another WorkflowDefinition as a child |
| `Escalate` | Terminal — assigns to human, closes automation |
| `End` | Terminal — successful completion |

### Human Approval Gates (ADR-013)

When an `Approval` step is reached, execution **pauses** — the `WorkflowExecution`
is persisted with status `AwaitingApproval` and the agent moves on to other tickets.
A `PendingApproval` record is created and surfaced in the admin portal UI.

A human reviews the proposed action and approves or rejects. The agent's approval
poller detects the decision and resumes the workflow from where it paused. Approval
state survives agent restarts.

```
Workflow reaches Approval step
        │
        ▼
WorkflowExecution.Status = AwaitingApproval  (persisted)
PendingApproval record created
Notification sent to reviewer
        │
        (time passes — minutes, hours, days)
        │
Human approves or rejects in portal
        │
Agent approval poller detects decision
        │
        ├── Approved ──► Resume workflow at next step
        └── Rejected ──► Route to rejection branch (typically Escalate)
```

---

## Ticket Processing Flow (End to End)

```
1. TRIGGER
   ServiceNow connector polls assigned queue every N seconds
   Returns new tickets with number, description, caller, urgency

2. DISPATCH
   WorkflowEngine loads the configured dispatcher WorkflowDefinition
   Creates ExecutionContext with ticket data

3. CLASSIFY  (runs once)
   TicketClassifier sends ticket to LLM (via configured PromptDriver)
   Few-shot prompting with organization-specific training examples
   Returns: { ticket_type, confidence, affected_user, target_resource, ... }

   If confidence < threshold → route directly to Escalate
   If ticket_type == "unknown" → route to Escalate

4. ROUTE
   Dispatcher transitions to the sub-workflow matching ticket_type
   SubWorkflowExecutor fetches child WorkflowDefinition from portal
   Child engine starts with same ExecutionContext (shared variables)

5. VALIDATE  (within sub-workflow)
   ToolCall steps verify pre-conditions:
     - User exists in AD
     - User is not in deny list
     - Request is within policy bounds
   If validation fails → Escalate step

6. APPROVE  (if workflow has an Approval step)
   Execution pauses — see Human Approval Gates above
   Resumes after human decision

7. EXECUTE
   ToolCall step → CapabilityRouter resolves tool server URL
   Agent calls tool server API (mTLS)
   Tool server executes against Active Directory via LDAPS
   Returns success/failure + details

8. COMMUNICATE
   Notify step → adds work notes to ServiceNow ticket
   Includes action taken, temp password (if password reset), next steps

9. CLOSE
   End step → updates ServiceNow ticket to Resolved
   Audit event written to admin portal

10. AUDIT
    All steps logged: agent ID, step type, capability, tool server, result, timestamp
```

---

## Component Details

### Admin Portal (Blazor Server, .NET 8)

The single source of truth for all Praxova configuration. The agent and tool server
are stateless — all persistent configuration lives in the portal.

**Responsibilities:**
- ServiceAccount CRUD and credential storage (encrypted)
- ToolServer registration, health monitoring, heartbeat tracking
- Capability and CapabilityMapping management
- WorkflowDefinition storage and versioning
- Agent registration and configuration serving
- Human approval queue (`PendingApproval` records)
- Audit event log

**Key REST API surface (consumed by agent and tool server):**

```
# Agent startup
GET  /api/agents/{name}/configuration     → full config: LLM, ServiceNow, assignment group

# Capability routing (called at workflow runtime)
GET  /api/capabilities/{name}/servers     → list of healthy tool servers for this capability

# Workflow execution
GET  /api/workflows/{name}/export         → WorkflowExportInfo for agent to execute

# Approval lifecycle
GET  /api/approvals/pending               → approvals awaiting decision (agent polls)
POST /api/approvals/{id}/resume           → agent calls after human decides

# Heartbeat / status
POST /api/agents/{name}/heartbeat
POST /api/tool-servers/{id}/heartbeat
```

**Three-layer .NET architecture:**
```
LucidAdmin.Core           — Domain entities, interfaces, enums (no external dependencies)
LucidAdmin.Infrastructure — EF Core, ISecretsService impl, ICertificateManager impl
LucidAdmin.Web            — Blazor pages, Minimal API endpoints, DI wiring
```

### Python Agent (Griptape 1.9.0)

The agent runtime. Stateless between restarts — all state is in the portal database.

**Startup sequence:**
1. Read `ADMIN_PORTAL_URL` and `AGENT_NAME` from environment
2. `GET /api/agents/{name}/configuration` → build LLM driver, ServiceNow connector
3. Enter main loop: poll → dispatch → classify → route → execute → communicate → close

**Key modules:**
```
agent/src/agent/
├── classifier/         TicketClassifier — LLM prompt + response parsing
├── pipeline/
│   ├── executor.py     TicketExecutor — main loop
│   ├── engine.py       WorkflowEngine — step executor registry, transition logic
│   └── executors/      One class per StepType (ToolCallExecutor, ApprovalExecutor, etc.)
├── tools/              Griptape BaseTool subclasses wrapping tool server HTTP calls
├── routing/            CapabilityRouter — portal query + result caching
└── drivers/            create_prompt_driver() factory — supports all LLM providers
```

**Griptape 1.9.0 import paths (use these, not the old 0.x paths):**
```python
from griptape.drivers.prompt.ollama import OllamaPromptDriver
from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
from griptape.structures import Agent, Pipeline, Workflow
from griptape.tasks import PromptTask, ToolkitTask
from griptape.tools import BaseTool
```

### Tool Server (.NET 8, Windows Service)

Executes AD operations on behalf of the agent. Runs as a Windows Service on a
domain-joined server. Never receives credentials from the agent — it uses its own
service account (`svc-praxova`) which is configured in the portal's ServiceAccount
for the `windows-ad` provider.

**Authentication:**
- Inbound: mTLS — validates the agent's client certificate against the Praxova CA
- Outbound to AD: LDAPS bind using `svc-praxova` credentials (retrieved from portal)

**API surface:**
```
GET  /api/health                         → connectivity check + AD reachability
POST /api/v1/tools/password/reset
POST /api/v1/tools/groups/add-member
POST /api/v1/tools/groups/remove-member
GET  /api/v1/tools/groups/{name}
GET  /api/v1/tools/user/{username}/groups
POST /api/v1/tools/permissions/grant
POST /api/v1/tools/permissions/revoke
GET  /api/v1/tools/permissions/{path}
```

---

## Entity Model

```
ServiceAccount
  id, name, provider_type, provider_config (JSON)
  storage_type, encrypted_credential, credential_iv, credential_tag, dek_id
  credential_expires_at, last_rotated_at
  │
  ├─── used by ──► CapabilityMapping (service_account_id FK)
  ├─── used by ──► Agent.llm_service_account_id
  └─── used by ──► Agent.service_now_account_id

ToolServer
  id, name, url, region, status, last_heartbeat
  │
  └─── used by ──► CapabilityMapping (tool_server_id FK)

Capability
  id, name (e.g. "ad-password-reset"), description
  │
  └─── used by ──► CapabilityMapping (capability_id FK)

CapabilityMapping
  tool_server_id FK
  capability_id FK
  service_account_id FK    ← the windows-ad account used for this capability
  is_enabled, priority

Agent
  id, name, display_name
  llm_service_account_id FK
  service_now_account_id FK
  assignment_group          ← ServiceNow queue to poll
  status, last_heartbeat, tickets_processed

WorkflowDefinition
  id, name, version
  trigger_type              ← "servicenow", "manual", "webhook", "email"
  trigger_config_json
  steps_json                ← full step graph
  is_dispatcher             ← true for the top-level dispatcher workflow

WorkflowExecution
  id, workflow_definition_id FK
  status                    ← Running, AwaitingApproval, Completed, Failed, Escalated
  context_json              ← ExecutionContext (ticket data + variables)
  current_step_id
  started_at, completed_at

PendingApproval
  id, workflow_execution_id FK
  step_id, step_description
  proposed_action           ← human-readable description of what will happen
  status                    ← Pending, Approved, Rejected
  decided_by, decided_at, decision_notes

AuditEvent
  id, timestamp, agent_id, event_type
  workflow_execution_id FK (nullable)
  capability, tool_server_url, result, detail_json
```

---

## Classification Improvement Loop (Key Differentiator)

Praxova is a **supervised learning system**, not an autonomous one. The LLM
classifier is seeded with generic few-shot examples at install time. Over the first
several months of operation, human reviewers correct misclassifications and add
organization-specific examples to the training data. The classifier gets measurably
better for that specific organization's ticket vocabulary and patterns.

This is intentional. An organization's ticket language is unique — their group names,
systems, internal jargon, and common request patterns are not in any public training
set. By month six, the deployed classifier is deeply customized. That customization
is the retention mechanism — not lock-in, but genuine fit.

The classification improvement workflow:
1. Classifier makes a prediction with a confidence score
2. Low-confidence predictions are flagged for human review in the portal
3. Reviewer confirms or corrects the classification
4. Confirmed examples are added to the few-shot training set for that organization
5. The classifier uses the updated examples on subsequent tickets

---

## Extensibility

### Adding a New LLM Provider
1. Add provider type to `ServiceAccount` enum
2. Add case to `create_prompt_driver()` factory in `agent/src/agent/drivers/factory.py`
3. Add Griptape driver extra: `griptape[drivers-prompt-<provider>]`
4. Create ServiceAccount in portal with new provider type

### Adding a New Tool Server Platform
1. Implement the tool server REST API contract (health, capabilities, tool endpoints)
2. Register in portal as a new ToolServer
3. Create ServiceAccount for target system credentials
4. Add CapabilityMappings linking capabilities to the new server

### Adding a New Ticket / Workflow Type
1. Add few-shot classification examples for the new ticket type to the portal
2. Build a new WorkflowDefinition in the visual designer
3. The dispatcher workflow routes to it automatically via the Classify → Condition pattern

### Adding a New Trigger Type
1. Implement a `BaseTrigger` subclass in the agent
2. Add provider type to `ServiceAccount` for any credentials the trigger needs
3. The `Agent` entity's dynamic account configuration picks it up automatically (ADR-011)

---

## References

- `docs/adr/` — Full Architecture Decision Records (ADR-005 through ADR-015)
- `docs/DEV-QUICKREF.md` — Developer quick reference, port map, common commands
- `docs/infra/` — Proxmox lab and Windows infrastructure runbooks
- `CLAUDE.md` — Claude Code instructions, security detail, current issue context
