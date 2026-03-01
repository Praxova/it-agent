# Phase 2 — Portal Authentication Hardening & Audit Integrity

## Context for the Intermediary Chat

This prompt was produced by a security architecture session that designed the to-be
security posture for Praxova IT Agent. It describes WHAT needs to exist and WHY.
Your job is to compare this against the current codebase, identify the delta, and
produce a Claude Code implementation prompt that gets from as-is to to-be.

**Dependency:** Phase 1 should be complete before starting Phase 2. Phase 1 cleaned
up the `.env.example` and isolated the unseal passphrase — Phase 2 builds on that
foundation.

**Scope:** All work in this phase is in the admin portal (.NET 8 / Blazor Server).
No changes to the Python agent or the tool server.

---

## Feature 1: Local Account Authentication Hardening

### What and Why

The admin portal is the security authority for the entire Praxova deployment. It
holds the encryption keys, manages certificates, stores credentials, issues operation
tokens (Phase 3), and hosts the human approval queue. If an attacker gains admin
access to the portal, they own the entire system.

The local "break-glass" account is the account that works when Active Directory is
unreachable — it is the recovery path. It must always exist and must be properly
secured. This account is NOT replaced by AD authentication (Feature 3 below) — it
is the fallback when AD is down.

Based on Phase 1 feedback, some hardening may already be partially implemented
(the default admin/admin issue was reportedly fixed). The intermediary chat should
examine the current state and identify remaining gaps.

### Specification

**1. Password Hashing — Argon2id**

All local account passwords must be hashed with Argon2id before storage. The
parameters should be:

```
Algorithm: Argon2id
Memory:    64 MB (65536 KiB)
Iterations: 3
Parallelism: 4
Salt:      16 bytes, cryptographically random, per-user
Hash:      32 bytes
```

These parameters follow the OWASP recommendation for Argon2id and provide strong
resistance against GPU-based brute force attacks. The memory parameter (64 MB) is
the key defense — it makes each hash attempt expensive in memory, which is the
resource GPUs are worst at scaling.

The password hash, salt, and algorithm parameters should be stored together so
that the verification function can extract the parameters from the stored hash.
The standard Argon2 PHC string format handles this:

```
$argon2id$v=19$m=65536,t=3,p=4$<base64-salt>$<base64-hash>
```

If there is an existing password storage mechanism (even a weak one), it needs to
be migrated. On login with the old hash format, verify against the old algorithm,
then re-hash with Argon2id and store the new hash. This is a transparent upgrade
that doesn't require password resets for existing accounts.

The intermediary chat should determine whether an Argon2id library is already
available in the .NET project (e.g., `Konscious.Security.Cryptography`,
`Isopoh.Cryptography.Argon2`, or the built-in `System.Security.Cryptography`
Argon2 support in .NET 9+). If the project targets .NET 8, a NuGet package will
be needed. Choose whichever is already in use or, if none, prefer
`Konscious.Security.Cryptography.Argon2` as it is well-maintained and widely used.

**2. First-Login Forced Password Change**

When a local account's password has never been changed by the user (i.e., it was
set by the system during initial setup or by an admin reset), the portal must
force a password change on the next login before granting access to any other page.

Implementation:
- Add a `MustChangePassword` boolean flag to the user/account entity
- Set it to `true` when:
  - The account is first created (initial setup)
  - An administrator resets another user's password
- Set it to `false` when:
  - The user successfully changes their own password through the change-password flow
- On login, if `MustChangePassword` is true:
  - Authenticate the user (verify the current password)
  - Redirect to a password change page
  - Do NOT issue a session token or allow navigation to any other page
  - After successful password change, issue the session token and proceed normally

The first-startup experience should be:
1. Portal initializes, creates default admin account with a generated temporary
   password (displayed in the startup log exactly once)
2. Operator navigates to portal, enters the temporary password
3. Portal forces password change immediately
4. After setting a strong password, operator has full access

If the current code already handles some of this (the Phase 1 feedback mentioned
admin/admin was fixed), the intermediary chat should verify the completeness of
the implementation against this specification.

**3. Password Policy Enforcement**

The portal must enforce a password policy on all password changes (initial setup,
forced change, voluntary change, admin reset). The policy:

```
Minimum length:     12 characters
Maximum length:     128 characters (to prevent DoS via extremely long passwords
                    being fed to Argon2id — this is a real concern)
Complexity:         At least 3 of: uppercase, lowercase, digit, special character
Reuse prevention:   Cannot reuse the current password
Common password:    Check against a list of the top 1000 most common passwords
                    (embed as a static resource, not an external API call)
```

The password policy should be enforced on the SERVER SIDE, not just in client-side
JavaScript/Blazor validation. The API endpoints that accept passwords must validate
before processing.

Password policy violations should return specific, helpful error messages:
- "Password must be at least 12 characters" (not just "Password does not meet policy")
- "Password must contain at least 3 of: uppercase letter, lowercase letter, number,
  special character"
- "This password is too commonly used — please choose a different password"

**4. Account Lockout**

After 5 consecutive failed login attempts, lock the account for 15 minutes. This
is a defense against online brute force attacks from a compromised internal endpoint.

- Track failed attempt count and last failure timestamp per account
- After 5 failures within a 15-minute window, reject all login attempts with
  "Account temporarily locked — try again in N minutes"
- Successful login resets the counter
- The lockout should NOT apply to the break-glass account's reset mechanism (if
  one exists) — but it SHOULD apply to normal login attempts on that account

### Acceptance Criteria

- [ ] Passwords are hashed with Argon2id using the specified parameters
- [ ] Existing weak hashes (if any) are transparently upgraded on next login
- [ ] First login with a system-generated password forces a password change
- [ ] The password change page is the ONLY accessible page when `MustChangePassword`
      is true (no navigation to dashboard, settings, API keys, etc.)
- [ ] Password policy is enforced server-side on all password change operations
- [ ] Passwords shorter than 12 characters are rejected with a specific message
- [ ] Passwords longer than 128 characters are rejected
- [ ] Common passwords (from the embedded list) are rejected
- [ ] After 5 failed login attempts, the account is locked for 15 minutes
- [ ] A successful login resets the failed attempt counter
- [ ] The default admin account created on first startup has `MustChangePassword = true`
- [ ] The temporary password is logged exactly once during first startup, then never
      again (verify it doesn't appear in subsequent log output)

---

## Feature 2: Audit Log Integrity

### What and Why

The audit log records every action the agent takes — ticket classification, approval
decisions, tool server calls, password resets, group membership changes. For a system
that performs privileged AD operations, the audit log is the evidence trail that proves
what happened and when.

Currently, the audit log is stored in the same database managed by the same portal
that an admin controls. There are no integrity protections. A compromised admin (or
an attacker with admin access) could modify or delete audit records to cover their
tracks. This undermines the entire purpose of the audit log.

For v1.0, we need two protections:

1. The audit log cannot be modified or deleted through the portal's API or UI —
   even by administrators
2. Tampering with the database directly is detectable via a hash chain

Full tamper-proof audit (external log shipping, immutable storage, cryptographic
witnesses) is a v2 enterprise feature. The v1.0 protections are the credible
foundation that makes the v2 features meaningful.

### Specification

**1. No Modification Through the Application**

The portal's API and UI must enforce that audit records are append-only:

- No API endpoint should accept PUT, PATCH, or DELETE operations on audit records
- The Blazor UI should not have edit or delete controls on the audit log page
- The Entity Framework configuration for the `AuditEvent` entity should NOT include
  update or delete operations in the repository/service layer
- If a generic CRUD pattern is in use, the audit entity must be explicitly excluded
  from update and delete operations

The intermediary chat should examine the current API surface and Blazor pages to
identify any existing modification paths and close them.

**2. Hash Chain for Tamper Detection**

Each audit record includes a hash of the previous record, creating a chain that
allows verification of log integrity.

Add the following fields to the `AuditEvent` entity:

```csharp
public class AuditEvent
{
    // ... existing fields ...

    /// <summary>
    /// SHA-256 hash of this record's content (excluding the hash fields themselves).
    /// Used as input to the next record's PreviousRecordHash.
    /// </summary>
    public string RecordHash { get; set; }

    /// <summary>
    /// SHA-256 hash from the immediately preceding audit record.
    /// First record in the chain uses a well-known genesis value.
    /// </summary>
    public string PreviousRecordHash { get; set; }

    /// <summary>
    /// Monotonically increasing sequence number. Gaps indicate deleted records.
    /// </summary>
    public long SequenceNumber { get; set; }
}
```

The hash computation:

```
RecordHash = SHA-256(
    SequenceNumber +
    Timestamp (ISO 8601 UTC) +
    AgentId +
    EventType +
    WorkflowExecutionId +
    Capability +
    ToolServerUrl +
    Result +
    DetailJson +
    PreviousRecordHash
)
```

The genesis record (first audit event ever) uses a well-known PreviousRecordHash:
`SHA-256("PRAXOVA-AUDIT-GENESIS-v1")`. This is not a secret — it's a convention
that allows any verifier to validate the chain from the beginning.

**Important implementation detail:** The hash must be computed and stored atomically
with the audit record insertion. If two audit events are written concurrently, they
must be serialized (e.g., using a database transaction with a serializable isolation
level, or a lock in the audit service). The sequence number must be strictly
monotonic with no gaps during normal operation. A gap in the sequence indicates a
deleted record.

**3. Verification Endpoint**

Add an API endpoint that verifies the integrity of the audit chain:

```
GET /api/audit/verify?from={sequenceNumber}&to={sequenceNumber}
```

This endpoint:
- Reads the specified range of audit records
- Recomputes each record's hash from its content
- Verifies each record's `RecordHash` matches the recomputed hash
- Verifies each record's `PreviousRecordHash` matches the preceding record's
  `RecordHash`
- Verifies sequence numbers are contiguous (no gaps)
- Returns a verification report:

```json
{
  "verified": true,
  "records_checked": 1523,
  "first_sequence": 1,
  "last_sequence": 1523,
  "chain_intact": true,
  "gaps_detected": [],
  "hash_mismatches": [],
  "verified_at": "2026-02-27T10:30:00Z"
}
```

Or on failure:

```json
{
  "verified": false,
  "records_checked": 1523,
  "first_sequence": 1,
  "last_sequence": 1523,
  "chain_intact": false,
  "gaps_detected": [412, 413],
  "hash_mismatches": [
    { "sequence": 414, "expected": "abc...", "actual": "def..." }
  ],
  "verified_at": "2026-02-27T10:30:00Z"
}
```

This endpoint should be accessible to Admin and Operator roles. It is read-only
and does not modify any data.

**4. Portal UI — Audit Integrity Indicator**

Add a visual indicator to the audit log page that shows the chain verification
status. This can be a simple badge or banner:

- ✅ "Audit chain verified — N records, no tampering detected" (green)
- ⚠️ "Audit chain verification detected anomalies — see details" (amber/red)

The verification should run automatically when the audit page is loaded (for the
most recent N records, e.g., last 1000) and be triggerable for the full history
via a "Verify Full Chain" button.

### Acceptance Criteria

- [ ] No API endpoint allows modification or deletion of audit records
- [ ] No UI control allows modification or deletion of audit records
- [ ] Each audit record contains `RecordHash`, `PreviousRecordHash`, and
      `SequenceNumber`
- [ ] The hash chain is valid from genesis through the most recent record
- [ ] Concurrent audit writes are serialized (no duplicate sequence numbers,
      no chain breaks from race conditions)
- [ ] `GET /api/audit/verify` returns a correct verification report
- [ ] Manually modifying an audit record in the database (UPDATE statement)
      causes the verification endpoint to report a hash mismatch
- [ ] Manually deleting an audit record from the database causes the verification
      endpoint to report a sequence gap
- [ ] The audit log page shows the integrity verification status
- [ ] Existing audit records (from before this change) are handled gracefully —
      either migrated with a hash chain starting from the most recent record, or
      clearly demarcated as "pre-integrity" records that are not part of the chain

---

## Feature 3: Recovery Key Generation

### What and Why

The unseal passphrase (isolated in Phase 1) derives the master key that decrypts
the entire secrets hierarchy. If the passphrase is lost — the operator forgets it,
the `/etc/praxova/unseal.env` file is deleted, the Docker host is rebuilt without
a backup — all encrypted data is permanently inaccessible. The CA key, the AD
credentials, the ServiceNow credentials, the API keys — all gone. The only option
is a full reset (nuke and re-seed), which means reconfiguring every external
connection from scratch.

A recovery key is a high-entropy key that can decrypt the Key Encryption Key (KEK)
independently of the passphrase. It is generated once at initial setup, displayed
to the operator exactly once, and never stored in the system. The operator writes
it down, puts it in a safe (literally — a physical safe or a secure password manager),
and never enters it into any system unless the passphrase is lost.

### Specification

**1. Recovery Key Generation**

During the portal's first-time initialization (the same startup sequence that
generates the CA, creates the KEK, and initializes the secrets store), generate
a recovery key:

```
Recovery Key:
- 256 bits of cryptographic randomness
- Encoded as Base64 (43 characters) or as a formatted string with dashes
  for readability: XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX (32 hex chars
  with dashes, or similar human-friendly format)
- Choose whichever format is more practical for the display mechanism
```

**2. Recovery Key Encryption of KEK**

The KEK is currently encrypted by the master key (derived from the passphrase
via Argon2id). The recovery key provides a second encryption path:

```
Current:
  Passphrase → Argon2id → Master Key → encrypts KEK

After this feature:
  Passphrase → Argon2id → Master Key → encrypts KEK (primary path)
  Recovery Key → Argon2id → Recovery Master Key → encrypts KEK (recovery path)

Both encrypted-KEK blobs are stored in the database.
Either path can decrypt the KEK independently.
```

The recovery key is run through Argon2id with the same parameters as the passphrase
(64 MB memory, 3 iterations, 4 parallelism) to derive a Recovery Master Key. This
Recovery Master Key encrypts the same KEK, producing a second encrypted-KEK blob
stored alongside the primary one.

**3. Display and Confirmation**

On first startup, after the recovery key is generated and the KEK is encrypted
with both paths:

- Display the recovery key in the startup log with clear formatting:

  ```
  ╔══════════════════════════════════════════════════════════════════╗
  ║                     RECOVERY KEY                                ║
  ║                                                                 ║
  ║  XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX                     ║
  ║                                                                 ║
  ║  WRITE THIS DOWN AND STORE IT IN A SECURE LOCATION.            ║
  ║  This key is the ONLY way to recover your data if the unseal   ║
  ║  passphrase is lost. It will NOT be displayed again.           ║
  ║                                                                 ║
  ╚══════════════════════════════════════════════════════════════════╝
  ```

- Also display the recovery key in the portal UI on the initial setup page
  (the same page where the operator is forced to change the default password).
  Include a checkbox: "I have saved the recovery key in a secure location" that
  must be checked before proceeding.

- The recovery key is NEVER stored in the database, NEVER written to a file,
  and NEVER logged after this initial display. Once the operator dismisses the
  setup page, the recovery key exists only in the operator's possession.

**4. Recovery Flow**

If the operator needs to use the recovery key (passphrase lost):

- The portal's unseal mechanism should accept EITHER the passphrase OR the
  recovery key
- Add an environment variable: `PRAXOVA_RECOVERY_KEY` (or a command-line argument)
- On startup, the portal tries:
  1. `PRAXOVA_UNSEAL_PASSPHRASE` → derive Master Key → decrypt primary KEK blob
  2. If step 1 fails (no passphrase or wrong passphrase):
     `PRAXOVA_RECOVERY_KEY` → derive Recovery Master Key → decrypt recovery KEK blob
  3. If both fail: portal remains sealed

- After recovery, the operator should immediately set a new unseal passphrase.
  The portal should surface a prominent warning: "System was unsealed using the
  recovery key. Please set a new unseal passphrase immediately."

- The intermediary chat should examine how the current unseal mechanism works
  (is it in Program.cs startup? A background service? A middleware?) to determine
  the correct integration point for the recovery key path.

**5. Recovery Key Regeneration**

If the operator believes the recovery key has been compromised, they should be able
to regenerate it from the portal UI (while authenticated as admin AND while the
portal is unsealed). Regeneration:

- Generates a new recovery key
- Re-encrypts the KEK with the new Recovery Master Key
- Replaces the old recovery KEK blob in the database
- Displays the new recovery key exactly once (same display pattern as initial setup)
- The old recovery key immediately stops working

### Acceptance Criteria

- [ ] On first startup, a recovery key is generated and displayed in the log
- [ ] The recovery key is displayed in the initial setup UI with a confirmation
      checkbox
- [ ] The KEK can be decrypted by EITHER the passphrase OR the recovery key
- [ ] Setting `PRAXOVA_RECOVERY_KEY` instead of `PRAXOVA_UNSEAL_PASSPHRASE`
      successfully unseals the portal
- [ ] The recovery key is not stored anywhere in the database or filesystem
      after initial display
- [ ] The recovery key does not appear in log output after the initial display
      (verify by searching subsequent log entries)
- [ ] An admin can regenerate the recovery key from the portal UI
- [ ] After regeneration, the old recovery key no longer works
- [ ] After regeneration, the new recovery key is displayed exactly once
- [ ] The unseal passphrase continues to work after recovery key regeneration
      (the primary path is not affected)
- [ ] If both passphrase and recovery key are provided, the passphrase takes
      precedence (recovery key is ignored if passphrase succeeds)

---

## Implementation Notes for the Intermediary Chat

### Architecture layers in the portal

The portal uses a three-layer architecture:
```
LucidAdmin.Core           — Domain entities, interfaces, enums
LucidAdmin.Infrastructure — EF Core, ISecretsService, ICertificateManager
LucidAdmin.Web            — Blazor pages, Minimal API endpoints, DI wiring
```

- Password hashing logic belongs in Infrastructure (it's a security service)
- The `AuditEvent` entity belongs in Core
- The hash chain computation belongs in Infrastructure (it's data access adjacent)
- The verification endpoint belongs in Web
- The recovery key generation belongs in Infrastructure (alongside existing secrets
  initialization)
- The Blazor UI pages belong in Web

### Interaction between features

Features 1 and 3 interact at one point: the first-startup experience. The flow is:

1. Portal starts for the first time
2. Initializes CA, KEK, secrets store (existing)
3. Generates recovery key (Feature 3) — displays in log
4. Creates default admin account with temporary password and `MustChangePassword = true`
   (Feature 1)
5. Displays temporary password in log
6. Operator opens portal in browser
7. Logs in with temporary password
8. Forced to change password (Feature 1)
9. Recovery key displayed in UI with confirmation checkbox (Feature 3)
10. After confirmation, operator has full access

The intermediary chat should examine the current first-startup sequence and determine
how to integrate these steps without breaking the existing initialization logic.

### Existing code to examine

- Current password storage mechanism (is there a User/Account entity? How are
  passwords currently stored? Is there any hashing at all?)
- Current login flow (Blazor page? API endpoint? Cookie-based? JWT-based?)
- Current audit log implementation (AuditEvent entity, how records are written,
  any existing API endpoints)
- Current secrets initialization (where the KEK is generated and stored)
- Current unseal mechanism (how PRAXOVA_UNSEAL_PASSPHRASE is read and processed)

### NuGet packages that may be needed

- Argon2id: `Konscious.Security.Cryptography.Argon2` (if not already present and
  .NET 8 doesn't have native support)
- No other new packages should be needed — SHA-256 is in `System.Security.Cryptography`,
  EF Core is already present

### Git commit guidance

```
security(portal): implement Argon2id password hashing with policy enforcement
security(portal): add first-login forced password change flow
security(portal): implement account lockout after failed attempts
security(portal): add hash chain integrity to audit log
security(portal): add audit chain verification endpoint and UI indicator
security(portal): implement recovery key generation and dual-path unseal
```

### What NOT to change

- Do not modify the Python agent
- Do not modify the tool server
- Do not modify the PKI certificate generation logic (that's working correctly)
- Do not modify the envelope encryption implementation (KEK/DEK structure) beyond
  adding the recovery key's second encrypted-KEK blob
- Do not modify the ServiceAccount entity or credential storage
- Do not add AD authentication in this phase — that is a separate feature that
  builds on top of the local account hardening done here
- Do not modify the Blazor layout, navigation, or styling beyond what's needed for
  the new pages/components described above
