# ADR-014: Certificate Management Agent & Internal PKI

**Status**: Accepted — Part A (Internal PKI) Implemented; Parts B and C post-launch  
**Date**: 2026-02-15  
**Decision Makers**: Alton  
**Related**: ADR-006 (Admin Portal), ADR-011 (Composable Workflows), ADR-008 (Agent Configuration)

---

## 1. Context & Motivation

### The Universal Pain Point

Certificate management is one of the most universally painful operational burdens in
enterprise IT. The technical act of creating a certificate is straightforward. The
operational reality of tracking hundreds of certificates across dozens of services,
renewing them before they expire, distributing trust anchors to every container and
VM, and debugging "it just stopped working" when something in the chain changes —
that's where organizations drown.

Every enterprise has been burned by certificate expiration. Massive outages at
Microsoft, Equifax, Spotify, and Teams were all caused by forgotten certificate
renewals. The typical "solution" is a spreadsheet someone maintains, or an expensive
purpose-built tool like Venafi or Keyfactor that costs six figures and takes months
to deploy.

There is a massive gap between "spreadsheet" and "Venafi" that nobody fills well.

### Why This Matters for Praxova

Praxova already sits at the intersection of multiple services that need to trust each
other: the admin portal, the Python agent, the tool server, Ollama, the domain
controller — five components with mutual trust requirements right out of the box. If
the installer handles all of that seamlessly, we've immediately solved something that
takes most customers days of painful troubleshooting.

More importantly, we already have an agent architecture that monitors queues,
classifies issues, and takes autonomous action. Certificate management is the exact
same pattern applied to a different domain. Instead of monitoring ServiceNow for
"I forgot my password," we monitor certificate stores for "this cert expires in
30 days" and take action.

### The Opportunity

The cert agent sits in the gap between spreadsheets and Venafi. It's not a standalone
PKI product competing with the big players. It's an operational agent that handles the
boring, error-prone work — the same value proposition as the IT helpdesk agent, just
applied to infrastructure instead of end users.

This transforms certificate management from Praxova's own operational burden into a
revenue-generating product capability and a key differentiator.

---

## 2. Decision

### Part A: Praxova Internal PKI (Level 1 — Launch Requirement)

Praxova will generate and manage its own internal Certificate Authority at install
time, issuing certificates to all Praxova components automatically. Customers using
the "Automatic" certificate mode will never touch a certificate for inter-component
communication.

### Part B: Enterprise CA Integration (Level 2 — Post-Launch)

Praxova will integrate with existing enterprise CAs (AD CS, HashiCorp Vault PKI,
EJBCA, Step CA) to request certificates from infrastructure the customer already
trusts, rather than requiring them to trust a new CA.

### Part C: Certificate Management Agent (Level 3 — Product Expansion)

A new Praxova agent type — the Certificate Agent — will monitor, manage, renew, and
remediate certificates across the customer's environment using the same agent
architecture as the IT helpdesk agent.

---

## 3. Part A: Praxova Internal PKI

### Install-Time Certificate Generation

At installation, the setup process will:

1. Generate an RSA 4096-bit (or P-384 ECDSA) root CA keypair
2. Store the CA private key encrypted at rest (AES-256, key derived from install-time passphrase or HSM)
3. Issue component certificates with short lifetimes (90 days) to:
   - Admin Portal (HTTPS + API TLS)
   - Python Agent (mTLS client cert for API auth)
   - Tool Server(s) (HTTPS + mTLS client cert)
   - Ollama reverse proxy (if applicable)
4. Generate a trust bundle containing the CA public certificate
5. Distribute component certs and trust bundle via Docker Compose volumes/secrets
6. Configure all components to use their issued certs automatically

### Certificate Lifecycle

A `CertificateManager` background service in the admin portal will:

- Track all issued certificates with expiration dates
- Auto-renew component certs 30 days before expiry
- Provide a trust bundle endpoint (`GET /api/pki/trust-bundle`) that containers fetch at startup
- Rotate the CA key on a configurable schedule (default: 5 years)
- Log all issuance, renewal, and revocation events to the audit trail

### Setup Wizard UX

```
Certificate Configuration
─────────────────────────
○ Automatic (recommended)
  Praxova generates and manages all certificates internally.
  No configuration needed. Certificates auto-renew.

○ Enterprise CA Integration  
  Connect to your existing certificate authority.
  [AD CS]  [HashiCorp Vault]  [EJBCA]  [Step CA]

○ Bring Your Own
  Provide your own certificates for each component.
  [Upload portal cert]  [Upload agent cert]  ...
```

The "Automatic" path is the default. Zero configuration. It just works.

### External Certificate Trust (e.g., LDAPS)

For connections to external systems like Active Directory over LDAPS, the admin portal
will provide a streamlined trust workflow:

1. Connect to the DC on port 389 first to validate credentials
2. Automatically test port 636
3. If 636 fails, diagnose the cause (no cert, expired, untrusted) in plain English
4. Offer one-click import of the DC's certificate into Praxova's trust store
5. Show a warning if running on plain LDAP with a "Fix this" button

This replaces the current manual process of copying certificates into Docker builds
and rebuilding containers.

---

## 4. Part B: Enterprise CA Integration

### Supported CA Backends

| CA Type | Protocol | Use Case |
|---------|----------|----------|
| AD Certificate Services | DCOM/RPC, CEP/CES | Most Windows enterprise environments |
| HashiCorp Vault PKI | REST API | Modern infrastructure teams, cloud-native |
| EJBCA | REST API | Open-source enterprise PKI |
| Step CA (smallstep) | ACME | DevOps-oriented, lightweight |
| ACME (Let's Encrypt) | ACME | Public-facing endpoints |

### Integration Pattern

When enterprise CA integration is selected:

1. Admin configures CA connection in the portal (endpoint, credentials, template/policy)
2. `CertificateManager` requests certs from the enterprise CA instead of self-issuing
3. Trust is automatic because the customer already trusts their own CA
4. Renewal follows the same automated lifecycle, just against the external CA
5. Fallback: if the enterprise CA is unreachable, alert but don't break — use cached certs until resolved

### Service Account Pattern

CA connections use the existing `ServiceAccount` entity (ADR-006):

| Provider Type | Purpose | Configuration |
|---------------|---------|---------------|
| `ca-adcs` | AD Certificate Services | CA hostname, template name |
| `ca-vault-pki` | Vault PKI secrets engine | Vault URL, PKI mount path, role |
| `ca-ejbca` | EJBCA REST API | Endpoint URL, profile name |
| `ca-step` | Step CA / ACME | ACME directory URL |
| `ca-acme` | Let's Encrypt / other ACME | ACME directory URL, contact email |

---

## 5. Part C: Certificate Management Agent

### Agent Model: Monitor → Classify → Act → Report

The certificate agent follows the same operational pattern as the IT helpdesk agent:

| Phase | IT Helpdesk Agent | Certificate Agent |
|-------|-------------------|-------------------|
| **Monitor** | Poll ServiceNow queue | Scan cert stores, probe endpoints, query CAs |
| **Classify** | Ticket type + confidence | Cert state: expiring, expired, weak, revoked, untrusted |
| **Act** | Reset password, modify group | Renew cert, deploy trust, revoke, open ticket |
| **Report** | Update ticket, audit log | Dashboard, audit log, alerts |

### What It Monitors

**Certificate Discovery**:
- Connect to endpoints on known ports (443, 636, 8443, 3389, etc.) and read presented certs
- Query AD CS for all issued certificates
- Scan Windows certificate stores via the Windows tool server
- Scan Linux certificate directories (`/etc/ssl/certs`, `/etc/pki/tls`, app-specific paths) via Linux tool server
- (Future) Check Kubernetes secrets, load balancers, reverse proxies

**Health Assessment**:
- Days until expiration (configurable thresholds per cert or per policy)
- Chain completeness (leaf → intermediate → root)
- Revocation status via CRL and OCSP
- Weak keys, deprecated algorithms (SHA-1, RSA-1024)
- Overly broad wildcards
- Orphaned certs (installed but unused)
- Shadow certs (actively serving but unknown to inventory)

### What It Does About It

**Auto-Renewal** (CA-issued certs the agent can reach):
- Request new certificate from the issuing CA
- Install the new certificate on the target system
- Validate the service still works with the new cert
- Roll back if validation fails
- Log the entire operation to audit trail

**Ticket-Driven Renewal** (commercial certs, third-party services):
- Open a ServiceNow ticket (same connector as the helpdesk agent) with full details
- Generate the CSR and attach it to the ticket
- Track the ticket to completion
- When the new cert arrives, deploy it automatically

**Trust Distribution**:
- When a CA cert changes or a new root needs to be trusted, push the trust bundle to all managed endpoints via tool servers
- Eliminate "we renewed the cert but forgot to update the trust store on three servers"

**Revocation & Blacklisting**:
- On compromise: revoke the cert, request a replacement, deploy it, update CRLs
- All in one automated workflow — the kind of coordinated response that takes a human team hours under pressure

### New Tool Server Capabilities

These extend the existing Windows and Linux tool servers:

| Capability | Platform | Description |
|------------|----------|-------------|
| `cert-store-scan` | Windows | Enumerate certs in LocalMachine and CurrentUser stores |
| `cert-store-scan` | Linux | Scan /etc/ssl, /etc/pki, and configured app paths |
| `cert-install` | Windows | Import cert into specified store |
| `cert-install` | Linux | Deploy cert file, update ca-certificates, restart services |
| `cert-endpoint-probe` | Both | TLS connect to host:port, return presented cert chain |
| `ca-request-cert` | Windows | Request cert from AD CS via template |
| `ca-request-cert` | Linux | Request cert via ACME or Vault API |
| `cert-revoke` | Both | Revoke cert via CA API |
| `cert-validate` | Both | Verify cert chain, check expiry, test service post-install |

### Architecture Within Praxova

```
Admin Portal
├── IT Helpdesk Agent (existing)
│   ├── Monitors: ServiceNow queue
│   ├── Capabilities: ad-password-reset, ad-group-add, ...
│   └── Tool Servers: Windows, Linux
│
├── Certificate Agent (new)
│   ├── Monitors: Cert stores, endpoints, CA databases
│   ├── Capabilities: cert-renew, cert-deploy, cert-revoke,
│   │                  trust-distribute, cert-discover
│   └── Tool Servers: Windows (certstore, AD CS), Linux (openssl, certbot)
│
└── (future agents: Compliance Agent, Onboarding Agent, etc.)
```

The cert agent is just another agent registered in the admin portal. It uses the same
ServiceAccount pattern for credentials, the same capability routing to reach tool
servers, the same audit trail for compliance. The composable workflow architecture
from ADR-011 applies directly — cert discovery, health check, renewal, and deployment
are all composable workflow steps.

---

## 6. The Supervised Learning Angle

The "classification improvement loop" that differentiates Praxova for helpdesk
automation applies perfectly to certificate management:

### Month 1: Discovery & Inventory
The agent scans the environment and builds a certificate inventory. Nobody even knew
about half these certs. That alone is worth the price of admission. The dashboard
shows every cert, its health, its issuer, and what services use it.

### Month 2: Recommendations
The agent starts recommending actions with human approval gates (ADR-013):
"This cert expires in 60 days, I can auto-renew it from AD CS. Approve?"
Humans approve or reject, and the agent learns the patterns.

### Month 3+: Graduated Autonomy
Approved patterns become automatic. The agent handles routine renewals silently and
only escalates unusual situations. The organization defines policies: "auto-renew
anything from our internal CA," "always get approval for public-facing certs,"
"alert immediately if any cert uses SHA-1."

### Month 6: Fully Customized
The agent is deeply customized to the organization's certificate landscape, renewal
policies, approval workflows, and risk tolerances. Migrating away means going back
to spreadsheets — not because of lock-in, but because the agent genuinely handles
something no one wants to do manually.

---

## 7. Admin Portal Dashboard

### Certificate Inventory View

A dedicated certificate management page in the admin portal showing:

- **Inventory table**: Every discovered cert, color-coded by health
  - 🟢 Green: Healthy, >60 days to expiry
  - 🟡 Yellow: Expiring within threshold
  - 🔴 Red: Expired, compromised, or revoked
- **Timeline view**: Upcoming expirations as a calendar/Gantt chart — see the "wall of renewals" coming
- **Certificate detail**: Full chain visualization, which services use it, rotation history, agent's planned action
- **Agent activity**: "Auto-renew scheduled for March 1" or "Ticket INC0012345 opened for manual renewal"
- **Statistics**: Total certs managed, auto-renewals this month, escalations, mean time between renewals

### Alerting

- Configurable notification channels (email, Teams, Slack, ServiceNow)
- Alert on: approaching expiry, failed renewal, new shadow cert discovered, weak algorithm detected
- Digest mode: daily summary of cert health across the environment

---

## 8. Business Model Impact

### Revenue Streams

The certificate agent can be monetized in several ways:

1. **Bundled**: Include in a "Praxova Platform" license alongside the helpdesk agent
   - Increases the per-agent annual contract value
   - Makes the platform stickier (two agents = twice the operational dependency)

2. **Standalone**: Separate annual subscription ($15-20k/year per environment)
   - Sells independently to organizations that don't need helpdesk automation yet
   - Entry point that leads to helpdesk agent upsell

3. **Implementation Services**: The $5k setup engagement gets larger
   - Certificate discovery and inventory baseline
   - Policy definition (what auto-renews, what needs approval)
   - CA integration setup
   - Trust chain validation and remediation

### Competitive Positioning

| Segment | Solution | Cost | Praxova Advantage |
|---------|----------|------|-------------------|
| Small/Medium | Spreadsheets, manual | Free + labor | Automation, zero missed renewals |
| Mid-Market | Cert monitoring tools | $10-50k/yr | Agent acts, not just alerts |
| Enterprise | Venafi, Keyfactor | $100k+/yr | Fraction of cost, faster deployment |

The key differentiator is **agent-based remediation**. Most tools in this space are
monitoring tools — they tell you something is wrong. Praxova's cert agent fixes it.
That's the same differentiator that makes the helpdesk agent compelling: not "here's
a classified ticket" but "here's a resolved ticket."

### Customer Retention

Once the cert agent is managing an organization's entire certificate lifecycle,
migrating away means returning to manual operations. This is a powerful retention
mechanism that is genuinely beneficial to the customer — they stay because the
system is actually better, not because of artificial lock-in.

Annual support renewal justification: "Praxova renewed 47 certificates this year
without any downtime, discovered 12 shadow certificates, and prevented 3 potential
outages from expired certificates."

---

## 9. Implementation Roadmap

### Phase 0: Internal PKI (Current Sprint — Demo Prep)
- Solve LDAPS trust for the admin portal's own AD connection
- Implement streamlined cert import in the AD settings UI
- Document the pattern for container-to-DC trust

### Phase 1: Praxova Component PKI (Post-Launch)
- `CertificateManager` service in admin portal
- Install-time CA generation and component cert issuance
- Auto-renewal background service
- Trust bundle endpoint for containers
- Setup wizard with Automatic/Enterprise/BYO options

### Phase 2: Enterprise CA Integration
- AD CS connector (most common enterprise CA)
- HashiCorp Vault PKI connector
- ACME connector (Let's Encrypt, Step CA)
- ServiceAccount provider types for CA credentials

### Phase 3: Certificate Discovery Agent
- Endpoint probing (TLS connect and read certs)
- Certificate store scanning via tool servers
- Inventory database and dashboard
- Health assessment and alerting

### Phase 4: Certificate Remediation Agent
- Automated renewal workflows (composable, per ADR-011)
- Trust distribution workflows
- ServiceNow ticket integration for manual-renewal certs
- Revocation and emergency response workflows

### Phase 5: Supervised Learning & Policy Engine
- Human-in-the-loop approval for cert operations (ADR-013)
- Policy rules: auto-renew vs. approve vs. alert-only
- Classification improvement loop for cert health assessment
- Graduated autonomy based on approval history

---

## 10. Security Considerations

### CA Private Key Protection
- Encrypted at rest with AES-256
- Access restricted to the CertificateManager service
- (Future) HSM support for production deployments
- Key ceremony documentation for enterprise compliance

### Least Privilege
- Cert agent only gets capabilities it needs (cert-install, not ad-password-reset)
- Tool server capabilities are independently scoped
- CA credentials stored as ServiceAccounts with audit trail

### Audit Trail
- Every certificate issuance, renewal, revocation, and deployment logged
- Who/what triggered the action (agent auto-renewal vs. human approval)
- Before/after state (old cert thumbprint → new cert thumbprint)
- Compliance-ready reporting for SOC 2, ISO 27001, PCI DSS

### Defense in Depth
- Short-lived component certs (90 days) limit blast radius of compromise
- mTLS between components prevents unauthorized API access
- Certificate pinning optional for highest-security deployments
- Automatic detection and alerting for unauthorized certificate changes

---

## 11. References

- **Venafi**: Market leader in machine identity management — $100k+/yr, complex deployment
- **Keyfactor**: Enterprise certificate lifecycle management — similar tier
- **cert-manager**: Kubernetes-native cert management — good model for auto-renewal patterns
- **smallstep/step-ca**: Lightweight open-source CA — potential integration target
- **ACME Protocol (RFC 8555)**: Automated certificate issuance standard
- **Equifax Breach (2017)**: Expired cert disabled breach detection for 19 months
- **Microsoft Teams Outage (2020)**: Expired authentication cert caused global outage
- **Spotify Outage (2020)**: Expired TLS cert caused 1-hour global outage

---

## 12. Open Questions

1. Should the Praxova internal CA be a root CA or an intermediate under the customer's existing root?
2. What's the right default cert lifetime for Praxova components — 90 days? 1 year?
3. Should the cert agent be a separate process or a mode of the existing Python agent?
4. How do we handle cert agent access to Windows cert stores — extend existing tool server or separate?
5. What's the minimum viable cert dashboard for the admin portal?
6. Should cert discovery be passive (scan on schedule) or active (continuous endpoint monitoring)?
