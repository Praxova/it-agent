# TD-007C: AD Settings UI, User Management, and DC Setup

## Context

**Prerequisite:** TD-007A and TD-007B must be completed first.

With TD-007A and TD-007B complete, the portal can authenticate against both local accounts
and Active Directory. However, AD configuration is only in `appsettings.json` — there's no
way to view, test, or manage it from the portal UI. The user management pages also need to
distinguish between local and AD-sourced users.

This deliverable adds the management UI and a DC setup script for the lab.

## Requirements

### 1. Create DC Setup PowerShell Script

**New file:** `admin/dotnet/scripts/Setup-LucidAdminGroups.ps1`

```powershell
<#
.SYNOPSIS
    Creates Active Directory groups required for Lucid Admin Portal role mapping.
.DESCRIPTION
    Creates three security groups in AD that map to portal roles:
    - LucidAdmin-Admins    → Admin role (full access)
    - LucidAdmin-Operators → Operator role (can execute, cannot configure)
    - LucidAdmin-Viewers   → Viewer role (read-only)

    Optionally adds specified users to each group for testing.
.PARAMETER OUPath
    The OU to create groups in. Default: searches for a Groups OU or uses Users container.
.PARAMETER TestUsers
    If specified, adds test user mappings. Expects hashtable like:
    @{ Admins = @("luke.skywalker"); Operators = @("han.solo"); Viewers = @("leia.organa") }
.EXAMPLE
    .\Setup-LucidAdminGroups.ps1
    .\Setup-LucidAdminGroups.ps1 -TestUsers @{ Admins = @("luke.skywalker"); Operators = @("han.solo") }
#>
param(
    [string]$OUPath,
    [hashtable]$TestUsers
)

# Import AD module
Import-Module ActiveDirectory -ErrorAction Stop

# Determine OU
if (-not $OUPath) {
    $domain = Get-ADDomain
    # Try common OU names
    $candidates = @("OU=Groups", "OU=Security Groups", "CN=Users")
    foreach ($candidate in $candidates) {
        $testPath = "$candidate,$($domain.DistinguishedName)"
        if ([adsi]::Exists("LDAP://$testPath")) {
            $OUPath = $testPath
            break
        }
    }
    if (-not $OUPath) {
        $OUPath = $domain.UsersContainer
    }
}

Write-Host "Creating Lucid Admin groups in: $OUPath" -ForegroundColor Cyan

$groups = @(
    @{ Name = "LucidAdmin-Admins";    Description = "Lucid Admin Portal - Administrator role (full access)" },
    @{ Name = "LucidAdmin-Operators"; Description = "Lucid Admin Portal - Operator role (execute operations)" },
    @{ Name = "LucidAdmin-Viewers";   Description = "Lucid Admin Portal - Viewer role (read-only access)" }
)

foreach ($group in $groups) {
    $existing = Get-ADGroup -Filter "Name -eq '$($group.Name)'" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  [EXISTS] $($group.Name)" -ForegroundColor Yellow
    } else {
        New-ADGroup -Name $group.Name `
                    -GroupScope Global `
                    -GroupCategory Security `
                    -Path $OUPath `
                    -Description $group.Description
        Write-Host "  [CREATED] $($group.Name)" -ForegroundColor Green
    }
}

# Add test users if specified
if ($TestUsers) {
    Write-Host "`nAdding test user memberships:" -ForegroundColor Cyan
    $roleMap = @{ Admins = "LucidAdmin-Admins"; Operators = "LucidAdmin-Operators"; Viewers = "LucidAdmin-Viewers" }
    foreach ($role in $TestUsers.Keys) {
        $groupName = $roleMap[$role]
        if (-not $groupName) { Write-Warning "Unknown role: $role"; continue }
        foreach ($user in $TestUsers[$role]) {
            try {
                Add-ADGroupMember -Identity $groupName -Members $user -ErrorAction Stop
                Write-Host "  [ADDED] $user → $groupName" -ForegroundColor Green
            } catch {
                Write-Warning "  [FAILED] $user → $groupName : $_"
            }
        }
    }
}

Write-Host "`nDone. Configure these group names in the Admin Portal's ActiveDirectory settings." -ForegroundColor Cyan
Write-Host "Default mapping:" -ForegroundColor Gray
Write-Host "  LucidAdmin-Admins    → Admin role" -ForegroundColor Gray
Write-Host "  LucidAdmin-Operators → Operator role" -ForegroundColor Gray
Write-Host "  LucidAdmin-Viewers   → Viewer role" -ForegroundColor Gray
```

### 2. Add AD Settings Page

**New file:** `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/ActiveDirectory.razor`

This page shows (read-only, since config comes from appsettings.json):
- AD Enabled/Disabled status (prominent badge)
- LDAP Server and Port
- Domain name
- Search Base DN
- Role mapping table (AD Group → Portal Role)
- A "Test Connection" button that calls the `/api/auth/ad-status` endpoint
- Connection test result (reachable, latency, any errors)

If AD is disabled, show an info panel explaining how to enable it (edit appsettings.json
or environment variables) and why it's disabled by default.

Add a link to this page in NavMenu.razor under Settings.

### 3. Enhance Users Management Page

**File:** `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/Users.razor`

Enhance the existing users page (or create it if it doesn't exist as a full page):

**User list table should show:**
- Username
- Email
- Role (with colored badge)
- Auth Source ("Local" badge or "Active Directory" badge, different colors)
- Last Login
- Status (Enabled/Disabled)
- Actions column

**Behavior differences by auth source:**
- **Local users**: Can edit role, reset password, enable/disable, delete (except "admin")
- **AD users**: Role shows as "from AD group: LucidAdmin-Admins" — cannot edit role locally.
  Can only disable portal access (which blocks login even if AD auth succeeds).
  Cannot reset password (password managed in AD). Show a note: "Manage this user's
  password and group membership in Active Directory."

**Add user dialog:**
- For local users: existing create form (username, email, password, role)
- When AD is enabled, add a "Sync AD User" option that lets you type an AD username,
  looks them up via LDAP, and creates a shadow record. This is optional — shadow records
  are also created automatically on first login.

### 4. Add AuthenticationSource indicator to claims/UI

The `AuthenticationSource` field added to User in TD-007B should be visible in the UI.
In `MainLayout.razor` or wherever the user's name is displayed, add a small indicator:
- Local users: show a key icon or "Local" chip
- AD users: show a directory icon or "AD" chip

This helps administrators quickly see how they authenticated.

### 5. Handle AD user that loses group membership

In `LdapAuthenticationProvider`, when resolving roles: if a user authenticates successfully
via AD but is NOT a member of ANY Lucid role group, use the `DefaultRole` from config
(default: Viewer). Log a warning.

If you want to be stricter (deny access to users with no Lucid group), add a config option:
```json
"RequireRoleGroup": false
```
When true, users with no matching group get an error: "Your AD account is not authorized
for the Lucid Admin Portal. Contact your administrator to be added to a LucidAdmin group."

### 6. Session/token refresh for AD role changes

When an AD user's group membership changes in AD, their portal role should update on next
login. Since we update the shadow user's role on every successful AD authentication (from
TD-007B), this happens naturally at login time. Document this behavior — role changes take
effect on next login, not immediately.

For long-lived sessions (8-hour cookie), the role won't update mid-session. This is
acceptable for MVP. Document it.

## Verification Steps

1. Run `Setup-LucidAdminGroups.ps1` on the DC:
   ```powershell
   .\Setup-LucidAdminGroups.ps1 -TestUsers @{
       Admins = @("luke.skywalker")
       Operators = @("han.solo")
       Viewers = @("leia.organa")
   }
   ```

2. Start the portal and navigate to Settings → Active Directory
   - Verify it shows the current AD configuration
   - Click "Test Connection" — should show green/reachable

3. Login as luke.skywalker@montanifarms.com
   - Should get Admin role
   - Navigate to Settings → Users
   - Should see shadow record with "Active Directory" badge

4. Login as han.solo@montanifarms.com
   - Should get Operator role
   - Verify limited UI access (can't access settings)

5. On DC, remove han.solo from LucidAdmin-Operators (don't add to any other group):
   ```powershell
   Remove-ADGroupMember -Identity "LucidAdmin-Operators" -Members "han.solo" -Confirm:$false
   ```

6. Logout and login as han.solo again
   - Should get Viewer role (DefaultRole)
   - Or denied if RequireRoleGroup is true

7. Navigate to Settings → Users as admin:
   - Verify "admin" account shows [Local] badge and cannot be deleted
   - Verify AD users show [Active Directory] badge
   - Verify AD user role shows as "from AD" and is not editable

## Files Created
- `admin/dotnet/scripts/Setup-LucidAdminGroups.ps1`
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/ActiveDirectory.razor`

## Files Modified
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/Users.razor`
- `admin/dotnet/src/LucidAdmin.Web/Components/Layout/NavMenu.razor` (AD settings link)
- `admin/dotnet/src/LucidAdmin.Web/Components/Layout/MainLayout.razor` (auth source indicator)

## Important Notes

- The PowerShell script should be run ONCE during initial setup. It's idempotent (checks
  for existing groups before creating).
- All AD configuration is in appsettings.json — there is no UI to CHANGE the config, only
  to VIEW and TEST it. This is intentional for MVP. Config changes require a portal restart.
- The "admin" local account is ALWAYS available regardless of AD configuration. This is the
  break-glass account and cannot be converted to an AD account.
