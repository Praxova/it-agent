# Praxova IT Agent — Roadmap

This document describes what ships in the v1.0 release, what is planned for near-term
releases, and the longer-term vision for the platform.

Status markers: ✅ Shipped · 🔧 In progress · 📋 Planned · 💡 Under consideration

---

## v1.0 — Initial Release (Current)

The v1.0 release establishes the core platform. An organization can deploy Praxova,
connect it to ServiceNow and Active Directory, and begin automating Level 1 IT tickets
with supervised human approval gates.

### Core Platform

- ✅ **Admin Portal** — Blazor Server web UI for all configuration and monitoring
- ✅ **Composable Workflows** — Visual dispatcher pattern with pluggable sub-workflows (ADR-011)
- ✅ **Human Approval Gates** — Pause/resume execution for operator review before sensitive actions (ADR-013)
- ✅ **Audit Log** — Full record of every action the agent takes, queryable from the portal
- ✅ **Internal PKI** — Auto-generated RSA 4096 root CA; all inter-component TLS uses certs
  issued from this CA, with no external certificate authority required (ADR-014 Part A)
- ✅ **Secrets Encryption** — Envelope encryption (AES-256-GCM, Argon2id) for all stored
  credentials; database dump alone exposes nothing (ADR-015 Part A)
- ✅ **Pluggable LLM Providers** — Ollama (local), OpenAI, Anthropic, Azure OpenAI,
  AWS Bedrock — swap without code changes (ADR-006)
- ✅ **Pluggable Triggers** — ServiceNow queue polling and manual trigger for testing (ADR-011)
- ✅ **Capability Routing** — Agent requests capabilities by name; portal resolves to tool
  server at runtime, decoupling agent logic from infrastructure (ADR-007)
- ✅ **Agent API Keys** — Token-based authentication for all agent↔portal communication

### Automated Actions

- ✅ **Password Reset** — Reset AD user passwords via delegated service account (LDAPS)
- ✅ **Group Membership** — Add/remove users from AD security groups
- ✅ **Account Unlock** — Unlock locked-out AD accounts
- ✅ **NTFS Permissions** — Grant and revoke file share access
- ✅ **Software Install** — Approve and trigger approved-software deployments
  (catalog managed in portal; execution via tool server)

### Classification & Learning

- ✅ **LLM-powered classification** — Ticket type detection with confidence scoring
- ✅ **Example sets in portal** — Organization-specific few-shot training examples
  stored and managed in the admin portal
- ✅ **Confidence thresholds** — Low-confidence tickets route to human review automatically
- ✅ **Escalation path** — Any ticket outside capability or policy routes cleanly to
  a human with full context attached

### Infrastructure

- ✅ **Docker Compose deployment** — Single-command stack startup
- ✅ **Windows Service tool server** — .NET 8, domain-joined, communicates via mTLS
- ✅ **Proxmox/Packer/OpenTofu** — Infrastructure-as-code for lab environment
  provisioning and teardown

---

## v1.1 — Hardening & Polish

Addresses known gaps that block production confidence. No new capabilities — making
what exists more reliable and enterprise-ready.

### Security & Auth

- 🔧 **Portal authentication hardening** (TD-007 — MVP blocker)
  - Force password change on first login
  - Argon2id password hashing for the local break-glass account
  - Password policy enforcement
- 📋 **Active Directory authentication for portal login**
  - LDAP bind authentication against the domain
  - AD group → portal role mapping (`LucidAdmin-Admins`, `LucidAdmin-Operators`, `LucidAdmin-Viewers`)
  - Graceful fallback to local account when AD is unreachable
- 📋 **Fix portal HTTP→HTTPS redirect on `/api/pki/trust-bundle`**
  - One-line middleware fix; removes the shared-volume workaround from docker-compose.yml

### Reliability

- 📋 **Failed workflows route to escalation** (TD-004)
  - Add `outcome == 'failed'` dispatcher transitions so failures never silently resolve tickets
  - Make "no matching transition" log as a warning distinct from intentional completion
- 📋 **Dynamic classification from portal example sets** (TD-001)
  - Wire classifier to pull categories and examples from the portal at runtime
  - Remove hardcoded prompt categories and `_TYPE_MAP` normalization
  - Enables new ticket types to be added entirely via the portal with no code changes

### Infrastructure

- 📋 **Ollama TLS** (TD-008)
  - Add Nginx/Caddy TLS-terminating sidecar for Ollama, or switch to llama.cpp server
    which supports `--ssl-cert-file` natively
  - All inter-service traffic encrypted end-to-end
- 📋 **Clean up `.env.example`**
  - Remove stale Python tool server variables
  - Document only current variables with accurate descriptions
- 📋 **Installer verification** — validate `provision-toolserver-certs.ps1` and
  tool server installer against current build artifacts

---

## v1.2 — Connector Expansion

Bring Praxova to organizations not using ServiceNow.

- 📋 **Jira Service Management connector** (ADR-003)
- 📋 **Email-based trigger** — parse inbound IT request emails as ticket sources
- 📋 **Microsoft Teams integration** — post approval requests and resolution notifications
  to Teams channels
- 📋 **Zendesk connector**

---

## v2.0 — Enterprise Platform

Capabilities required for regulated or high-security enterprise deployments.

### Security

- 📋 **Enterprise CA integration** (ADR-014 Part B)
  - AD Certificate Services (AD CS) as the issuing CA
  - HashiCorp Vault PKI engine
  - Step CA
  - Organizations bring their own CA; Praxova enrolls from it automatically
- 📋 **External secrets vault** (ADR-015 Part B)
  - HashiCorp Vault backend for credential storage
  - Azure Key Vault backend
  - AWS Secrets Manager backend
- 📋 **gMSA support for tool server**
  - Replace LDAP bind with Group Managed Service Account
  - No stored AD password; Windows handles credential rotation automatically
- 📋 **Kerberos authentication for AD operations**
  - Replace LDAP simple bind with Kerberos (stronger auth, required in some environments)

### Scale & Reliability

- 📋 **Multi-agent deployments** — multiple agents with different assignment groups,
  all managed from one portal instance
- 📋 **Agent resilience** — local configuration cache so the agent processes tickets
  if the portal is temporarily unavailable (TD-006)
- 📋 **Multi-task ticket decomposition** (TD-005) — detect and handle tickets that
  contain multiple distinct requests (e.g., "reset password AND add to VPN group")
- 📋 **Dynamic capability endpoint discovery** (TD-003 remaining) — tool servers
  advertise their capabilities and endpoints at registration time; no hardcoded maps

### Operations

- 📋 **Role-based access control in portal** — Viewer role sees audit log only;
  Operator role manages approvals; Admin role manages all configuration
- 📋 **Compliance exports** — export audit log in formats suitable for SOC 2 / ISO 27001
  evidence packages
- 📋 **Health dashboard** — portal home page with agent status, ticket throughput,
  classification accuracy trend, and approval queue depth

---

## Future Vision

Longer-term ideas under consideration. No commitment on timing.

### Classification Improvement Loop at Scale

The core value proposition of Praxova is that it gets measurably better for each
organization over time through human feedback. The v1.0 foundation (example sets in
the portal, confidence-based routing to human review) enables this loop. Future work
will make it explicit and measurable:

- Classification accuracy metrics per ticket type, per time period
- A/B testing of example set variations
- Suggested examples surfaced from recently reviewed tickets
- Bulk example import from historical ticket data

### Additional Tool Server Platforms

- 💡 **Linux tool server** — Samba/LDAP environments, not just Windows AD
- 💡 **SAP tool server** — user provisioning and access changes in SAP landscapes
- 💡 **Cloud identity** — Azure AD / Entra ID and AWS IAM as target systems

### Workflow Intelligence

- 💡 **Adaptive confidence thresholds** — thresholds that tighten automatically as
  the classifier accumulates high-confidence correct predictions for a ticket type
- 💡 **Workflow suggestion** — when a ticket type appears repeatedly without a matching
  workflow, the portal suggests creating one
- 💡 **Sub-workflow library** — community-contributed sub-workflows for common
  IT operations, importable from the portal

---

## What Praxova Is Not Building

To set expectations clearly:

**Not an autonomous agent.** Praxova is explicitly designed to require human oversight,
especially during the initial deployment period. The approval gate system is a feature,
not a limitation. Organizations that want fully autonomous resolution can configure
workflows without approval steps — but the system is built to make supervised operation
the easy default.

**Not a general-purpose AI assistant.** Praxova is purpose-built for IT ticket automation
against structured target systems (AD, ServiceNow, file shares). It is not a chat interface,
a knowledge base, or a general helpdesk tool.

**Not a replacement for your ITSM.** Praxova reads from and writes to ServiceNow (or your
chosen ITSM) — it does not replace it. Tickets live in your ITSM; Praxova automates the
resolution.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to contribute to Praxova.  
Bug reports and feature requests: [GitHub Issues](https://github.com/your-org/praxova-it-agent/issues)

---

*Last updated: 2026-02-25*
