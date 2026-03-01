# Phase 4a — Active Directory Authentication for Portal Login

## Context for the Intermediary Chat

This prompt was produced by a security architecture session. It describes WHAT needs
to exist and WHY. Your job is to compare against the current codebase and produce a
Claude Code implementation prompt.

**Dependency:** Phase 2 must be complete (Argon2id password hashing, first-login
forced password change). Phase 4a adds AD as an authentication source alongside the
local account system from Phase 2.

---

## What and Why

The admin portal currently authenticates users via a local account (admin/admin at
first boot, forced password change on first login after Phase 2). This is the
break-glass account. But in production, portal operators are domain users who
already have AD credentials. They should log in with their AD username and password,
and their portal role should be determined by their AD group membership.

This is both a usability feature (operators don't manage separate portal credentials)
and a security feature (portal access is governed by the same identity system the
organization already manages — password policies, account lockout, MFA if configured
at the AD level all apply automatically).

The local account remains as a break-glass fallback. If AD is unreachable (DC down,
network issue, DNS failure), the local admin account still works. This prevents a
scenario where a DC outage locks operators out of the portal they need to diagnose
the outage.

---

## Specification

### 1. Authentication Flow

The login page should attempt AD authentication first, then fall back to local:

```
User enters username + password
        │
        ▼
Is an AD service account configured in the portal?
        │
   ┌────┴────┐
   No        Yes
   │         │
   │         ▼
   │    Attempt LDAP simple bind against DC
   │    using user's credentials
   │         │
   │    ┌────┴────┐
   │    Success   Failure
   │    │         │
   │    │         ▼
   │    │    Is this a local account username?
   │    │         │
   │    │    ┌────┴────┐
   │    │    Yes       No
   │    │    │         │
   │    │    │         ▼
   │    │    │    Return "Invalid credentials"
   │    │    │
   │    │    ▼
   │    │    Attempt local account auth (Argon2id)
   │    │         │
   │    │    ┌────┴────┐
   │    │    Success   Failure
   │    │    │         │
   │    │    │         ▼
   │    │    │    Return "Invalid credentials"
   │    │    │
   │    ▼    ▼
   │    Authenticated — determine role (see below)
   │
   ▼
Attempt local account auth only (Argon2id)
```

**Important:** The login flow should NOT reveal whether a username exists in AD or
locally. Both "AD bind failed" and "local auth failed" return the same generic
"Invalid credentials" message. This prevents username enumeration.

**Important:** The AD bind uses the USER's credentials, not the service account's.
The service account is used only for group membership lookups AFTER successful
authentication (see step 2). The authentication itself is a direct LDAP simple bind
as the user — this proves the user knows their password without the portal ever
handling or storing their AD password.

### 2. AD Group → Portal Role Mapping

After successful AD authentication, the portal needs to determine the user's role.
This is done by checking AD group membership:

| AD Group | Portal Role | Permissions |
|----------|-------------|-------------|
| `Praxova-Admins` | Admin | Full configuration, service accounts, workflows, approvals, audit |
| `Praxova-Operators` | Operator | View configuration, manage approvals, view audit |
| `Praxova-Viewers` | Viewer | View audit log, view dashboards (read-only) |

If a user is in multiple groups, the highest-privilege role wins (Admin > Operator > Viewer).

If a user authenticates via AD but is not in ANY of the mapped groups, the login
should be **rejected** with a message like "Access denied — your account is not a
member of any Praxova access group. Contact your administrator." This prevents
any domain user from accessing the portal.

The group names should be **configurable** in the portal settings, not hardcoded.
Different organizations will have different naming conventions. The defaults above
are sensible starting points.

**Group membership lookup:** After the user authenticates (LDAP bind succeeds), the
portal uses the configured AD service account (the `windows-ad` ServiceAccount
entity) to perform a group membership query for the authenticated user. This is
because the user's own bind may not have permission to read their own group
membership in all AD configurations. The service account has read access to the
relevant OUs.

```
1. User authenticates: LDAP bind as user@montanifarms.com with user's password
2. Bind succeeds → user identity confirmed
3. Portal binds as svc-praxova (service account) to query group membership
4. Portal queries: "What groups is user@montanifarms.com a member of?"
5. Portal maps groups to role
6. Portal creates JWT session token with the determined role
```

### 3. LDAP Connection Details

The portal already has a `windows-ad` ServiceAccount type with LDAPS connection
details (host, port, bind DN, encrypted password). The AD authentication feature
should reuse this same service account configuration — no new credential storage
needed.

The intermediary chat should examine the existing `windows-ad` ServiceAccount
entity to understand what connection details are stored and how to establish an
LDAP connection from the portal. The portal is a .NET 8 application — use
`System.DirectoryServices.Protocols` (the cross-platform LDAP library) rather
than `System.DirectoryServices` (Windows-only).

**Note:** The portal runs in a Linux container. `System.DirectoryServices.Protocols`
works on Linux via `libldap`. The Dockerfile may need `libldap-2.5-0` or similar
installed. The intermediary chat should check whether this is already in the
container image.

**LDAPS (port 636):** The connection to the DC must use LDAPS. The DC's certificate
must be trusted by the portal container. Check whether the portal's CA trust
bootstrap (the internal Praxova CA) covers this, or whether the DC's certificate
is from a different CA (the domain's own Enterprise CA, typically). If the DC cert
is from a different CA, that CA's root cert needs to be mounted into the container
and added to the trust store.

In the montanifarms.com lab, the DC's LDAPS cert is self-signed or issued by the
domain's own CA. The intermediary chat should check how the agent currently handles
LDAPS trust (since the agent already connects to AD indirectly via the tool server)
and whether there's an established pattern for trusting the DC's CA in containers.

### 4. Portal Configuration for AD Auth

Add a configuration section (portal settings page or appsettings.json) for AD
authentication:

```json
{
  "ActiveDirectoryAuth": {
    "Enabled": true,
    "ServiceAccountId": "guid-of-windows-ad-service-account",
    "Domain": "montanifarms.com",
    "BaseDN": "DC=montanifarms,DC=com",
    "UserSearchBase": "OU=Users,DC=montanifarms,DC=com",
    "GroupMappings": {
      "Admin": "Praxova-Admins",
      "Operator": "Praxova-Operators",
      "Viewer": "Praxova-Viewers"
    },
    "FallbackToLocalAuth": true
  }
}
```

The `ServiceAccountId` references an existing ServiceAccount entity. The portal
decrypts the service account's credentials (via the secrets store) to perform
group membership lookups. The user's own credentials are used for the initial
bind (authentication) and are never stored.

**Where this config lives:** Ideally in the database (manageable via portal UI),
not just appsettings.json. This lets operators configure AD auth without touching
files on the Docker host. But the intermediary chat should check what configuration
patterns the portal currently uses and follow the established approach.

### 5. Session Token Changes

After Phase 2, the portal issues JWT session tokens for authenticated users. The
token includes the user's role. For AD-authenticated users:

- `sub` claim: the user's AD username (e.g., `jsmith` or `jsmith@montanifarms.com`)
- `role` claim: the mapped portal role (Admin, Operator, Viewer)
- `auth_method` claim: `"ad"` (to distinguish from `"local"` for the break-glass account)

The `auth_method` claim is useful for audit trail differentiation. It does NOT affect
authorization — an Admin is an Admin regardless of how they authenticated.

### 6. Audit Events

Log authentication events:

- `PortalLoginSuccess` — user, auth_method (ad/local), role, source IP
- `PortalLoginFailed` — username (not password), auth_method attempted, source IP,
  reason (invalid_credentials, ad_unreachable, no_group_membership)
- `PortalLoginFallback` — user attempted AD auth, AD unreachable, fell back to local

These use the Phase 2 hash chain audit system.

### 7. UI Changes

The login page itself doesn't change much — it's still a username/password form.
But add:

- A small indicator on the login page showing whether AD authentication is
  configured and reachable (e.g., a subtle "AD ✓" or "AD ✗" badge). This helps
  operators understand which auth path will be used.
- After login, the user's session should show their role and auth method in the
  portal header (e.g., "jsmith (Admin, AD)" or "admin (Admin, Local)").

### 8. First-Time Setup Flow

When the portal is first deployed:

1. No AD service account is configured yet → AD auth is disabled
2. Operator logs in with the local break-glass account (admin)
3. Operator creates a `windows-ad` ServiceAccount with DC connection details
4. Operator enables AD authentication in portal settings
5. Operator configures group mappings
6. Operator verifies by logging out and logging back in with their AD credentials
7. If it works → they're authenticated via AD with the correct role
8. If it fails → they can still use the local account to troubleshoot

The local break-glass account should ALWAYS work, regardless of AD configuration.
It should never be possible to lock yourself out of the portal by misconfiguring AD.

---

## Implementation Notes for the Intermediary Chat

### Examine existing auth infrastructure

Phase 2 added local account authentication with Argon2id. Look at:
- How the login endpoint works (Blazor page? API endpoint? Both?)
- How JWT session tokens are created and validated
- How roles are represented in the token and enforced in the UI
- Whether there's an `IAuthenticationService` or similar abstraction that could
  be extended to support AD as a second provider

### Examine existing LDAP/AD code

The tool server connects to AD via LDAPS, but the portal may not have any LDAP
code yet. Check:
- Does the portal have any `System.DirectoryServices` references?
- Does the portal connect to AD for anything currently?
- The agent connects to the tool server (which connects to AD) — the portal
  doesn't need direct AD access for ticket operations. AD auth for login is
  potentially the portal's first direct AD connection.

### LDAP library choice

For .NET 8 on Linux, use `System.DirectoryServices.Protocols` with the NuGet
package `System.DirectoryServices.Protocols`. This is the cross-platform option.
Do NOT use `System.DirectoryServices.AccountManagement` — it's Windows-only.

The LDAP bind for authentication:
```csharp
using var connection = new LdapConnection(new LdapDirectoryIdentifier(host, port));
connection.SessionOptions.SecureSocketLayer = true; // LDAPS
connection.Credential = new NetworkCredential(username, password, domain);
connection.AuthType = AuthType.Basic; // Simple bind
connection.Bind(); // Throws on failure
```

The group membership query (after auth, using service account):
```csharp
var searchRequest = new SearchRequest(
    baseDN,
    $"(&(objectClass=user)(sAMAccountName={username}))",
    SearchScope.Subtree,
    "memberOf"
);
var response = (SearchResponse)connection.SendRequest(searchRequest);
var memberOf = response.Entries[0].Attributes["memberOf"];
```

### Container dependencies

The portal container may need `libldap` for `System.DirectoryServices.Protocols`
to work on Linux. Check the Dockerfile and add if missing:
```dockerfile
RUN apt-get update && apt-get install -y libldap-2.5-0
```

### Git commit guidance

```
feat(portal): AD LDAP authentication provider
feat(portal): AD group to portal role mapping
feat(portal): configurable AD auth settings in portal UI
feat(portal): authentication fallback chain (AD → local)
feat(portal): auth method indicator in session token
feat(portal): audit events for login attempts
fix(portal): add libldap dependency to container image
docs: update DEV-QUICKREF with AD auth setup steps
```

### What NOT to change

- Do not modify the local account authentication — ADD AD auth alongside it
- Do not modify the Phase 2 password hashing or forced change logic
- Do not remove the break-glass local admin account
- Do not modify the operation token system from Phase 3
- Do not add AD password change capabilities to the portal (the portal
  authenticates against AD, it does not manage AD passwords)
