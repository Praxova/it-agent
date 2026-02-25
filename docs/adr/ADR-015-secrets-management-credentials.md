# ADR-015: Secrets Management & Credential Security

**Status**: Accepted — Part A (Encrypted Internal Store) Implemented; Parts B and C post-launch  
**Date**: 2026-02-15  
**Decision Makers**: Alton  
**Related**: ADR-006 (ServiceAccount Pattern), ADR-014 (Certificate Management Agent), ADR-008 (Agent Configuration)

---

## 1. Context & Motivation

### The Problem Today

Praxova currently handles secrets through a mix of approaches with varying security
postures:

- **LDAP bind password**: Stored in a `.env` file as plaintext on disk. Anyone with
  file access owns the AD service account.
- **API keys (LLM providers, ServiceNow)**: Stored as Argon2 hashes in the database.
  This is fundamentally wrong — hashing is one-way and irreversible. A hashed API key
  can never be retrieved and sent to OpenAI. Hashing is correct for user login
  passwords (verify-only). Secrets the system must read back require **encryption**
  (reversible), not **hashing** (irreversible).
- **JWT signing key**: In `appsettings.json` as plaintext.
- **Database connection string**: In `appsettings.json` or environment variable.
- **ServiceAccount UI**: Has dropdown options for Vault, Environment, and Database
  credential storage, but the backend implementations are incomplete.

None of these approaches are acceptable for production enterprise deployment. A
penetration tester or security-conscious prospect reviewing the system would
immediately flag these issues.

### Why This Matters

Secrets management is the companion to certificate management (ADR-014). Together
they form a complete "trust infrastructure" story. Certificates prove identity.
Secrets grant access. Both require the same operational discipline: secure storage,
automated rotation, audited access, and minimized blast radius.

More importantly, Praxova is an agent that operates with elevated privileges across
enterprise systems — resetting passwords in Active Directory, modifying group
memberships, executing operations on production servers. The credentials that grant
this access are high-value targets. If Praxova's secrets are compromised, the attacker
inherits every privilege Praxova has.

This is not cosmetic. This is existential to the product's credibility.

### The Enterprise Landscape

Organizations handle secrets across a maturity spectrum:

| Level | Approach | Who Does This |
|-------|----------|---------------|
| 0 — Chaos | Config files, env vars, source code, spreadsheets | Most small orgs |
| 1 — Centralized | One vault/store, manual rotation, coarse access control | Mid-market |
| 2 — Dynamic | Short-lived credentials generated on demand, auto-rotation | Large enterprise |
| 3 — Zero Standing | No permanent secrets, JIT credential generation, auto-revocation | Aspirational |

Most Praxova customers will be at Level 0 or 1. Praxova should operate at Level 2
internally and help customers reach Level 2 over time.

---

## 2. Decision

### Part A: Encrypted Internal Secrets Store (Launch Requirement)

Replace all plaintext and hashed secret storage with a proper encrypted secrets store
using an envelope encryption pattern with a key hierarchy.

### Part B: External Vault Integration (Post-Launch)

Implement real backends for HashiCorp Vault, Azure Key Vault, and AWS Secrets Manager
behind the existing ServiceAccount credential storage abstraction.

### Part C: Secrets Management Agent (Product Expansion)

A Praxova agent capability that monitors, rotates, and manages credentials across
the customer's environment — the operational companion to the Certificate Agent.

---

## 3. Non-Negotiable Security Principles

These apply regardless of implementation phase or vault backend:

### Secrets Are Encrypted at Rest, Always

Whether in the database, a file, or memory-mapped storage — never plaintext, never
"just hashed" if retrieval is needed. Use AES-256-GCM with authenticated encryption.

### Secrets Are Encrypted in Transit, Always

Every connection carrying a secret uses TLS. The LDAP bind password travels over
LDAPS (ADR-014). The agent configuration API uses HTTPS. No exceptions, no "it's on
a private network so it's fine."

### Secrets Are Never Logged

This is the principle that bites everyone. Common violations:
- Debug log prints the full HTTP request including the Authorization header
- Stack trace includes the connection string with embedded password
- Audit log records the credential value instead of a reference

Praxova must scrub secrets from all logging, error reporting, and diagnostic paths.
Implement a `SecretString` type that overrides `ToString()` to return `"[REDACTED]"`
and cannot be accidentally serialized.

### Access to Secrets Is Audited

Every secret retrieval is logged: which secret, which process, which host, what time.
Not just "someone changed the password" but "the agent process on host X retrieved
the LDAP bind credential at timestamp Y via API call Z."

### Secrets Have Defined Lifetimes

Every secret has an expiration, even if it's long (e.g., 1 year for API keys). This
forces rotation, which limits the damage window if something leaks. The admin portal
tracks expiration dates and alerts before they arrive.

### Blast Radius Is Minimized

Each component gets only the secrets it needs. The Python agent doesn't receive the
admin portal's database encryption key. The tool server doesn't receive ServiceNow
credentials. The API endpoint `/api/agents/{name}/configuration` returns only the
secrets that specific agent requires.

---

## 4. Part A: Encrypted Internal Secrets Store

### Key Hierarchy (Envelope Encryption)

```
┌─────────────────────────────────────────────────────────────────┐
│  Master Key (MK)                                                │
│  Derived from: install passphrase (Argon2id) or external KMS    │
│  Purpose: Protects the Key Encryption Key                       │
│  Storage: Never persisted — derived at startup or fetched from  │
│           KMS, held only in memory                              │
└──────────────────────────┬──────────────────────────────────────┘
                           │ encrypts
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Key Encryption Key (KEK)                                       │
│  Generated: Once at install time (AES-256)                      │
│  Purpose: Encrypts individual Data Encryption Keys              │
│  Storage: Encrypted by MK, stored in database                   │
│  Rotation: When master passphrase changes or on schedule        │
└──────────────────────────┬──────────────────────────────────────┘
                           │ encrypts
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Data Encryption Keys (DEK)                                     │
│  Generated: One per secret or per secret category               │
│  Purpose: Encrypts actual secret values                         │
│  Storage: Encrypted by KEK, stored alongside the secret record  │
│  Rotation: When the associated secret is rotated                │
└──────────────────────────┬──────────────────────────────────────┘
                           │ encrypts
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Secret Values                                                  │
│  LDAP bind password, API keys, ServiceNow credentials,          │
│  JWT signing key, etc.                                          │
│  Storage: AES-256-GCM encrypted blob in database                │
│  Each blob includes: ciphertext, IV/nonce, auth tag, DEK ref    │
└─────────────────────────────────────────────────────────────────┘
```

This is the same pattern used by HashiCorp Vault, KeePass, AWS KMS, and most serious
secrets managers. A database dump alone exposes nothing — you need the master key to
begin the decryption chain.

### Seal/Unseal Model

When the admin portal starts, it must "unseal" the secrets store before any secrets
can be read. This requires the master key, obtained via one of:

| Unseal Method | When To Use | Security Level |
|---------------|-------------|----------------|
| Passphrase prompt | Small deployments, manual startup | High — requires human |
| Environment variable | Container orchestration (K8s secrets, Docker secrets) | Medium — secret in orchestrator |
| KMS auto-unseal | Cloud deployments (AWS KMS, Azure KV, GCP KMS) | High — hardware-backed |
| HSM | Regulated industries, highest security | Highest — tamper-resistant |

For development and demo: passphrase via environment variable is acceptable.
For production: KMS auto-unseal or HSM is recommended.

If the admin portal cannot unseal (wrong passphrase, KMS unreachable), it starts in
a degraded mode where it serves the UI but cannot perform any operations requiring
secrets. The dashboard shows a clear "SEALED — Enter master passphrase to unlock"
state.

### ServiceAccount Entity Changes

The `ServiceAccount` entity's credential storage needs to change:

**Current (broken for retrievable secrets)**:
```csharp
// Using Argon2 hash — WRONG for secrets that must be read back
public string? CredentialHash { get; set; }
```

**Proposed**:
```csharp
public class ServiceAccount : BaseEntity
{
    // ... existing fields ...
    
    // Credential storage method
    public CredentialStorageType StorageType { get; set; }
    
    // For StorageType.Internal: AES-256-GCM encrypted blob
    public byte[]? EncryptedCredential { get; set; }
    public byte[]? CredentialIV { get; set; }        // GCM nonce
    public byte[]? CredentialTag { get; set; }       // GCM auth tag
    public Guid? DataEncryptionKeyId { get; set; }   // Which DEK was used
    
    // For StorageType.Vault: reference path
    public string? VaultPath { get; set; }           // e.g., "secret/data/praxova/ldap"
    
    // For StorageType.Environment: variable name
    public string? EnvironmentVariable { get; set; } // e.g., "PRAXOVA_LDAP_PASSWORD"
    
    // For StorageType.AzureKeyVault: reference
    public string? KeyVaultSecretName { get; set; }
    
    // Metadata
    public DateTime? CredentialExpiresAt { get; set; }
    public DateTime? LastRotatedAt { get; set; }
    public string? CredentialFingerprint { get; set; } // SHA-256 of plaintext for change detection
}

public enum CredentialStorageType
{
    Internal,       // Encrypted in Praxova's database (default)
    Vault,          // HashiCorp Vault
    AzureKeyVault,  // Azure Key Vault
    AwsSecrets,     // AWS Secrets Manager
    Environment,    // Environment variable (development/container orchestrator)
    None            // No credential needed (gMSA, managed identity)
}
```

### Secrets Service Interface

```csharp
public interface ISecretsService
{
    /// <summary>
    /// Store a secret, encrypted, associated with a ServiceAccount.
    /// </summary>
    Task StoreSecretAsync(Guid serviceAccountId, string plaintext, CancellationToken ct = default);
    
    /// <summary>
    /// Retrieve a decrypted secret for a ServiceAccount.
    /// Resolves the storage backend (internal, vault, env, etc.) transparently.
    /// </summary>
    Task<string> RetrieveSecretAsync(Guid serviceAccountId, CancellationToken ct = default);
    
    /// <summary>
    /// Rotate a secret: generate new value, store it, return the new value.
    /// For external systems, the caller is responsible for updating the external system.
    /// </summary>
    Task<string> RotateSecretAsync(Guid serviceAccountId, string newPlaintext, CancellationToken ct = default);
    
    /// <summary>
    /// Delete a secret and its encryption key material.
    /// </summary>
    Task DeleteSecretAsync(Guid serviceAccountId, CancellationToken ct = default);
    
    /// <summary>
    /// Check if the secrets store is unsealed and operational.
    /// </summary>
    bool IsUnsealed { get; }
    
    /// <summary>
    /// Unseal the secrets store with the master passphrase.
    /// </summary>
    Task<bool> UnsealAsync(string passphrase);
}
```

The critical design point: **the agent code and tool server code never know which
backend holds the secret.** They call the admin portal API, which calls
`ISecretsService.RetrieveSecretAsync()`, which resolves the storage type and returns
the plaintext over TLS. The backend is an infrastructure concern, not an application
concern.

### SecretString Type (Logging Protection)

```csharp
/// <summary>
/// A string wrapper that prevents accidental logging or serialization of secret values.
/// ToString() returns "[REDACTED]". JSON serialization is blocked.
/// The actual value is only accessible via explicit .Reveal() call.
/// </summary>
public sealed class SecretString : IDisposable
{
    private string? _value;
    
    public SecretString(string value) => _value = value;
    
    /// <summary>
    /// Explicitly retrieve the secret value. Use sparingly and never log the result.
    /// </summary>
    public string Reveal() => _value ?? throw new ObjectDisposedException(nameof(SecretString));
    
    public override string ToString() => "[REDACTED]";
    
    // Prevent JSON serialization from leaking the value
    // System.Text.Json and Newtonsoft.Json custom converters that write "[REDACTED]"
    
    public void Dispose()
    {
        // Zero out the string in memory (best-effort in managed runtime)
        if (_value != null)
        {
            unsafe
            {
                fixed (char* p = _value)
                {
                    for (int i = 0; i < _value.Length; i++)
                        p[i] = '\0';
                }
            }
            _value = null;
        }
    }
}
```

This ensures that even if a developer accidentally writes
`_logger.LogDebug("Credential: {Cred}", credential)`, the log contains
`Credential: [REDACTED]` instead of the actual secret.

---

## 5. Part B: External Vault Integration

### HashiCorp Vault

The most common enterprise vault. Integration details:

**Authentication Methods** (how Praxova authenticates to Vault):

| Method | When To Use |
|--------|-------------|
| AppRole | Most common — Praxova gets a role_id + secret_id at install |
| Token | Simple but requires manual token renewal |
| Kubernetes | When Praxova runs in K8s — uses pod service account |
| TLS Certificate | mTLS — pairs well with ADR-014 cert management |

**Secret Resolution Flow**:
```
ServiceAccount.StorageType = Vault
ServiceAccount.VaultPath = "secret/data/praxova/ldap-bind"
    │
    ▼
ISecretsService.RetrieveSecretAsync()
    │
    ├── Authenticate to Vault (AppRole)
    ├── GET /v1/secret/data/praxova/ldap-bind
    ├── Extract .data.data.password from response
    ├── Cache in memory with TTL (configurable, default 5 min)
    └── Return plaintext
```

**Vault Policy** (least privilege):
```hcl
# Praxova admin portal policy
path "secret/data/praxova/*" {
  capabilities = ["create", "read", "update", "delete"]
}

# Praxova agent policy (read-only)
path "secret/data/praxova/agent/*" {
  capabilities = ["read"]
}
```

### Azure Key Vault

**Authentication**: Managed Identity (preferred) or Service Principal.

**Secret Resolution**:
```
ServiceAccount.StorageType = AzureKeyVault
ServiceAccount.KeyVaultSecretName = "praxova-ldap-bind-password"
    │
    ▼
ISecretsService.RetrieveSecretAsync()
    │
    ├── Authenticate via DefaultAzureCredential
    ├── SecretClient.GetSecretAsync("praxova-ldap-bind-password")
    └── Return .Value.Value
```

### AWS Secrets Manager

**Authentication**: IAM Role (preferred) or Access Key.

**Secret Resolution**:
```
ServiceAccount.StorageType = AwsSecrets
ServiceAccount.AwsSecretArn = "arn:aws:secretsmanager:us-east-1:123456:secret:praxova/ldap"
    │
    ▼
ISecretsService.RetrieveSecretAsync()
    │
    ├── Authenticate via default credential chain
    ├── GetSecretValueAsync(SecretId)
    └── Return .SecretString
```

### Unified Provider Configuration in Admin Portal

The ServiceAccount management UI adapts based on the selected storage type:

```
Credential Storage
──────────────────
○ Praxova Managed (recommended)
  [Enter credential: ••••••••••]
  Encrypted and stored in Praxova's internal secrets store.

○ HashiCorp Vault
  Vault URL:     [https://vault.example.com:8200]
  Secret Path:   [secret/data/praxova/ldap-bind]
  Auth Method:   [AppRole ▾]
  Role ID:       [xxxxxxxx-xxxx-xxxx-xxxx]
  
○ Azure Key Vault
  Vault Name:    [praxova-keyvault]
  Secret Name:   [ldap-bind-password]
  Auth:          [Managed Identity ▾]

○ AWS Secrets Manager
  Secret ARN:    [arn:aws:secretsmanager:...]
  Region:        [us-east-1]

○ Environment Variable
  Variable Name: [PRAXOVA_LDAP_PASSWORD]
  ⚠ Not recommended for production
```

### ServiceAccount Pattern for Vault Connections

The vault connections themselves use the ServiceAccount entity (consistent with
ADR-006). This means the Vault authentication credential is stored in Praxova's
internal encrypted store — you don't need a vault to access the vault.

| Provider Type | Purpose | Configuration |
|---------------|---------|---------------|
| `vault-hashicorp` | HashiCorp Vault connection | URL, auth method, role |
| `vault-azure-kv` | Azure Key Vault connection | Vault name, tenant ID |
| `vault-aws-sm` | AWS Secrets Manager | Region, access config |
| `vault-cyberark` | CyberArk (future) | API URL, safe name |

---

## 6. Part C: Secrets Management Agent

### Agent Model

The secrets management agent follows the same Monitor → Classify → Act → Report
pattern as the IT helpdesk agent (ADR-004) and certificate agent (ADR-014):

| Phase | IT Helpdesk Agent | Certificate Agent | Secrets Agent |
|-------|-------------------|-------------------|---------------|
| **Monitor** | ServiceNow queue | Cert stores, endpoints | Vaults, AD password policies, API key expiry |
| **Classify** | Ticket type | Cert health state | Secret age, compliance, exposure risk |
| **Act** | Reset password | Renew cert | Rotate credential, update consumers |
| **Report** | Update ticket | Dashboard, alerts | Audit log, compliance report |

### What It Monitors

**Secret Age & Rotation Compliance**:
- Track when each secret was last rotated
- Compare against policy (e.g., AD password max age, corporate policy of 90-day rotation)
- Alert when secrets approach rotation deadline

**AD Service Account Password Expiry**:
- Query AD for service account password last set date
- Calculate time until password policy forces expiry
- Proactively rotate before the account locks out

**API Key Validity**:
- Periodically test API keys against their respective services
- Detect revoked or expired keys before they cause service failures
- Track provider-specific expiration (e.g., some providers expire keys after 1 year)

**Secret Sprawl & Exposure**:
- Detect secrets in configuration files, environment variables, and log files
- Find credentials committed to source control (integrate with git scanning)
- Identify shared credentials used across multiple systems

**Vault Health**:
- Monitor vault backend availability (is Vault sealed? is KMS reachable?)
- Track vault token/lease expiration
- Alert on vault access failures before they cascade

### What It Does About It

**Coordinated Secret Rotation**:
This is the high-value capability. Rotating a secret for a service account that
multiple systems depend on is a multi-step operation that's terrifying to do manually:

```
Rotation Workflow: AD Service Account Password
────────────────────────────────────────────────
1. Generate new password meeting AD complexity requirements
2. Change password in Active Directory (via Windows tool server)
3. Verify new password works (test LDAP bind)
4. Update the secret in Praxova's store (or Vault)
5. Notify all consuming services to refresh credentials
   - Admin portal re-reads credential for LDAP connections
   - Tool server re-reads credential for AD operations
6. Verify all consumers authenticate successfully with new credential
7. If any consumer fails → rollback to previous password
8. Log the entire operation to audit trail
```

The agent handles this end-to-end. A human doing this manually risks a window where
some services have the old password and some have the new one, causing cascading
authentication failures. The agent does it atomically.

**ServiceNow Integration**:
For secrets that require human involvement (e.g., a vendor needs to reissue an API
key), the agent opens a ServiceNow ticket using the same connector as the helpdesk
agent. The ticket includes what needs to happen, why, and the deadline.

**Emergency Revocation**:
If a secret is suspected compromised:
1. Immediately rotate the compromised credential
2. Invalidate all sessions using the old credential
3. Update all consumers
4. Generate an incident report
5. Open a security incident ticket in ServiceNow

### New Tool Server Capabilities

| Capability | Platform | Description |
|------------|----------|-------------|
| `secret-ad-password-rotate` | Windows | Change AD service account password |
| `secret-ad-password-verify` | Windows | Test AD credential validity |
| `secret-scan-files` | Both | Scan config files for plaintext secrets |
| `secret-scan-env` | Both | Enumerate environment variables with secret-like values |
| `vault-health-check` | Both | Test vault backend connectivity and auth |

### Combined with Certificate Agent

The secrets agent and certificate agent are natural companions — potentially modes
of a single "Trust Infrastructure Agent" rather than separate agents:

```
Admin Portal
├── IT Helpdesk Agent
│   └── Domain: End-user IT support
│
├── Trust Infrastructure Agent
│   ├── Certificate Management
│   │   ├── Discovery, inventory, health monitoring
│   │   ├── Auto-renewal, trust distribution
│   │   └── Revocation and emergency response
│   ├── Secrets Management
│   │   ├── Rotation compliance monitoring
│   │   ├── Coordinated credential rotation
│   │   └── Secret sprawl detection
│   └── Shared Capabilities
│       ├── Supervised learning / approval gates
│       ├── ServiceNow ticket integration
│       └── Unified trust dashboard
│
└── (future: Compliance Agent, Onboarding Agent, etc.)
```

---

## 7. Admin Portal UI Changes

### Secrets Dashboard

A dedicated page in the admin portal showing:

- **Secret inventory**: All managed secrets with storage backend, age, and rotation status
  - 🟢 Green: Recently rotated, within policy
  - 🟡 Yellow: Approaching rotation deadline
  - 🔴 Red: Overdue for rotation or detected exposure
- **Rotation timeline**: Upcoming rotation schedule, recently completed rotations
- **Vault status**: Health of connected vault backends (sealed/unsealed, reachable, auth valid)
- **Audit feed**: Recent secret access and rotation events

### ServiceAccount Credential Management

The ServiceAccount create/edit form shows:

- Selected storage backend with appropriate fields (see Section 5)
- Credential age and last rotation date
- Expiration date with countdown
- "Rotate Now" button for immediate rotation
- "Test Credential" button to verify the secret works
- Rotation history showing who/what changed it and when

### Seal Status Indicator

The admin portal header shows seal status:

- 🟢 **UNSEALED** — Secrets store operational, all secrets accessible
- 🔴 **SEALED** — Master key required, no secret operations available
- 🟡 **DEGRADED** — Partial access (e.g., internal store unsealed but Vault unreachable)

---

## 8. Security Architecture

### Encryption Specifications

| Component | Algorithm | Key Size | Mode |
|-----------|-----------|----------|------|
| Master Key derivation | Argon2id | 256-bit output | N/A |
| Key Encryption Key | AES | 256-bit | GCM |
| Data Encryption Keys | AES | 256-bit | GCM |
| Secret fingerprinting | SHA-256 | 256-bit | Hash for change detection only |
| User password hashing | Argon2id | 256-bit | N/A (verify-only, no retrieval) |

### Threat Model

| Threat | Mitigation |
|--------|------------|
| Database dump | Envelope encryption — secrets are encrypted, keys are encrypted, master key is not in DB |
| Memory dump | SecretString zeros memory on dispose; short retention of decrypted values |
| Log exposure | SecretString.ToString() returns "[REDACTED]"; logging middleware strips Authorization headers |
| Network sniffing | All secret-carrying connections use TLS (enforced by ADR-014) |
| Insider with DB access | Requires master key to decrypt; all access audited |
| Master key compromise | KEK rotation re-encrypts all DEKs; master passphrase change is atomic |
| Vault backend outage | Graceful degradation; cached credentials with TTL; alert immediately |
| Service account lockout during rotation | Atomic rotation with rollback; verify before committing |

### Separation of Concerns

```
┌──────────────────────────────────────────────────────────────────┐
│  Admin Portal (Secrets Authority)                                │
│  - Holds master key in memory (after unseal)                     │
│  - Performs all encryption/decryption                             │
│  - Manages key hierarchy                                         │
│  - Connects to external vaults                                   │
│  - Audits all secret access                                      │
└────────────────────────────┬─────────────────────────────────────┘
                             │ TLS (decrypted secrets in response)
              ┌──────────────┼──────────────┐
              │              │              │
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────┐
        │  Agent   │  │  Tool    │  │  Other   │
        │ (Python) │  │  Server  │  │ Consumer │
        │          │  │  (.NET)  │  │          │
        │ Secrets  │  │ Secrets  │  │ Secrets  │
        │ in memory│  │ in memory│  │ in memory│
        │ only     │  │ only     │  │ only     │
        └──────────┘  └──────────┘  └──────────┘
        Never writes   Never writes   Never writes
        secrets to     secrets to     secrets to
        disk           disk           disk
```

No component other than the admin portal has access to the encryption keys or the
vault backends. Consumers receive decrypted secrets over TLS and hold them only in
memory for the duration needed.

---

## 9. Business Model Impact

### Combined Trust Infrastructure Value

Together with the Certificate Agent (ADR-014), the Secrets Agent creates a "Trust
Infrastructure" product tier:

| Tier | Components | Annual Price (est.) |
|------|------------|---------------------|
| Helpdesk Automation | IT Helpdesk Agent | $20k/agent/year |
| Trust Infrastructure | Certificate Agent + Secrets Agent | $15-20k/env/year |
| Platform | All agents, full admin portal | $35-50k/env/year |

### Implementation Services Expansion

The $5k setup engagement grows to cover:
- Secret discovery and inventory baseline
- Credential rotation policy definition
- Vault integration setup (if applicable)
- Service account audit and hardening
- Trust infrastructure health verification

### Competitive Positioning

| Problem | Current Solutions | Praxova Advantage |
|---------|-------------------|-------------------|
| Secret rotation | Manual, CyberArk ($$$), custom scripts | Agent-based, automated, supervised learning |
| Secret sprawl | Occasional audits, git scanning tools | Continuous monitoring, automatic remediation |
| Coordinated rotation | War rooms, change windows, prayer | Atomic multi-system rotation with rollback |
| Compliance reporting | Manual evidence gathering | Continuous, audited, dashboard-ready |

### Customer Retention

Once Praxova manages an organization's secrets alongside their certificates:
- Every service account password rotation is handled automatically
- Every API key renewal is tracked and executed
- Every credential compliance report is generated without effort
- Going back to manual means going back to spreadsheets and 2am outages

---

## 10. Implementation Roadmap

### Phase 0: Fix Current Issues (Immediate — Demo Prep)

- [ ] Replace Argon2 hashing with AES-256-GCM encryption for retrievable secrets
- [ ] Keep Argon2 for user login passwords (verify-only use case)
- [ ] Move LDAP bind password from .env file to encrypted database storage
- [ ] Move JWT signing key from appsettings.json to encrypted storage
- [ ] Implement SecretString type to prevent accidental logging
- [ ] Add credential test buttons to ServiceAccount management UI

### Phase 1: Internal Encrypted Secrets Store (Launch)

- [ ] Implement envelope encryption key hierarchy (MK → KEK → DEK)
- [ ] Implement seal/unseal model with passphrase-based unseal
- [ ] Implement ISecretsService with Internal backend
- [ ] Migrate all ServiceAccount credentials to encrypted storage
- [ ] Add secret expiration tracking and alerts
- [ ] Add rotation history to audit trail
- [ ] Add seal status indicator to admin portal UI

### Phase 2: External Vault Integration (Post-Launch)

- [ ] Implement HashiCorp Vault backend for ISecretsService
- [ ] Implement Azure Key Vault backend
- [ ] Implement AWS Secrets Manager backend
- [ ] Add vault health monitoring
- [ ] Add credential caching with configurable TTL
- [ ] Update ServiceAccount UI with vault-specific configuration fields

### Phase 3: Secrets Management Agent (Product Expansion)

- [ ] Secret age monitoring and rotation compliance checking
- [ ] AD service account password rotation workflow
- [ ] API key validity testing
- [ ] Coordinated multi-system rotation with rollback
- [ ] ServiceNow ticket integration for manual-rotation secrets
- [ ] Secret sprawl detection (config files, env vars, logs)

### Phase 4: Supervised Learning & Policy Engine

- [ ] Human-in-the-loop approval for rotation operations (ADR-013)
- [ ] Policy rules: auto-rotate vs. approve vs. alert-only
- [ ] Graduated autonomy based on approval history
- [ ] Compliance reporting and dashboard

---

## 11. Relationship to ADR-014 (Certificate Management)

Certificates and secrets are two sides of the same coin:

| Aspect | Certificates (ADR-014) | Secrets (ADR-015) |
|--------|------------------------|---------------------|
| Purpose | Prove identity | Grant access |
| Storage | Certificate stores, files | Vaults, databases, env vars |
| Lifecycle | Issue → use → renew → revoke | Create → use → rotate → revoke |
| Failure mode | TLS handshake fails, service unreachable | Authentication fails, access denied |
| Rotation risk | Service interruption if cert mismatch | Cascading auth failures if consumers desync |
| Common pain | Forgotten expiration, untrusted chains | Forgotten rotation, plaintext exposure |

Both require the same operational discipline and benefit from the same agent-based
automation. The implementation roadmaps are designed to proceed in parallel, with
shared infrastructure (audit trail, admin portal UI, supervised learning, tool server
capabilities) reducing the incremental effort for each.

The combined "Trust Infrastructure Agent" is a compelling product story: "Praxova
doesn't just automate your helpdesk — it manages the security infrastructure that
keeps your entire environment trustworthy."

---

## 12. Open Questions

1. Should the master key derivation use Argon2id or PBKDF2? Argon2id is stronger
   but requires a native library; PBKDF2 is built into .NET.
2. What's the right default TTL for cached secrets from external vaults — 5 minutes?
   30 seconds? Configurable per secret?
3. Should the secrets agent be combined with the cert agent into a single "Trust
   Infrastructure Agent" or kept separate for modularity?
4. How do we handle the bootstrap problem — the vault credentials themselves need to
   be stored somewhere before the vault is connected?
5. Should Praxova support CyberArk integration? It's common in banking/healthcare but
   has a complex API.
6. What's the migration path for existing installations that have plaintext/hashed
   secrets in the database?
7. Should the SecretString type use `SecureString` internally on Windows, or is the
   zeroing-on-dispose pattern sufficient given .NET's managed memory model?

---

## 13. References

- **HashiCorp Vault Architecture**: https://developer.hashicorp.com/vault/docs/internals/architecture
- **Envelope Encryption**: https://cloud.google.com/kms/docs/envelope-encryption
- **OWASP Secrets Management Cheat Sheet**: https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html
- **NIST SP 800-57**: Recommendation for Key Management (key hierarchy guidance)
- **CIS Benchmarks**: Password and credential management controls
- **Equifax Breach Report**: Certificate expiration enabled 19-month undetected breach
- **SolarWinds Attack**: Compromised build credentials led to supply chain attack
