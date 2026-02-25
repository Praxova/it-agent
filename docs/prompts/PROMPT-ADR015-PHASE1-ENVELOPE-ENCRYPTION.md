# Claude Code Prompt: ADR-015 Phase 1 — Envelope Encryption & Seal/Unseal

## Step 0: Git Branch Setup

Continue working on the existing feature branch:

```bash
cd /home/alton/Documents/lucid-it-agent
git checkout feature/adr015-secrets-foundation
```

All work for this prompt should be committed to this branch. Use meaningful commit messages prefixed with `ADR-015:` (e.g., `ADR-015: Implement SealManager with Argon2id key derivation`). Commit after each logical unit of work.

## Context

Read these files first to understand the current architecture:
- `CLAUDE_CONTEXT.md` (project overview)
- `docs/adr/ADR-015-secrets-management-credentials.md` (full ADR — focus on Section 4: Envelope Encryption)
- `docs/prompts/PROMPT-ADR015-PHASE0-SECRETS-FOUNDATION.md` (what Phase 0 implemented)

### Current State After Phase 0

Phase 0 established these foundations:
- **EncryptionService** (`Infrastructure/Services/EncryptionService.cs`) — AES-256-GCM, loads a single flat encryption key from `PRAXOVA_KEY_FILE` or `PRAXOVA_ENCRYPTION_KEY` env var. Registered as **singleton**.
- **SystemSecrets table** — Generic encrypted secret storage (currently holds JWT signing key).
- **JwtKeyManager** — Stores/retrieves JWT signing key encrypted via EncryptionService.
- **DatabaseCredentialProvider** — Encrypts ServiceAccount credentials via EncryptionService.
- **SecretString type** — Prevents accidental logging of secrets.
- **Credential lifecycle fields** — `CredentialExpiresAt`, `LastRotatedAt`, `CredentialFingerprint` on ServiceAccount.

### What Phase 0 Does NOT Have

- No passphrase-based key protection — the encryption key is a flat file/env var with no protection layer
- No seal/unseal concept — if the key is available, everything works; if not, nothing works (no graceful degradation)
- No key hierarchy — single flat key encrypts everything directly
- A database dump + the key file = full compromise (no defense in depth)

## What This Prompt Implements

### Architecture: Two-Level Key Hierarchy with Seal/Unseal

```
┌─────────────────────────────────────────────────────────┐
│  Master Key (MK)                                        │
│  Derived from: passphrase via Argon2id                  │
│  Purpose: Protects the Key Encryption Key               │
│  Storage: NEVER persisted — derived at startup,         │
│           held only in memory                           │
└────────────────────────┬────────────────────────────────┘
                         │ encrypts
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Key Encryption Key (KEK)                               │
│  Generated: Once at install time (AES-256, 32 bytes)    │
│  Purpose: Encrypts all secret data                      │
│  Storage: Encrypted by MK, stored in SystemSecrets      │
│  This replaces the current flat encryption key          │
└────────────────────────┬────────────────────────────────┘
                         │ encrypts
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Secret Values                                          │
│  ServiceAccount credentials, JWT signing key, etc.      │
│  Storage: AES-256-GCM encrypted in database             │
└─────────────────────────────────────────────────────────┘
```

**Why two levels, not three?** The ADR describes a three-level hierarchy (MK → KEK → DEK). Per-secret DEKs are a future optimization that enables key rotation without re-encrypting every secret. For Phase 1, the KEK encrypts secrets directly — this delivers the security benefit (passphrase-protected keys, seal/unseal) without the complexity. The architecture supports adding DEKs later without breaking changes.

**What changes from Phase 0:** The current `EncryptionService` loads a flat key from a file/env var. After this prompt, the key is a KEK that's encrypted in the database, protected by a master passphrase. A database dump alone exposes nothing — you need the passphrase to derive the MK to decrypt the KEK.

---

## Implementation Items

### 1. ISealManager Interface

Create `LucidAdmin.Core/Interfaces/Services/ISealManager.cs`:

```csharp
/// <summary>
/// Manages the seal/unseal lifecycle of the secrets store.
/// When sealed, no secret operations are possible.
/// When unsealed, the Key Encryption Key (KEK) is available in memory.
/// </summary>
public interface ISealManager
{
    /// <summary>Whether the secrets store is currently unsealed and operational.</summary>
    bool IsUnsealed { get; }

    /// <summary>Whether this is a first-time setup (no KEK exists in the database yet).</summary>
    bool RequiresInitialization { get; }

    /// <summary>
    /// Initialize the secrets store for the first time.
    /// Generates a new KEK, encrypts it with a master key derived from the passphrase,
    /// and stores it in the database.
    /// </summary>
    Task InitializeAsync(string passphrase);

    /// <summary>
    /// Unseal the secrets store by providing the master passphrase.
    /// Derives the master key via Argon2id and decrypts the KEK from the database.
    /// </summary>
    /// <returns>True if unseal succeeded; false if the passphrase is wrong.</returns>
    Task<bool> UnsealAsync(string passphrase);

    /// <summary>
    /// Seal the secrets store, zeroing the KEK from memory.
    /// All subsequent secret operations will fail until unsealed again.
    /// </summary>
    void Seal();

    /// <summary>
    /// Get the current KEK bytes. Throws if sealed.
    /// This is called by EncryptionService to perform actual encryption/decryption.
    /// </summary>
    byte[] GetEncryptionKey();
}
```

### 2. SealManager Implementation

Create `LucidAdmin.Infrastructure/Services/SealManager.cs`:

**Key derivation:** Use `Konscious.Security.Cryptography.Argon2id` (already in the project — used by `Argon2PasswordHasher`).

```
Argon2id parameters:
- Memory: 128 MB (131072 KB) — same as Argon2PasswordHasher
- Iterations: 4
- Parallelism: 2
- Output: 32 bytes (AES-256 key)
- Salt: 32 bytes, randomly generated at initialization, stored with the KEK
```

**Initialization flow (first startup):**
1. Generate a 32-byte random salt
2. Derive MK from passphrase + salt via Argon2id (32 bytes)
3. Generate a 32-byte random KEK
4. Encrypt the KEK with MK using AES-256-GCM (use raw `System.Security.Cryptography.AesGcm` directly — don't go through EncryptionService since EncryptionService depends on us)
5. Store in `SystemSecrets` table:
   - Name: `"envelope-kek"`
   - EncryptedValue: the encrypted KEK + GCM tag (same format as EncryptionService: ciphertext || tag)
   - Nonce: the GCM nonce used to encrypt the KEK
   - Purpose: `"Key Encryption Key — protects all secret data"`
   - A separate `SystemSecret` for the salt:
     - Name: `"envelope-kek-salt"`
     - EncryptedValue: the raw salt bytes (NOT encrypted — the salt is not secret, but storing it as a SystemSecret reuses existing infrastructure)
     - Nonce: empty byte array (salt is not encrypted)
     - Purpose: `"Argon2id salt for master key derivation"`

   **Alternative (cleaner):** Add a `Metadata` (string, nullable) column to `SystemSecret` and store the salt as hex in the KEK record's metadata, avoiding a second record. Choose whichever approach you think is cleaner given the existing schema. If adding a column, add it in the migration.

**Unseal flow (subsequent startups):**
1. Load the KEK record and salt from `SystemSecrets`
2. Derive MK from passphrase + salt via Argon2id
3. Decrypt the KEK using MK
4. If decryption fails (wrong passphrase → GCM auth tag mismatch → `CryptographicException`), return false
5. If successful, store KEK bytes in a private field and set `IsUnsealed = true`

**Seal flow:**
1. Zero out the KEK bytes in memory (Array.Clear)
2. Set `IsUnsealed = false`

**Registration:** Singleton (holds KEK in memory for app lifetime).

**Thread safety:** Use a `ReaderWriterLockSlim` or `lock` to protect the KEK field, since seal/unseal could theoretically be called from different threads.

### 3. Modify EncryptionService to Use ISealManager

The current `EncryptionService` loads its key in the constructor from config/env var. Change it to:

1. Remove the key-loading logic from the constructor (`LoadEncryptionKey`, `GetKeyFilePath`, `GetDefaultKeyPaths`, `LoadKeyFromFile`, `ParseKeyString` — all of it)
2. Inject `ISealManager` instead of loading a key directly
3. On each `Encrypt`/`Decrypt` call, get the key from `_sealManager.GetEncryptionKey()`
4. `IsConfigured` now returns `_sealManager.IsUnsealed`

```csharp
public class EncryptionService : IEncryptionService
{
    private readonly ISealManager _sealManager;
    private readonly ILogger<EncryptionService> _logger;
    // ... keep constants (KeySize, NonceSize, TagSize)

    public EncryptionService(ISealManager sealManager, ILogger<EncryptionService> logger)
    {
        _sealManager = sealManager;
        _logger = logger;
    }

    public bool IsConfigured => _sealManager.IsUnsealed;

    public (byte[] CipherText, byte[] Nonce) Encrypt(byte[] plaintext)
    {
        var key = _sealManager.GetEncryptionKey(); // throws if sealed
        // ... rest of encryption logic using key (same as current, but use local var instead of _key)
    }

    // ... same pattern for Decrypt
}
```

**Important:** EncryptionService is a singleton. SealManager is a singleton. The dependency is singleton → singleton, which is fine.

### 4. Startup Flow Changes

Modify `Program.cs` startup to handle the seal/unseal lifecycle:

**Auto-unseal for development/containers:** Check for `PRAXOVA_UNSEAL_PASSPHRASE` environment variable. If set, auto-unseal at startup (after database migration, before `JwtKeyManager.InitializeAsync()`).

```csharp
// After database migration, before app.Run()
var sealManager = app.Services.GetRequiredService<ISealManager>();

// Check for auto-unseal passphrase
var autoUnsealPassphrase = Environment.GetEnvironmentVariable("PRAXOVA_UNSEAL_PASSPHRASE");

if (sealManager.RequiresInitialization)
{
    if (!string.IsNullOrEmpty(autoUnsealPassphrase))
    {
        await sealManager.InitializeAsync(autoUnsealPassphrase);
        logger.LogInformation("Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE");
    }
    else
    {
        logger.LogWarning(
            "Secrets store requires initialization. Set PRAXOVA_UNSEAL_PASSPHRASE or " +
            "use POST /api/v1/system/initialize to set the master passphrase.");
    }
}
else if (!string.IsNullOrEmpty(autoUnsealPassphrase))
{
    var success = await sealManager.UnsealAsync(autoUnsealPassphrase);
    if (success)
        logger.LogInformation("Secrets store unsealed via PRAXOVA_UNSEAL_PASSPHRASE");
    else
        logger.LogError("Failed to unseal secrets store — PRAXOVA_UNSEAL_PASSPHRASE is incorrect");
}
else
{
    logger.LogWarning("Secrets store is SEALED. Use POST /api/v1/system/unseal to provide the master passphrase.");
}

// JwtKeyManager initialization now depends on unseal state
if (sealManager.IsUnsealed)
{
    var jwtKeyManager = app.Services.GetRequiredService<IJwtKeyManager>();
    await jwtKeyManager.InitializeAsync();
}
```

**Migration of existing data:** Since this is a dev environment, the approach is:
1. Remove `PRAXOVA_ENCRYPTION_KEY` / `PRAXOVA_KEY_FILE` from the environment
2. Delete the dev database
3. Set `PRAXOVA_UNSEAL_PASSPHRASE` to a dev passphrase (e.g., `"dev-passphrase-change-in-production"`)
4. On first startup, SealManager initializes with this passphrase and everything is re-seeded fresh

### 5. Seal/Unseal API Endpoints

Create `LucidAdmin.Web/Endpoints/SystemEndpoints.cs`:

```
GET  /api/v1/system/seal-status    — Returns { isSealed, requiresInitialization }
POST /api/v1/system/initialize     — First-time setup: { passphrase } → initializes KEK
POST /api/v1/system/unseal         — Provide passphrase to unseal: { passphrase } → true/false
POST /api/v1/system/seal           — Seal the secrets store (admin only)
```

**Authorization:**
- `seal-status` — No auth required (the UI needs to check this before login is possible, since login requires JWT which requires unseal)
- `initialize` — No auth required (only works when `RequiresInitialization` is true — first-time setup only)
- `unseal` — No auth required (same reason as above — can't authenticate while sealed)
- `seal` — Requires Admin role (don't let operators accidentally seal the system)

**Security note:** The `initialize` and `unseal` endpoints accept a passphrase in the request body. This is acceptable because:
1. These should only be called over TLS in production (ADR-014 will enforce this)
2. The passphrase is never logged (use SecretString internally)
3. Rate limiting should be applied to prevent brute force (add a note/TODO for this)

Request models:
```csharp
public record InitializeRequest(string Passphrase);
public record UnsealRequest(string Passphrase);
public record SealStatusResponse(bool IsSealed, bool RequiresInitialization);
```

### 6. Admin Portal UI — Seal Status Indicator

**Seal status in the header/nav bar:**
Add a visual indicator to the main layout (`Components/Layout/`) showing:
- 🟢 **UNSEALED** (or just a green lock icon) when `ISealManager.IsUnsealed` is true
- 🔴 **SEALED** (or a red lock icon) when sealed

Keep it subtle — a small icon in the top nav is sufficient. Don't make it a full banner unless sealed.

**Sealed state behavior:**
When the admin portal is sealed:
- The login page should still work IF it's local account auth (which uses Argon2 hashing, not the secrets store)
- AD authentication will fail (needs credential service → needs encryption → needs unseal)
- After login, show a prominent alert/banner: "Secrets store is sealed. Operations requiring credentials are unavailable. [Unseal]"
- The [Unseal] link opens a dialog or navigates to an unseal page where the admin enters the passphrase
- After successful unseal, the banner disappears and full functionality is available

**Unseal page/dialog:**
Create a simple component (can be a dialog or a dedicated page at `/system/unseal`):
- If `RequiresInitialization`: show "Set Master Passphrase" with a passphrase + confirm field
- If sealed (not first-time): show "Enter Master Passphrase" with a single passphrase field
- Show success/failure feedback
- On success, redirect to dashboard or reload current page

### 7. Graceful Degradation When Sealed

Services that depend on `IEncryptionService` or `ICredentialService` should handle the sealed state gracefully rather than throwing unhandled exceptions:

- **JwtKeyManager:** If sealed at startup, skip initialization. Log a warning. JWT operations will fail, which means API auth fails, which is expected behavior when sealed. Local auth (cookie-based Blazor) should still work since it uses Argon2 password verification, not secrets.
- **DatabaseCredentialProvider:** `IsAvailable` already returns `_encryptionService.IsConfigured`, which will return false when sealed. This means credential retrieval returns null, which existing callers handle.
- **Health endpoints:** Should report degraded health when sealed.
- **Dashboard:** Should show a warning widget when sealed.

Most of this graceful degradation already works because `EncryptionService.IsConfigured` returns false when there's no key. After this change, it returns false when sealed — same code path, different reason.

---

## Implementation Order

1. **ISealManager interface** — No dependencies
2. **Add Metadata column to SystemSecret** (or create salt storage approach) — Schema change
3. **SealManager implementation** — Depends on ISealManager, SystemSecrets table, Argon2id
4. **Modify EncryptionService** — Remove flat key loading, inject ISealManager
5. **Update DI registration** — Register SealManager as singleton
6. **Update Program.cs startup** — Auto-unseal flow, conditional JwtKeyManager init
7. **System API endpoints** — seal-status, initialize, unseal, seal
8. **UI: Seal status indicator + unseal dialog** — Depends on API endpoints
9. **EF Core migration** — For any schema changes
10. **Test the full flow**

## Files to Create
- `admin/dotnet/src/LucidAdmin.Core/Interfaces/Services/ISealManager.cs`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/SealManager.cs`
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/SystemEndpoints.cs`
- Unseal UI component (dialog or page — implementer's choice on exact file)
- New EF Core migration

## Files to Modify
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/EncryptionService.cs` — Major refactor: remove key loading, inject ISealManager
- `admin/dotnet/src/LucidAdmin.Infrastructure/DependencyInjection.cs` — Register ISealManager/SealManager
- `admin/dotnet/src/LucidAdmin.Web/Program.cs` — Add unseal startup flow
- `admin/dotnet/src/LucidAdmin.Core/Entities/SystemSecret.cs` — Possibly add Metadata field
- `admin/dotnet/src/LucidAdmin.Infrastructure/Data/LucidDbContext.cs` — If schema changes
- `admin/dotnet/src/LucidAdmin.Web/Components/Layout/` — Seal status indicator in nav
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/HealthEndpoints.cs` — Report sealed state in health

## Files NOT to Modify
- **Argon2PasswordHasher** — User login hashing is unrelated to the seal manager
- **DatabaseCredentialProvider** — It already handles `IsConfigured = false` correctly
- **JwtKeyManager** — Only needs a null-check at startup, no structural changes
- **CredentialService / CredentialProviderRegistry** — Abstraction is solid, no changes needed
- **SecretString** — Complete as-is

## Testing

After implementation:
1. Delete the database: `rm admin/dotnet/src/LucidAdmin.Web/lucid-admin-dev.db`
2. Remove old env vars: `unset PRAXOVA_ENCRYPTION_KEY PRAXOVA_KEY_FILE`
3. Set new env var: `export PRAXOVA_UNSEAL_PASSPHRASE="dev-passphrase-change-in-production"`
4. Run: `cd admin/dotnet/src/LucidAdmin.Web && dotnet run`
5. Verify logs show: "Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE"
6. Verify: Login works, ServiceAccount credentials can be stored/retrieved
7. Test seal status endpoint: `curl http://localhost:5000/api/v1/system/seal-status` → `{"isSealed": false, "requiresInitialization": false}`
8. Test manual unseal flow: restart without `PRAXOVA_UNSEAL_PASSPHRASE`, verify sealed state, unseal via API
9. Verify: Seal status indicator shows in the admin portal UI
10. Verify: Wrong passphrase on unseal returns failure (not a crash)

## Notes for Claude Code

- The `Konscious.Security.Cryptography` NuGet package is already in the project (used by `Argon2PasswordHasher`). Use the same library for MK derivation in `SealManager`.
- `SealManager` must use `AesGcm` directly from `System.Security.Cryptography` for encrypting/decrypting the KEK — do NOT use `EncryptionService` because that creates a circular dependency (EncryptionService depends on SealManager).
- The `SealManager` needs database access but is a singleton. Use the same pattern as `JwtKeyManager`: inject `IServiceProvider` and create a scoped `LucidDbContext` when needed.
- Be careful with the startup ordering: SealManager must be initialized/unsealed BEFORE JwtKeyManager, which must be initialized BEFORE the app starts serving requests.
- When deleting the flat key loading code from EncryptionService, make sure no other code directly references `PRAXOVA_ENCRYPTION_KEY` or `PRAXOVA_KEY_FILE`. Search the codebase. The only remaining reference should be in documentation (which should be updated to reference `PRAXOVA_UNSEAL_PASSPHRASE` instead).
