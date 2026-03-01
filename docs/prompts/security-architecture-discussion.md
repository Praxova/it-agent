# Security Architecture Discussion — Praxova IT Agent

You are being asked to serve as a senior security architect. Your role in this conversation
is to help reason through the **to-be security architecture** for an enterprise software
product called **Praxova IT Agent**, identify gaps between the current state and a
defensible production posture, and recommend a prioritized roadmap to close them.

Take your time. Think carefully. Ask clarifying questions before making recommendations
where the right answer depends on context you don't yet have.

---

## 1. What Praxova Is

Praxova IT Agent is an **enterprise IT helpdesk automation platform**. It monitors
ServiceNow ticket queues, classifies tickets using a local LLM, routes them through
configurable approval-gated workflows, and executes resolutions against Active Directory
and file systems. It is open source (Apache 2.0) with a services/support revenue model
targeting mid-market enterprises.

**Why security matters more here than average:**
Praxova operates with elevated delegated privileges in Active Directory — it resets
passwords, modifies group memberships, and changes file permissions. The credentials and
certificates that grant this access are high-value targets. A compromise of Praxova's
trust infrastructure gives an attacker the same reach as Praxova itself.

---

## 2. Deployment Topology

```
┌──────────────────────── Docker Host (Ubuntu Linux, runs in Proxmox VM) ──────────────────────┐
│                                                                                               │
│  ┌─────────────────────┐   HTTPS/TLS     ┌─────────────────────┐   HTTP (unencrypted)        │
│  │  Admin Portal        │◄───────────────►│  Agent              │◄──────────────────────────►│
│  │  Blazor/.NET 8       │                 │  Python / Griptape  │                            │
│  │  Port 5000 (HTTP)    │                 │  Polls ServiceNow   │  ┌─────────────────────┐   │
│  │  Port 5001 (HTTPS)   │                 │  Runs workflows     │  │  Ollama (llama3.1)  │   │
│  │  Internal CA cert    │                 │  Has client cert    │  │  Port 11434 (HTTP)  │   │
│  └──────────┬───────────┘                 └──────────┬──────────┘  └─────────────────────┘   │
│             │                                        │                                        │
└─────────────┼────────────────────────────────────────┼────────────────────────────────────────┘
              │ HTTPS REST (portal manages config/     │ HTTPS + mTLS (internal CA certs)
              │ secrets/capabilities)                  │ Agent presents client cert to tool server
              │                                        │
              └──────────────────┬─────────────────────┘
                                 │
              ┌──────────────────▼─────────────────────────────────────────┐
              │  tool01.montanifarms.com — Windows Server 2022, domain-joined │
              │                                                              │
              │  Praxova Tool Server (.NET 8 Windows Service)               │
              │  Port 8443 (HTTPS, internal CA cert)                        │
              │  Validates agent client cert (mTLS)                         │
              │                                                              │
              │  Capabilities: ad-password-reset, ad-group-add/remove,      │
              │                ntfs-permission-grant/revoke                  │
              │                                └──── via LDAPS (port 636) ──►DC│
              └────────────────────────────────────────────────────────────────┘

Domain: montanifarms.com
DC: dc01.montanifarms.com
AD service account: svc-praxova@montanifarms.com (delegated, not Domain Admin)
```

**Key architectural facts:**
- The **Admin Portal is the security authority**: it holds the encryption keys, manages
  certificates, and is the only component that can decrypt secrets. All other components
  receive decrypted secrets over TLS from the portal.
- The **agent never holds AD credentials** — it requests capability endpoints from the portal,
  then calls the tool server directly via mTLS. The tool server holds the AD credentials.
- **Capability routing**: the agent asks the portal "which tool server handles ad-password-reset?"
  and gets back a URL. The agent then calls that tool server directly.

---

## 3. Current Security State (As-Built)

### 3a. Certificate Infrastructure — Partially Implemented

**What works:**
- The admin portal generates a **self-signed RSA 4096 root CA** at first startup
- Issues TLS certificates for portal, agent (client cert), and tool server from this CA
- CA private key is encrypted at rest (AES-256-GCM, Argon2id-derived key)
- mTLS is functional: agent presents its client cert to the tool server; tool server validates it
- A background `CertificateManager` service handles issuance; a `/api/pki/trust-bundle` endpoint
  serves the CA cert for bootstrapping

**Known gap — SSL trust bootstrap workaround (active):**
The agent container is supposed to:
1. Fetch the CA cert over HTTP from `ADMIN_PORTAL_BOOTSTRAP_URL` (plain HTTP, port 5000)
2. Add it to the trust store before starting
3. Then use HTTPS (`ADMIN_PORTAL_URL`, port 5001) for all runtime calls

But the portal's HTTPS redirect middleware intercepts requests to port 5000 and redirects
them to 5001 before the trust bundle endpoint can respond. As a workaround, `docker-compose.yml`
mounts the portal's data volume into the agent container read-only, and the agent reads the
CA cert directly from the shared filesystem. This works but creates infrastructure coupling
that shouldn't exist.

**The fix is one line of C#**: exempt `/api/pki/trust-bundle` from `UseHttpsRedirection()`.
It hasn't been done yet.

**What's planned but not started:**
- Enterprise CA integration (AD CS, HashiCorp Vault PKI, Step CA, ACME)
- Automatic cert renewal (the CA generates certs but doesn't auto-renew them yet)
- A certificate monitoring/management agent (longer term product expansion)

### 3b. Secrets Storage — Partially Implemented

**What works:**
- Envelope encryption is implemented: Master Key → Key Encryption Key (KEK) → per-secret
  Data Encryption Keys (DEKs) → AES-256-GCM encrypted secret blobs
- The portal must "unseal" on startup (Argon2id derives the master key from
  `PRAXOVA_UNSEAL_PASSPHRASE` env var); without unseal, no secrets are accessible
- `SecretString` type overrides `ToString()` → `[REDACTED]` to prevent logging accidents
- The `ISecretsService` interface abstracts storage backends

**Known gaps:**
- External vault backends (HashiCorp Vault, Azure Key Vault, AWS Secrets Manager) are
  **stubbed in the UI** but not implemented in the backend
- Secret rotation is **manual** — no automated rotation, no expiration tracking, no alerts
- The `.env.example` still references old stale variables from an earlier architecture;
  current required variables are minimal (`SERVICENOW_PASSWORD`, `PRAXOVA_UNSEAL_PASSPHRASE`,
  `LUCID_API_KEY`)

### 3c. Admin Portal Authentication — NOT YET IMPLEMENTED (TD-007, High)

**Current state:** The portal ships with hardcoded `admin` / `admin` credentials. There is
no password change mechanism, no hash verification, no policy enforcement.

**Required (blocks production credibility):**
1. **Local break-glass account hardening**: force password change on first login, Argon2id
   hashing, password policy enforcement. This account must always work regardless of AD state —
   it is the recovery path when AD is unreachable.
2. **Active Directory authentication**: LDAP bind auth for operator/admin logins, AD group →
   portal role mapping (`LucidAdmin-Admins` → Admin, `LucidAdmin-Operators` → Operator,
   `LucidAdmin-Viewers` → Viewer). Graceful fallback to local account when AD is unreachable.

**Existing role model:** `UserRole` enum with Admin, Operator, Viewer exists in code but
is not enforced anywhere yet.

### 3d. Ollama — No TLS (TD-008, Medium)

Ollama only serves plain HTTP on port 11434. It is on an isolated Docker bridge network
with no external port exposure, which is the current mitigation. Enterprise security teams
will not accept unencrypted inter-service traffic regardless of network isolation arguments.

Options considered: TLS-terminating Nginx/Caddy sidecar (easiest), switch to llama.cpp
server (native TLS support), or use a cloud LLM provider (OpenAI/Anthropic, already
supported via the pluggable driver factory — zero code changes needed in the agent).

### 3e. Tool Server / AD Operations

The mTLS leg (agent → tool server) is working. The current blocking issue is the tool
server's AD service account (`svc-praxova`) not having the correct delegation rights on
the target OUs for password reset — this is a configuration issue in the lab, not an
architecture issue.

Production deployment requires either:
- **LDAP simple bind** over LDAPS (current approach): credential stored encrypted in portal,
  retrieved by tool server at runtime
- **gMSA (Group Managed Service Account)**: Windows handles credential management; no
  stored password anywhere; requires domain-joined container host or Windows Service (current)
- **Kerberos**: stronger auth, required in some regulated environments

---

## 4. What the ADRs Intend (To-Be)

### ADR-014: Certificate Management (Three-Level Vision)

**Level 1 (launch):** Internal PKI — done.

**Level 2 (post-launch):** Enterprise CA integration. Customers bring their own CA;
Praxova enrolls from it. ServiceAccount entity has provider types for `ca-adcs`,
`ca-vault-pki`, `ca-step`, `ca-acme`. Same ServiceAccount pattern used throughout.

**Level 3 (product expansion):** A dedicated Certificate Agent that monitors certificate
health across the customer environment (endpoint probing, cert store scanning, expiry
tracking), auto-renews where it has CA access, and opens ServiceNow tickets for the rest.

### ADR-015: Secrets Management (Three-Level Vision)

**Level 1 (launch):** Encrypted internal store — largely done, gaps noted above.

**Level 2 (post-launch):** External vault backends fully implemented (Vault, Azure KV,
AWS SM). Vault connections are themselves ServiceAccount entities, stored in the internal
encrypted store (avoiding the bootstrap circular dependency).

**Level 3 (product expansion):** Secrets Management Agent — monitors credential age/rotation
compliance, performs coordinated multi-system rotation with rollback, detects secret sprawl.

### Open Questions from the ADRs (Unresolved)

From ADR-014:
1. Should the Praxova internal CA be a root CA or an intermediate under the customer's
   existing root?
2. What's the right default component cert lifetime — 90 days? 1 year?
3. Should the cert agent be a separate process or a mode of the existing Python agent?
4. Passive scanning (scheduled) vs. active monitoring (continuous) for cert discovery?

From ADR-015:
1. Argon2id vs PBKDF2 for master key derivation — Argon2id is stronger but requires a
   native library; PBKDF2 is built into .NET.
2. Right TTL for cached secrets from external vaults — 5 minutes? 30 seconds? Configurable?
3. Cert agent and secrets agent: separate agents or one combined "Trust Infrastructure Agent"?
4. Bootstrap problem: vault credentials must be stored somewhere before the vault is connected.
5. CyberArk integration — common in banking/healthcare, complex API. Worth it for v2?
6. Migration path for existing installations with improperly stored secrets.
7. SecureString vs. zeroing-on-dispose for the SecretString type in .NET managed memory.

---

## 5. What I Want to Discuss

I want your help thinking through the **complete to-be security architecture** — not just
filling the gaps above, but reasoning about whether the overall approach is sound and what
a defensible, enterprise-grade security posture looks like for this class of product.

Specific areas I want to cover, in whatever order makes sense to you:

**A. The trust bootstrap problem**
The agent needs to trust the portal's CA before it can talk to the portal over HTTPS.
The current workaround (shared Docker volume) is wrong. The one-line fix (exempt the
endpoint from HTTPS redirect) addresses the symptom. But is there a deeper question
here about how components should discover and establish initial trust in a zero-knowledge
deployment? How do other systems solve this?

**B. The seal/unseal model**
We're using a Vault-inspired seal/unseal pattern. The master key is derived from a
passphrase via Argon2id and held in memory. Is this the right model for this class of
product? What are the failure modes? What happens in a container restart at 3am when
no human is available to enter a passphrase? How do we think about auto-unseal safely?

**C. Certificate lifetime strategy**
Short-lived certs (90 days) limit blast radius but require reliable auto-renewal.
Long-lived certs (1 year) are more operationally forgiving but create larger exposure
windows. For the Praxova component certs specifically (portal, agent, tool server),
what's the right strategy given our threat model?

**D. The mTLS architecture**
The agent authenticates to the tool server via a client certificate issued by the Praxova
CA. Is this sufficient? Should the portal be a mediator (agent → portal → tool server)
rather than allowing direct agent → tool server calls? What are the security tradeoffs
between these two topologies?

**E. Secret rotation — the hard part**
Rotating the AD service account password is a multi-system operation. If it goes wrong
mid-rotation, you can lock out the tool server from AD. What does a safe, atomic rotation
workflow look like? What's the right rollback strategy?

**F. The external vault integration design**
When we add HashiCorp Vault / Azure KV / AWS SM as backends: the vault connection
credential itself needs to be stored somewhere. We're planning to store it in the internal
encrypted store (avoiding the circular dependency). Is this the right call? What are the
implications? How do others solve the "who guards the guardians" problem?

**G. Threat model review**
Given the deployment topology and what Praxova is privileged to do, what are the highest
priority threats we should be designing against? What's missing from our current thinking?

**H. Prioritization**
Given v1 is shipping shortly and the team is small: what should be fixed before v1 ships,
what can safely land in v1.1, and what belongs in the v2 enterprise tier? I need a
frank assessment, not a diplomatic one.

---

## 6. Ground Rules for This Discussion

- Be direct. If something in the current design is wrong, say so plainly.
- Flag when you're uncertain or when the right answer depends on context you don't have.
  Ask. Don't assume.
- Avoid generic security advice. Every recommendation should be specific to this
  architecture, this threat model, and this product tier (open source, mid-market
  enterprise, small implementation team).
- When there are multiple valid approaches, explain the tradeoffs rather than picking one
  arbitrarily. I need to make the decisions; you need to make sure I have what I need to
  make them well.
- This is a conversation, not a document delivery. Think out loud. Push back on me.

Where would you like to start?
