# TD-007B: Active Directory LDAP Authentication

## Context

**Prerequisite:** TD-007A must be completed first (MustChangePassword flag, password policy,
ChangePassword page).

The Admin Portal runs on Linux (Alton's Ubuntu workstation) and needs to authenticate users
against the Active Directory domain `montanifarms.com` (DC at 172.16.119.20). The portal
already has a `ServiceAccount` entity pattern used for AD connections to the tool server —
we'll use the same pattern for the portal's own LDAP bind.

The portal uses TWO auth flows:
- **Cookie auth** via `AccountController.cs` — used by Blazor UI (form POST to `/account/login`)
- **JWT auth** via `AuthEndpoints.cs` — used by API clients (agent, tool servers)

Both flows need to support AD authentication.

**Cross-platform consideration:** `System.DirectoryServices.AccountManagement` is Windows-only.
Use `System.DirectoryServices.Protocols` which works on Linux with the `libldap` native library.
The NuGet package is `System.DirectoryServices.Protocols`.

## Requirements

### 1. Add NuGet package

**File:** `admin/dotnet/src/LucidAdmin.Web/LucidAdmin.Web.csproj`

Add:
```xml
<PackageReference Include="System.DirectoryServices.Protocols" Version="8.0.1" />
```

Also ensure `libldap-2.5-0` is available on the Linux host. Add a note to the README but
don't try to install it from code. (Alton's Ubuntu system likely already has it.)

### 2. Add AD authentication configuration

**File:** `admin/dotnet/src/LucidAdmin.Web/appsettings.json`

Add a new section:
```json
{
  "ActiveDirectory": {
    "Enabled": false,
    "Domain": "montanifarms.com",
    "LdapServer": "172.16.119.20",
    "LdapPort": 389,
    "UseLdaps": false,
    "SearchBase": "DC=montanifarms,DC=com",
    "BindUserDn": "",
    "BindPasswordEnvVar": "LUCID_AD_BIND_PASSWORD",
    "RoleMapping": {
      "AdminGroup": "LucidAdmin-Admins",
      "OperatorGroup": "LucidAdmin-Operators",
      "ViewerGroup": "LucidAdmin-Viewers"
    },
    "DefaultRole": "Viewer"
  }
}
```

**IMPORTANT:** Set `Enabled: false` by default. AD auth is opt-in. The portal must always
work with just local accounts. Create a strongly-typed options class for this.

### 3. Create AD configuration options class

**New file:** `admin/dotnet/src/LucidAdmin.Web/Models/ActiveDirectoryOptions.cs`

```csharp
public class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";

    public bool Enabled { get; set; } = false;
    public string Domain { get; set; } = "";
    public string LdapServer { get; set; } = "";
    public int LdapPort { get; set; } = 389;
    public bool UseLdaps { get; set; } = false;
    public string SearchBase { get; set; } = "";
    public string BindUserDn { get; set; } = "";
    public string BindPasswordEnvVar { get; set; } = "LUCID_AD_BIND_PASSWORD";
    public RoleMappingOptions RoleMapping { get; set; } = new();
    public string DefaultRole { get; set; } = "Viewer";
}

public class RoleMappingOptions
{
    public string AdminGroup { get; set; } = "LucidAdmin-Admins";
    public string OperatorGroup { get; set; } = "LucidAdmin-Operators";
    public string ViewerGroup { get; set; } = "LucidAdmin-Viewers";
}
```

Register in Program.cs:
```csharp
builder.Services.Configure<ActiveDirectoryOptions>(
    builder.Configuration.GetSection(ActiveDirectoryOptions.SectionName));
```

### 4. Create authentication provider abstraction

**New file:** `admin/dotnet/src/LucidAdmin.Core/Interfaces/Services/IAuthenticationProvider.cs`

```csharp
public interface IAuthenticationProvider
{
    /// <summary>
    /// Attempt to authenticate a user.
    /// </summary>
    /// <param name="username">Username (may include domain prefix or UPN suffix)</param>
    /// <param name="password">Password</param>
    /// <returns>AuthenticationResult with success/failure, user info, and role</returns>
    Task<AuthenticationResult> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Whether this provider can handle the given username format.
    /// </summary>
    bool CanHandle(string username);
}
```

**New file:** `admin/dotnet/src/LucidAdmin.Core/Models/AuthenticationResult.cs`
(or put in a ValueObjects folder)

```csharp
public record AuthenticationResult
{
    public bool Success { get; init; }
    public string? Username { get; init; }        // Normalized username (SAM account name)
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public UserRole Role { get; init; } = UserRole.Viewer;
    public string? ErrorMessage { get; init; }
    public string AuthenticationMethod { get; init; } = "Local";  // "Local" or "ActiveDirectory"
    public bool MustChangePassword { get; init; } = false;
}
```

### 5. Implement LocalAuthenticationProvider

**New file:** `admin/dotnet/src/LucidAdmin.Web/Services/LocalAuthenticationProvider.cs`

This extracts the existing login logic from AccountController into a provider:
- `CanHandle()`: Returns true for ANY username (local is always the fallback)
- `AuthenticateAsync()`: Looks up user in DB, verifies password hash, checks IsEnabled,
  checks lockout, returns AuthenticationResult with the user's DB role and MustChangePassword flag.
- Handles failed login counting and lockout (existing logic in AccountController).

### 6. Implement LdapAuthenticationProvider

**New file:** `admin/dotnet/src/LucidAdmin.Web/Services/LdapAuthenticationProvider.cs`

```csharp
public class LdapAuthenticationProvider : IAuthenticationProvider
{
    private readonly IOptions<ActiveDirectoryOptions> _options;
    private readonly ILogger<LdapAuthenticationProvider> _logger;

    public bool CanHandle(string username)
    {
        if (!_options.Value.Enabled) return false;

        // Handle: user@domain.com, DOMAIN\user, or plain username when AD is enabled
        return username.Contains('@') ||
               username.Contains('\\') ||
               _options.Value.Enabled;  // When AD is enabled, try AD for all non-local users
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password)
    {
        var config = _options.Value;
        var samAccountName = NormalizeUsername(username);

        try
        {
            // Step 1: LDAP bind with user's credentials to verify password
            using var connection = new LdapConnection(
                new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

            connection.AuthType = AuthType.Basic;
            // Use UPN format for bind: user@domain
            var bindDn = $"{samAccountName}@{config.Domain}";
            var credential = new System.Net.NetworkCredential(bindDn, password);
            connection.Bind(credential);

            // Step 2: Search for user to get attributes
            var searchRequest = new SearchRequest(
                config.SearchBase,
                $"(sAMAccountName={samAccountName})",
                SearchScope.Subtree,
                "displayName", "mail", "memberOf", "sAMAccountName");

            var searchResponse = (SearchResponse)connection.SendRequest(searchRequest);

            if (searchResponse.Entries.Count == 0)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "User not found in Active Directory"
                };
            }

            var entry = searchResponse.Entries[0];
            var displayName = GetAttribute(entry, "displayName");
            var email = GetAttribute(entry, "mail");
            var memberOf = GetAttributes(entry, "memberOf");

            // Step 3: Resolve role from group membership
            var role = ResolveRole(memberOf, config.RoleMapping, config.DefaultRole);

            return new AuthenticationResult
            {
                Success = true,
                Username = samAccountName,
                DisplayName = displayName,
                Email = email ?? $"{samAccountName}@{config.Domain}",
                Role = role,
                AuthenticationMethod = "ActiveDirectory",
                MustChangePassword = false  // AD manages its own password policy
            };
        }
        catch (LdapException ex) when (ex.ErrorCode == 49) // Invalid credentials
        {
            _logger.LogWarning("AD authentication failed for {Username}: invalid credentials", samAccountName);
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password"
            };
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP error during authentication for {Username}", samAccountName);
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Active Directory is unavailable. Use a local account."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during AD authentication for {Username}", samAccountName);
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Authentication service error"
            };
        }
    }
}
```

**Important implementation details:**
- `NormalizeUsername()`: Extract SAM account name from `DOMAIN\user` or `user@domain.com` formats
- `ResolveRole()`: Check memberOf DNs against configured group names. Use the highest-privilege
  match (Admin > Operator > Viewer). If no group matches, use DefaultRole.
- `GetAttribute()`/`GetAttributes()`: Helper methods to safely extract LDAP attributes
- The bind uses the USER's credentials (not a service account) — this validates their password
- The search after bind uses the now-authenticated connection to read attributes
- Wrap the whole thing in `Task.Run()` since `System.DirectoryServices.Protocols` is synchronous

### 7. Create AuthenticationService that orchestrates providers

**New file:** `admin/dotnet/src/LucidAdmin.Web/Services/AuthenticationService.cs`

```csharp
public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly LocalAuthenticationProvider _localProvider;
    private readonly LdapAuthenticationProvider _ldapProvider;
    private readonly IOptions<ActiveDirectoryOptions> _adOptions;
    private readonly ILogger<AuthenticationService> _logger;

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password)
    {
        // Rule 1: Username "admin" ALWAYS authenticates locally (break-glass)
        if (username.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            return await _localProvider.AuthenticateAsync(username, password);
        }

        // Rule 2: If AD is enabled, try AD first for domain-formatted usernames
        if (_adOptions.Value.Enabled && _ldapProvider.CanHandle(username))
        {
            var adResult = await _ldapProvider.AuthenticateAsync(username, password);
            if (adResult.Success)
            {
                // Ensure AD user exists in local DB (for audit trail, preferences)
                // Create or update a shadow user record
                return adResult;
            }

            // If AD auth failed due to connectivity, fall through to local
            // If AD auth failed due to bad credentials, return the failure
            if (!adResult.ErrorMessage?.Contains("unavailable") ?? true)
            {
                return adResult;
            }

            _logger.LogWarning("AD unavailable, falling back to local auth for {Username}", username);
        }

        // Rule 3: Try local authentication
        return await _localProvider.AuthenticateAsync(username, password);
    }
}
```

Register in DI (Program.cs):
```csharp
builder.Services.AddScoped<LocalAuthenticationProvider>();
builder.Services.AddScoped<LdapAuthenticationProvider>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
```

### 8. Create/update shadow user records for AD users

When an AD user authenticates successfully for the first time, create a local `User` record
with:
- Username = SAM account name
- Email = from AD
- PasswordHash = "" (empty — they never use local auth)
- Role = resolved from AD groups
- IsEnabled = true
- A new field or marker to indicate this is an AD-sourced user

Add a field to User entity:
```csharp
public string AuthenticationSource { get; set; } = "Local";  // "Local" or "ActiveDirectory"
```

This requires another migration. Can be combined with the TD-007A migration if doing
both at once, or create a second migration `AddAuthenticationSource`.

On subsequent AD logins, update the shadow user's Role (in case group membership changed)
and LastLogin timestamp.

**IMPORTANT:** Shadow users with `AuthenticationSource = "ActiveDirectory"` should NOT be
editable via the local user management UI (password, role) since those are managed in AD.

### 9. Refactor AccountController to use IAuthenticationService

**File:** `admin/dotnet/src/LucidAdmin.Web/Controllers/AccountController.cs`

Replace the direct DB lookup and password verification with:
```csharp
var authService = HttpContext.RequestServices.GetRequiredService<IAuthenticationService>();
var result = await authService.AuthenticateAsync(username, password);

if (!result.Success)
{
    return Redirect($"/login?error={Uri.EscapeDataString(result.ErrorMessage ?? "Authentication failed")}");
}
```

Then create claims from `AuthenticationResult` and proceed with cookie sign-in as before.
Add an `AuthenticationMethod` claim so the UI knows how the user authenticated.

### 10. Refactor AuthEndpoints to use IAuthenticationService

**File:** `admin/dotnet/src/LucidAdmin.Web/Endpoints/AuthEndpoints.cs`

Same pattern as AccountController — replace direct DB/password logic with
`IAuthenticationService.AuthenticateAsync()`.

### 11. Update Login.razor for AD awareness

**File:** `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Login.razor`

When AD is enabled (inject `IOptions<ActiveDirectoryOptions>` and check `.Value.Enabled`):
- Add a hint below the form: "You can sign in with your domain credentials (user@montanifarms.com)"
- The username field should accept both plain usernames and domain-qualified names
- No need for a domain dropdown — the AuthenticationService handles routing based on username format

When AD is NOT enabled:
- Show the form as-is (minus the default credentials hint per TD-007A)

### 12. AD connectivity test endpoint

**File:** `admin/dotnet/src/LucidAdmin.Web/Endpoints/AuthEndpoints.cs`

Add a diagnostic endpoint (admin-only):
```
GET /api/auth/ad-status
```

Returns:
```json
{
  "enabled": true,
  "server": "172.16.119.20",
  "domain": "montanifarms.com",
  "reachable": true,
  "latencyMs": 12
}
```

This tests LDAP connectivity (anonymous or with bind credentials) without authenticating a user.
Useful for troubleshooting.

## Verification Steps

### Prep: Create AD groups on the domain controller

Run on DC (172.16.119.20) via PowerShell:
```powershell
# Create Lucid Admin portal role groups
New-ADGroup -Name "LucidAdmin-Admins" -GroupScope Global -GroupCategory Security -Path "OU=Groups,DC=montanifarms,DC=com" -Description "Lucid Admin Portal - Administrator role"
New-ADGroup -Name "LucidAdmin-Operators" -GroupScope Global -GroupCategory Security -Path "OU=Groups,DC=montanifarms,DC=com" -Description "Lucid Admin Portal - Operator role"
New-ADGroup -Name "LucidAdmin-Viewers" -GroupScope Global -GroupCategory Security -Path "OU=Groups,DC=montanifarms,DC=com" -Description "Lucid Admin Portal - Viewer role"

# Add a test user to the Admins group (use an existing test user)
Add-ADGroupMember -Identity "LucidAdmin-Admins" -Members "luke.skywalker"
Add-ADGroupMember -Identity "LucidAdmin-Operators" -Members "han.solo"
```

### Test sequence

1. Update `appsettings.Development.json` to enable AD:
```json
{
  "ActiveDirectory": {
    "Enabled": true,
    "Domain": "montanifarms.com",
    "LdapServer": "172.16.119.20",
    "LdapPort": 389,
    "SearchBase": "DC=montanifarms,DC=com"
  }
}
```

2. Install libldap if needed: `sudo apt install libldap-2.5-0`

3. Restart the portal

4. Test AD status: `curl http://localhost:5000/api/auth/ad-status` — should show reachable

5. Login with "admin" / (new password from TD-007A) — should work (local, break-glass)

6. Login with "luke.skywalker@montanifarms.com" / (AD password) — should work, role = Admin

7. Login with "han.solo@montanifarms.com" / (AD password) — should work, role = Operator

8. Login with "luke.skywalker" (no domain suffix) / (AD password) — should also work
   (AD is enabled, non-"admin" usernames try AD first)

9. Verify audit log shows authentication method for each login

10. Stop the DC (simulate AD outage), try logging in as luke.skywalker — should fail
    with "Active Directory is unavailable" message

11. Login as "admin" during AD outage — should still work (break-glass)

## Files Created
- `admin/dotnet/src/LucidAdmin.Web/Models/ActiveDirectoryOptions.cs`
- `admin/dotnet/src/LucidAdmin.Core/Interfaces/Services/IAuthenticationProvider.cs`
- `admin/dotnet/src/LucidAdmin.Core/Models/AuthenticationResult.cs`
- `admin/dotnet/src/LucidAdmin.Web/Services/LocalAuthenticationProvider.cs`
- `admin/dotnet/src/LucidAdmin.Web/Services/LdapAuthenticationProvider.cs`
- `admin/dotnet/src/LucidAdmin.Web/Services/AuthenticationService.cs`

## Files Modified
- `admin/dotnet/src/LucidAdmin.Web/LucidAdmin.Web.csproj` (NuGet package)
- `admin/dotnet/src/LucidAdmin.Web/appsettings.json`
- `admin/dotnet/src/LucidAdmin.Core/Entities/User.cs` (AuthenticationSource field)
- `admin/dotnet/src/LucidAdmin.Web/Program.cs` (DI registration, options binding)
- `admin/dotnet/src/LucidAdmin.Web/Controllers/AccountController.cs` (use IAuthenticationService)
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/AuthEndpoints.cs` (use IAuthenticationService, AD status)
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Login.razor` (AD hint)
- `admin/dotnet/src/LucidAdmin.Infrastructure/Data/Migrations/` (auto-generated)

## Do NOT modify
- `Argon2PasswordHasher.cs` — only used for local accounts
- The change-password flow from TD-007A — AD users don't use it (AD manages passwords)
- Tool server authentication — that's a separate concern
