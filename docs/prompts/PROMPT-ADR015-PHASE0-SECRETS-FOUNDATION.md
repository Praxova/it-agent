# Claude Code Prompt: ADR-015 Phase 0 — Secrets Foundation & Security Hardening

## Step 0: Git Branch Setup

Before making any code changes, create a feature branch:

```bash
cd /home/alton/Documents/lucid-it-agent
git checkout main
git checkout -b feature/adr015-secrets-foundation
```

All work for this prompt should be committed to this branch. Use meaningful commit messages prefixed with `ADR-015:` (e.g., `ADR-015: Add SecretString type with JSON converter`). Commit after each logical unit of work, not one giant commit at the end.

## Context

Read these files first to understand the current architecture:
- `CLAUDE_CONTEXT.md` (project overview, ADRs, patterns)
- `docs/adr/ADR-015-secrets-management-credentials.md` (full ADR)
- `admin/dotnet/src/LucidAdmin.Core/Entities/ServiceAccount.cs`
- `admin/dotnet/src/LucidAdmin.Core/Enums/CredentialStorageType.cs`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/EncryptionService.cs`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/Argon2PasswordHasher.cs`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Credentials/` (all files)
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/JwtTokenService.cs`
- `admin/dotnet/src/LucidAdmin.Web/Services/LdapAuthenticationProvider.cs`
- `admin/dotnet/src/LucidAdmin.Web/appsettings.json`
- `admin/dotnet/src/LucidAdmin.Infrastructure/DependencyInjection.cs`

## Current State (What Already Works)

The credential infrastructure is **already partially implemented and correct**:

1. **EncryptionService** — AES-256-GCM encryption with key loaded from file (`PRAXOVA_KEY_FILE`) or env var (`PRAXOVA_ENCRYPTION_KEY`). Supports hex, base64, and passphrase-derived keys. This is solid.
2. **DatabaseCredentialProvider** — Encrypts/decrypts ServiceAccount credentials using EncryptionService. Stores `EncryptedCredentials` + `CredentialNonce` on the entity. Working correctly.
3. **Argon2PasswordHasher** — Used ONLY for User login password verification (one-way hashing). This is the correct use of Argon2. Do NOT change this.
4. **CredentialService** — Orchestrates credential providers with audit logging. Working correctly.
5. **ServiceAccount entity** — Already has `EncryptedCredentials`, `CredentialNonce`, `CredentialStorage`, `CredentialReference`, `CredentialsUpdatedAt`.

## What This Prompt Implements (5 Items)

### 1. SecretString Type (Logging Protection)

Create `LucidAdmin.Core/Security/SecretString.cs`:

```csharp
/// <summary>
/// A string wrapper that prevents accidental logging or serialization of secret values.
/// ToString() returns "[REDACTED]". JSON serialization writes "[REDACTED]".
/// The actual value is only accessible via explicit .Reveal() call.
/// Implements IDisposable to zero memory on cleanup.
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

    // Implicit conversion from string for convenience
    public static implicit operator SecretString(string value) => new(value);

    public bool IsDisposed => _value == null;

    public void Dispose()
    {
        _value = null; // In .NET managed memory, best-effort zeroing is unreliable
                       // but nulling the reference allows GC to collect the string.
                       // For true secure zeroing, use SecureString or pinned byte arrays.
    }
}
```

Also create a `System.Text.Json` converter `SecretStringJsonConverter` in `LucidAdmin.Core/Security/` that:
- On **write**: outputs `"[REDACTED]"` (never serializes the actual value)
- On **read**: creates a `SecretString` from the JSON string value (for deserialization from config/API input)

Register the converter globally in `Program.cs` JSON options so that any `SecretString` property on any model is automatically protected.

### 2. JWT Signing Key — Move to Auto-Generated Encrypted Storage

**Current problem**: JWT signing key is a hardcoded string in `appsettings.json`:
```json
"Jwt": {
    "SecretKey": "your-secret-key-min-32-chars-change-in-production!"
}
```

**Solution**: Auto-generate a cryptographically random JWT signing key at first startup and store it encrypted in the database using the existing EncryptionService.

Create `LucidAdmin.Infrastructure/Services/JwtKeyManager.cs`:

- At startup, check if a JWT signing key exists in the database (use a new `SystemSecret` table or a dedicated row — see schema changes below).
- If not, generate a 64-byte random key using `RandomNumberGenerator`, encrypt it via `IEncryptionService`, and store it.
- If yes, decrypt it and hold it in memory for the lifetime of the application.
- Expose the key via an `IJwtKeyManager` interface with a `byte[] GetSigningKey()` method.
- Modify `JwtTokenService` to inject `IJwtKeyManager` instead of reading from `IConfiguration["Jwt:SecretKey"]`.
- **Keep** the `Jwt:Issuer`, `Jwt:Audience`, and `Jwt:ExpirationMinutes` settings in `appsettings.json` — only the secret key moves.
- Remove the `Jwt:SecretKey` entry from `appsettings.json` and `appsettings.Development.json`.

New database table `SystemSecrets`:
```
Id (Guid, PK)
Name (string, unique) — e.g., "jwt-signing-key"
EncryptedValue (byte[])
Nonce (byte[])
Purpose (string) — human-readable description
CreatedAt (DateTime)
RotatedAt (DateTime?)
```

This table will also be used by ADR-014 for the CA private key, so design it generically.

### 3. LDAP Bind Password — Route Through Credential Service

**Current problem**: The LDAP bind password is read from an environment variable (`PRAXOVA_AD_BIND_PASSWORD`) via `ActiveDirectoryOptions.BindPasswordEnvVar`. The `LdapAuthenticationProvider` doesn't use the credential service at all.

**Solution**: The ActiveDirectory settings page in the admin portal should let the administrator configure which ServiceAccount to use for LDAP bind operations. This ServiceAccount's credentials are then retrieved via the existing `ICredentialService` at authentication time.

Changes:
- Add `LdapServiceAccountId` (Guid?) to `ActiveDirectoryOptions` model. This references the ServiceAccount used for LDAP bind.
- In the AD Settings page (`Settings/ActiveDirectory.razor`), add a dropdown to select a ServiceAccount of provider type `windows-ad`. If one doesn't exist, show a link to create one.
- Modify `LdapAuthenticationProvider` to accept `ICredentialService` and retrieve the bind credentials from the selected ServiceAccount instead of from an environment variable.
- **Fallback behavior**: If `LdapServiceAccountId` is not set, fall back to the legacy env var behavior (log a deprecation warning). This prevents breaking existing setups.
- Remove `BindUserDn` and `BindPasswordEnvVar` from `ActiveDirectoryOptions` since these are now part of the ServiceAccount's configuration. Keep them temporarily for backward compatibility but log deprecation warnings if used.

**Important**: The `LdapAuthenticationProvider` currently does NOT use a bind account — it binds directly with the user's credentials (line ~57: `connection.Bind(credential)` where `credential` is the user typing their password). The `BindUserDn`/`BindPasswordEnvVar` fields in `ActiveDirectoryOptions` exist but are unused in the auth flow. 

However, service account bind IS needed for:
- The AD Settings page "Test Connection" button
- Future: looking up user attributes before authentication (e.g., checking if account is locked)
- The tool server's AD operations (password reset, group management) — but that uses its own ServiceAccount

So: wire the `LdapServiceAccountId` to be used by the AD Settings "Test Connection" functionality and any pre-auth lookups. The user authentication bind should continue using the user's own credentials (that's correct behavior — you don't want a shared bind account for user auth).

### 4. ServiceAccount Entity — Add Credential Lifecycle Fields

Add these fields to the `ServiceAccount` entity:

```csharp
/// <summary>
/// When this credential expires (null = no expiration set)
/// </summary>
public DateTime? CredentialExpiresAt { get; set; }

/// <summary>
/// When credentials were last deliberately rotated (distinct from CredentialsUpdatedAt
/// which tracks any update including initial creation)
/// </summary>
public DateTime? LastRotatedAt { get; set; }

/// <summary>
/// SHA-256 fingerprint of the plaintext credential for change detection.
/// Allows checking if a credential changed without decrypting it.
/// </summary>
public string? CredentialFingerprint { get; set; }
```

Update the `DatabaseCredentialProvider.StoreAsync()` method to:
- Compute and store the SHA-256 fingerprint of the credential JSON before encryption
- Set `CredentialExpiresAt` if provided in the `CredentialSet.ExpiresAt`

Create a new EF Core migration for these schema changes + the `SystemSecrets` table.

**Since this is a dev environment with test data, use a destructive migration approach:**
- Drop and recreate the database after migration
- Update any seed data to work with the new schema
- Document in the migration that this is a pre-launch schema change

### 5. Credential Test Buttons in ServiceAccount UI

The ServiceAccount Edit page (`ServiceAccounts/Edit.razor`) should have a "Test Credentials" button that:
1. Calls `ICredentialService.GetCredentialsAsync()` to verify the credential can be retrieved and decrypted
2. If the ServiceAccount has a provider (e.g., `windows-ad`, `servicenow`, `llm-openai`), calls the provider's health check to verify the credential actually works against the external system
3. Shows results inline: ✅ "Credentials valid — connected to montanifarms.com" or ❌ "Connection failed: LDAP bind rejected"

The endpoint for this already exists partially — look at the provider `TestConnectivityAsync` methods and the existing health check infrastructure. Wire a new endpoint:

```
POST /api/v1/service-accounts/{id}/test-credentials
```

Response:
```json
{
  "canDecrypt": true,
  "canConnect": true,
  "message": "Successfully connected to montanifarms.com via LDAP",
  "testedAt": "2026-02-15T10:30:00Z"
}
```

If `canDecrypt` is false, `canConnect` is not attempted.

## Implementation Order

1. **SecretString type + JSON converter** — No dependencies, pure addition
2. **SystemSecrets table + EF migration** — Schema foundation for JWT key storage
3. **JwtKeyManager** — Depends on SystemSecrets table and EncryptionService
4. **ServiceAccount lifecycle fields** — Schema addition, update DatabaseCredentialProvider
5. **LDAP bind through credential service** — Depends on existing credential infrastructure
6. **Credential test endpoint + UI button** — Depends on provider health checks

## Files to Create
- `admin/dotnet/src/LucidAdmin.Core/Security/SecretString.cs`
- `admin/dotnet/src/LucidAdmin.Core/Security/SecretStringJsonConverter.cs`
- `admin/dotnet/src/LucidAdmin.Core/Entities/SystemSecret.cs`
- `admin/dotnet/src/LucidAdmin.Core/Interfaces/Services/IJwtKeyManager.cs`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/JwtKeyManager.cs`
- New EF Core migration (via `dotnet ef migrations add SecretsFoundation`)

## Files to Modify
- `admin/dotnet/src/LucidAdmin.Core/Entities/ServiceAccount.cs` — Add lifecycle fields
- `admin/dotnet/src/LucidAdmin.Infrastructure/Services/JwtTokenService.cs` — Use IJwtKeyManager
- `admin/dotnet/src/LucidAdmin.Infrastructure/Credentials/DatabaseCredentialProvider.cs` — Compute fingerprint on store
- `admin/dotnet/src/LucidAdmin.Infrastructure/DependencyInjection.cs` — Register new services
- `admin/dotnet/src/LucidAdmin.Infrastructure/Data/LucidDbContext.cs` — Add SystemSecrets DbSet
- `admin/dotnet/src/LucidAdmin.Web/appsettings.json` — Remove Jwt:SecretKey
- `admin/dotnet/src/LucidAdmin.Web/appsettings.Development.json` — Remove Jwt:SecretKey if present
- `admin/dotnet/src/LucidAdmin.Web/Program.cs` — Register SecretStringJsonConverter, IJwtKeyManager
- `admin/dotnet/src/LucidAdmin.Web/Models/ActiveDirectoryOptions.cs` — Add LdapServiceAccountId
- `admin/dotnet/src/LucidAdmin.Web/Services/LdapAuthenticationProvider.cs` — Add ICredentialService for bind account
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/CredentialEndpoints.cs` — Add test-credentials endpoint
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/ServiceAccounts/Edit.razor` — Add test button

## What NOT to Change
- **Argon2PasswordHasher** — Leave as-is. It's correct for user login passwords.
- **EncryptionService** — Leave as-is. It's the foundation everything builds on. We'll enhance it in Phase 1 (envelope encryption) but not now.
- **CredentialService / CredentialProviderRegistry** — Leave as-is. The abstraction is solid.
- **User entity** — Don't change password handling for user accounts.

## Testing

After implementation:
1. Delete the database: `rm admin/dotnet/src/LucidAdmin.Web/lucid-admin-dev.db`
2. Run: `cd admin/dotnet/src/LucidAdmin.Web && dotnet run`
3. Verify: First startup auto-generates JWT signing key (check logs for "Generated new JWT signing key")
4. Verify: Login still works (JWT tokens issued with the new auto-generated key)
5. Verify: Create a ServiceAccount with Database credential storage, store credentials, verify they can be retrieved
6. Verify: "Test Credentials" button works on a ServiceAccount edit page
7. Verify: `SecretString.ToString()` returns `[REDACTED]` (unit test)
8. Verify: JSON serialization of a class with a `SecretString` property outputs `"[REDACTED]"` (unit test)

## Notes for Claude Code

- The project uses .NET 8. Run `dotnet build` from `admin/dotnet/` to verify compilation.
- EF Core migrations are in `LucidAdmin.Infrastructure/Migrations/`. Generate new migration with:
  `cd admin/dotnet/src/LucidAdmin.Web && dotnet ef migrations add SecretsFoundation --project ../LucidAdmin.Infrastructure/`
- The database is SQLite for development (`lucid-admin-dev.db`).
- Follow existing code patterns: records for DTOs, interfaces in Core, implementations in Infrastructure.
- Use `ILogger<T>` for all logging. Never log secret values.
- XML doc comments on all public types and members.
