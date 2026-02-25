# TD-007 Authentication & Hardening — Test Plan

**Date:** 2026-02-09
**Scope:** TD-007A (local hardening), TD-007B (AD/LDAP), TD-007C (settings UI)
**Tester:** Alton

---

## Prerequisites

### Environment

- [x] Portal workstation (Ubuntu) can reach DC at 172.16.119.20:389
- [x] `libldap-2.5-0` installed: `dpkg -l | grep libldap`
  - If missing: `sudo apt install libldap-2.5-0`
- [x] Confirm portal builds clean:
  ```bash
  cd ~/Documents/lucid-it-agent/admin/dotnet
  dotnet build
  ```
  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

### DC Setup (run once on 172.16.119.20)

- [x] Run the setup script:
  ```powershell
  cd \\path\to\lucid-it-agent\admin\dotnet\scripts
  .\Setup-LucidAdminGroups.ps1 -TestUsers @{
      Admins = @("luke.skywalker")
      Operators = @("han.solo")
      Viewers = @("leia.organa")
  }
  ```
- [x] Verify groups exist: `Get-ADGroup -Filter 'Name -like "LucidAdmin-*"' | Select Name`
- [x] Verify membership:
  ```powershell
  Get-ADGroupMember "LucidAdmin-Admins" | Select SamAccountName
  Get-ADGroupMember "LucidAdmin-Operators" | Select SamAccountName
  Get-ADGroupMember "LucidAdmin-Viewers" | Select SamAccountName
  ```

### Fresh Database

Delete the existing DB to test from scratch. This is critical — seeding behavior
only triggers on a missing admin user or default password.

```bash
rm -f ~/Documents/lucid-it-agent/admin/dotnet/src/LucidAdmin.Web/lucid-admin.db
```

---

## Phase 1: TD-007A — Local Account Hardening

### Start portal with AD disabled (default)

```bash
cd ~/Documents/lucid-it-agent/admin/dotnet/src/LucidAdmin.Web
dotnet run
```

Wait for startup log: `Default admin user created — password change required on first login`

---

### T1.1 — First Login Forces Password Change

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Navigate to `http://localhost:5000` | Redirected to `/login` | [x] |
| 2 | Verify login page does NOT show "Default credentials: admin/admin" | Should show generic "First-time login?" hint instead | [x] |
| 3 | Login with `admin` / `admin` | Redirected to `/change-password` (NOT home) | [x] |
| 4 | Verify forced change banner shows | "You must change your default password before continuing" in yellow/warning | [x ] |
| 5 | Verify no Cancel button visible | Only the Change Password button, no way to skip | [x] |

### T1.2 — Route Guard Enforcement

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | While on `/change-password`, manually navigate to `/` | Redirected back to `/change-password` | [x] |
| 2 | Try `/service-accounts` | Redirected back to `/change-password` | [x] |
| 3 | Try `/audit-log` | Redirected back to `/change-password` | [x] |
| 4 | Try `/settings/users` | Redirected back to `/change-password` | [x] |
| 5 | Try `/account/logout` | Should be allowed (logout works) | [x] |

### T1.3 — Password Policy Enforcement

Login again as `admin`/`admin` (you'll be back at change-password).

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Current: `admin`, New: `test`, Confirm: `test` → Submit | Error: "at least 12 characters" | [x] |
| 2 | Current: `admin`, New: `testpassword1`, Confirm: `testpassword1` → Submit | Error: "uppercase letter" | [x] |
| 3 | Current: `admin`, New: `Testpassword1`, Confirm: `Testpassword1` → Submit | Error: "special character" | [x] |
| 4 | Current: `admin`, New: `admin`, Confirm: `admin` → Submit | Error: "cannot be 'admin'" | [x] | -> Actually got 'new passowrd must be different'
| 5 | Current: `admin`, New: `Admin12345!!`, Confirm: `Admin12345!!` → Submit | Error: "cannot be the same as your username" (case-insensitive) | [ ] | -> THIS WAS ALLOWED
| 6 | Current: `admin`, New: `LucidAdmin2026!`, Confirm: `LucidAdmin2025!` → Submit | Error: "do not match" | [x] |
| 7 | Current: `wrongpassword`, New: `LucidAdmin2026!`, Confirm: `LucidAdmin2026!` → Submit | Error: "Current password is incorrect" | [x] |
| 8 | Current: `admin`, New: `LucidAdmin2026!`, Confirm: `LucidAdmin2026!` → Submit | Error: "must be different from current password" | [x] |

**Note for T1.3.5:** Username is "admin" so "Admin12345!!" matches case-insensitively.

### T1.4 — Successful Password Change

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Current: `admin`, New: `LucidAdmin2026!`, Confirm: `LucidAdmin2026!` → Submit | Redirected to `/` (home/dashboard) | [x] |
| 2 | Verify dashboard loads normally | All nav items accessible | [x] |
| 3 | Navigate to Audit Log (`/audit-log`) | See `PasswordChanged` event for user `admin` | [ ] | <- ISSUE - NOT IMPLEMENTED YET
| 4 | Log out | Redirected to `/login` | [X] |

**Note:** If T1.3.8 passed (same-password rejection), change T1.4 to use a different new password
like `LucidAdmin2026!` for step 1 (since `admin` → `LucidAdmin2026!` is current→new, not same).

### T1.5 — Second Login with New Password

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login with `admin` / `LucidAdmin2026!` | Redirected to `/` directly (no forced change) | [X] |
| 2 | Login with `admin` / `admin` (old password) | Error: "Invalid username or password" | [X] |

### T1.6 — Voluntary Password Change

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | While logged in, click "Change Password" in nav menu | Navigate to `/change-password` | [x] |
| 2 | Verify Cancel button IS visible (not forced) | Cancel button present, links to `/` | [x] |
| 3 | Verify forced-change banner is NOT shown | Just "Update your password" subtitle | [x] |
| 4 | Click Cancel | Returns to home | [x] |

### T1.7 — Admin Account Protection

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Navigate to Settings → Users | User list visible | [x] |
| 2 | Find "admin" user row | Should show [Local] badge | [x] |
| 3 | Attempt to delete admin user (if delete button exists) | Should be blocked with error | [x] |
| 4 | Attempt to disable admin user (if toggle exists) | Should be blocked with error | [x] |

### T1.8 — Retroactive Force (Existing DB Scenario)

This tests the Program.cs logic that detects an existing admin with default password.

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Stop the portal | — | [ ] |
| 2 | Use SQLite to check state: | — | [ ] |
| | `sqlite3 lucid-admin.db "SELECT Username, MustChangePassword FROM Users WHERE Username='admin';"` | Should show `admin|0` (already changed) | [ ] |
| 3 | Start the portal again | Portal starts normally (no retroactive trigger since password was changed) | [ ] |

**Note:** The retroactive check only fires if `VerifyPassword("admin", hash)` returns true.
Since we changed the password, this won't trigger. This is a safety net for upgrade scenarios
where an existing installation still has the default password.

---

## Phase 2: TD-007B — Active Directory Authentication

### Enable AD

Edit `appsettings.json` (or create `appsettings.Development.json`) at
`~/Documents/lucid-it-agent/admin/dotnet/src/LucidAdmin.Web/`:

```json
{
  "ActiveDirectory": {
    "Enabled": true,
    "Domain": "montanifarms.com",
    "LdapServer": "172.16.119.20",
    "LdapPort": 389,
    "UseLdaps": false,
    "SearchBase": "DC=montanifarms,DC=com",
    "RoleMapping": {
      "AdminGroup": "LucidAdmin-Admins",
      "OperatorGroup": "LucidAdmin-Operators",
      "ViewerGroup": "LucidAdmin-Viewers"
    },
    "DefaultRole": "Viewer",
    "RequireRoleGroup": false
  }
}
```

Restart the portal after this change.

---

### T2.1 — AD Connectivity Test (API)

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login as `admin` / `LucidAdmin2026!` (local) | Success | [x] |
| 2 | Navigate to Settings → Active Directory | Page loads | [x] |
| 3 | Click "Test Connection" button | Green success alert: "Connected to 172.16.119.20 (montanifarms.com) in Xms" | [x] |

### T2.2 — Login Page AD Awareness

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Log out, view login page | Should show "You can sign in with your domain credentials (user@montanifarms.com)" | [x] |
| 2 | Verify the "First-time login?" message is NOT shown when AD is enabled | AD hint replaces it | [x] |

### T2.3 — AD User Login (UPN Format)

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login with `luke.skywalker@montanifarms.com` / (AD password) | Success, redirected to home | [x] |
| 2 | Check role displayed in UI | Should be **Admin** (member of LucidAdmin-Admins) | [x] |
| 3 | Check audit log | Login event with `method: "ActiveDirectory"` | [ ] |  <- ISSUE - NOT IMPLEMENTED YET

### T2.4 — AD User Login (Plain Username)

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Log out | — | [x] |
| 2 | Login with `han.solo` / (AD password) | Success | [x] |
| 3 | Check role | Should be **Operator** (member of LucidAdmin-Operators) | [x] |

### T2.5 — AD User Login (DOMAIN\user Format)

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Log out | — | [x] |
| 2 | Login with `MONTANIFARMS\leia.organa` / (AD password) | Success | [x] |
| 3 | Check role | Should be **Viewer** (member of LucidAdmin-Viewers) | [x] |

### T2.6 — Shadow User Records

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login as admin (local), navigate to Settings → Users | User list loads | [x] |
| 2 | Find `luke.skywalker` | Shows [Active Directory] badge, role = Admin | [x] |
| 3 | Find `han.solo` | Shows [Active Directory] badge, role = Operator | [x] |
| 4 | Find `leia.organa` | Shows [Active Directory] badge, role = Viewer | [x] |
| 5 | Find `admin` | Shows [Local] badge, role = Admin | [x] |

### T2.7 — AD User Restrictions

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | On Users page, check AD user row | Role should NOT be editable (managed in AD) | [x] |
| 2 | Verify no password reset/change option for AD users | Managed in AD | [x] |
| 3 | Login as `luke.skywalker`, go to `/change-password` | Error or block: "Active Directory users must change their password through AD" | [x] |

### T2.8 — Break-Glass: "admin" Always Local

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login with `admin` / `LucidAdmin2026!` | Success via LOCAL auth (not AD) | [x] |
| 2 | Check audit log for this login | `method: "Local"` even though AD is enabled | [ ] |   <- ISSUE - NOT IMPLEMENTED YET

### T2.9 — AD Bad Credentials

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login with `luke.skywalker@montanifarms.com` / `wrongpassword` | Error: "Invalid username or password" | [x] |
| 2 | Login with `nonexistent.user@montanifarms.com` / `anypassword` | Error: "Invalid username or password" | [x] |

### T2.10 — AD User Without Role Group (DefaultRole)

Requires a test AD user NOT in any LucidAdmin group (e.g., `chewbacca` or any other existing user).

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Identify an AD user not in any LucidAdmin group | — | [x] |
| 2 | Login with that user / (AD password) | Success (RequireRoleGroup=false) | [x] |
| 3 | Check role | Should be **Viewer** (DefaultRole) | [x] |

### T2.11 — RequireRoleGroup = true

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Stop portal | — | [x] |
| 2 | Edit appsettings: set `"RequireRoleGroup": true` | — | [x] |
| 3 | Restart portal | — | [x] |
| 4 | Login with user from T2.10 (no LucidAdmin group) | Error: "not authorized...contact your administrator" | [x] |
| 5 | Login with `luke.skywalker` / (AD password) | Success (has LucidAdmin-Admins group) | [x] |
| 6 | **Cleanup:** Reset `RequireRoleGroup` to `false` and restart | — | [x] |

### T2.12 — AD Unreachable Fallback

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Block DC access: `sudo iptables -A OUTPUT -d 172.16.119.20 -j DROP` | — | [x] | -> I just shut down the DC
| 2 | Login with `luke.skywalker@montanifarms.com` / (AD password) | Error: "Active Directory is unavailable" | [x] | -> The error is just unknown username/password - regardless of if it is right or wrong - same as I have gotten on other systems when AD is down.
| 3 | Login with `admin` / `LucidAdmin2026!` | **Success** — local break-glass still works | [x] |
| 4 | Navigate to Settings → Active Directory → Test Connection | `reachable: false` | [x] |
| 5 | **Cleanup:** `sudo iptables -D OUTPUT -d 172.16.119.20 -j DROP` | — | [x] |
| 6 | Verify AD login works again: `luke.skywalker@montanifarms.com` | Success | [x] |

### T2.13 — AD Role Change on Re-Login

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | On DC, move han.solo from Operators to Admins: | — | [x] |
| | `Remove-ADGroupMember -Identity "LucidAdmin-Operators" -Members "han.solo" -Confirm:$false` |x| |
| | `Add-ADGroupMember -Identity "LucidAdmin-Admins" -Members "han.solo"` |x| |
| 2 | Login as `han.solo` | Success | [x] |
| 3 | Check role | Should now be **Admin** (updated from AD groups) | [x] |
| 4 | Check Users page — han.solo's role | Updated to Admin | [x] |
| 5 | **Cleanup:** move han.solo back: | — | [x] |
| | `Remove-ADGroupMember -Identity "LucidAdmin-Admins" -Members "han.solo" -Confirm:$false` |x| |
| | `Add-ADGroupMember -Identity "LucidAdmin-Operators" -Members "han.solo"` |x| |

---

## Phase 3: TD-007C — Settings UI & User Management

### T3.1 — AD Settings Page (AD Enabled)

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login as admin, navigate to Settings → Active Directory | Page loads | [x] |
| 2 | Verify green "enabled" banner | "Active Directory authentication is enabled for domain montanifarms.com" | [x] |
| 3 | Verify Connection Settings card | Shows 172.16.119.20, port 389, search base, LDAPS=No | [x] |
| 4 | Verify Role Mapping card | LucidAdmin-Admins→Admin (red), Operators→Operator (warning), Viewers→Viewer (blue) | [x] |
| 5 | Click "Test Connection" button | Green alert with latency | [x] |
| 6 | Verify "Role Refresh Behavior" section | Explains next-login behavior | [x] |

### T3.2 — AD Settings Page (AD Disabled)

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Stop portal, set `"Enabled": false`, restart | — | [x] |
| 2 | Navigate to Settings → Active Directory | Page loads | [x] |
| 3 | Verify "disabled" banner | "Active Directory authentication is disabled" | [x] |
| 4 | Verify "Test Connection" not active | Greyed out or hidden with explanation text | [x] |
| 5 | Verify "How to Enable" instructions section | 4-step guide with appsettings.json reference | [x] |
| 6 | **Cleanup:** Re-enable AD and restart | — | [x] |

### T3.3 — Users Page Features

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Navigate to Settings → Users | Page loads with user table | [x] |
| 2 | Verify columns visible | Username, Email, Role (badge), Auth Source (badge), Last Login, Status | [x] |
| 3 | Use search box: type "luke" | Filters to luke.skywalker | [x] |
| 4 | Use Auth Source filter: select "Active Directory" | Shows only AD users | [x] |
| 5 | Use Auth Source filter: select "Local" | Shows only local users | [x] |
| 6 | Clear filters | Shows all users | [x] |

### T3.4 — Nav Menu

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Check Settings group in nav menu | Contains "Users" and "Active Directory" links | [x] |
| 2 | Check "Change Password" link | Visible in nav menu (outside Settings group) | [x] |

---

## Phase 4: API Authentication (JWT Path)

### T4.1 — JWT Local Auth

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | ```curl -s -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d '{"username":"admin","password":"LucidAdmin2026!"}'``` | 200 with JWT token | [ ] |
| 2 | Decode token (jwt.io or `jq`) | role=Admin, method=Local | [ ] |

### T4.2 — JWT AD Auth

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | ```curl -s -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d '{"username":"luke.skywalker@montanifarms.com","password":"<AD_PASSWORD>"}'``` | 200 with JWT token | [x] |
| 2 | Decode token | role=Admin, method=ActiveDirectory | [ ] | -> I am not sure how to decode the token

### T4.3 — JWT Bad Credentials

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | ```curl -s -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d '{"username":"admin","password":"wrong"}'``` | 401 | [x] |

---

## Phase 5: Edge Cases & Security

### T5.1 — LDAP Injection Prevention

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login with username: `luke*)(objectClass=*` / password: `anything` | Auth failure, no crash | [x] |
| 2 | Login with username: `luke.skywalker)(|(password=*` / password: `anything` | Auth failure, clean error | [x] |

### T5.2 — Concurrent Sessions

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Login as admin in Chrome | Success | [x] |
| 2 | Login as admin in Firefox (second browser) | Both sessions active | [x] |
| 3 | Change password in Chrome | Success | [x] |
| 4 | Refresh page in Firefox | Still works (existing cookie valid) | [x] |

### T5.3 — Empty/Null Input Handling

| Step | Action | Expected Result | Pass |
|------|--------|-----------------|------|
| 1 | Submit login form with empty username and password | Error message, no crash | [x] |
| 2 | Submit change-password form with all empty fields | Error, no crash | [x] |x

---

## Results Summary

| Phase | Tests | Passed | Failed | Blocked |
|-------|-------|--------|--------|---------|
| Phase 1: Local Hardening | 29 | | | |
| Phase 2: AD Authentication | 33 | | | |
| Phase 3: Settings UI | 14 | | | |
| Phase 4: JWT API | 5 | | | |
| Phase 5: Edge Cases | 6 | | | |
| **Total** | **87** | | | |

## Known Limitations (Document, Don't Fix)

- Role changes in AD take effect on next login, not mid-session (8-hour cookie)
- AD config changes require portal restart (loaded from appsettings.json at startup)
- No manual "Sync AD User" button yet — shadow users created on first AD login
- Password policy is server-side only — no real-time client-side validation while typing

## Post-Test Cleanup

```bash
# Restore iptables if T2.12 cleanup was missed
sudo iptables -D OUTPUT -d 172.16.119.20 -j DROP 2>/dev/null

# Restore RequireRoleGroup to false if T2.11 cleanup was missed
# Edit appsettings.json → RequireRoleGroup: false → restart portal

# Restore han.solo to Operators if T2.13 cleanup was missed (on DC)
# Remove-ADGroupMember -Identity "LucidAdmin-Admins" -Members "han.solo" -Confirm:$false
# Add-ADGroupMember -Identity "LucidAdmin-Operators" -Members "han.solo"
```
