# TD-007A: Local Account Hardening — Force Password Change + Policy

## Context

The Admin Portal currently seeds a default `admin/admin` user in `Program.cs` (~line 266).
Authentication flows through `AccountController.cs` (cookie auth for Blazor UI) and
`AuthEndpoints.cs` (JWT auth for API). The `Argon2PasswordHasher` is already wired up and
working. MudBlazor is the UI framework. The `User` entity is in `LucidAdmin.Core/Entities/User.cs`.

**Goal:** Force the default admin to change their password on first login. Add password policy
enforcement. Ensure the local admin account can never be deleted or disabled (break-glass).

## Requirements

### 1. Add `MustChangePassword` flag to User entity

**File:** `admin/dotnet/src/LucidAdmin.Core/Entities/User.cs`

Add:
```csharp
public bool MustChangePassword { get; set; } = false;
```

### 2. Update the admin user seed

**File:** `admin/dotnet/src/LucidAdmin.Web/Program.cs` (~line 266-275)

When creating the default admin user, set `MustChangePassword = true`.

Also add logic: if the admin user already exists BUT still has the default password hash
(i.e., `passwordHasher.VerifyPassword("admin", adminUser.PasswordHash)` returns true),
set `MustChangePassword = true` and save. This handles the case where an existing DB
is migrated — the flag gets set retroactively.

### 3. Create EF Core migration

After modifying User.cs, generate a migration:
```bash
cd admin/dotnet
dotnet ef migrations add AddMustChangePassword \
    --project src/LucidAdmin.Infrastructure \
    --startup-project src/LucidAdmin.Web \
    --output-dir Data/Migrations
```

### 4. Intercept login to enforce password change

**File:** `admin/dotnet/src/LucidAdmin.Web/Controllers/AccountController.cs`

In the `Login` method, after successful authentication (after `HttpContext.SignInAsync`),
check if `user.MustChangePassword == true`. If so, redirect to `/change-password` instead
of the return URL or home page.

Add a `MustChangePassword` claim to the claims list so the Blazor UI can also check it:
```csharp
new Claim("MustChangePassword", user.MustChangePassword.ToString())
```

### 5. Create a Change Password page

**New file:** `admin/dotnet/src/LucidAdmin.Web/Components/Pages/ChangePassword.razor`

This page should:
- Use `@layout LoginLayout` (same minimal layout as login page, no nav menu)
- Require authentication (`@attribute [Authorize]`)
- Show differently based on whether this is a forced change (first login) or voluntary:
  - If `MustChangePassword` claim is "True": show a banner "You must change your default
    password before continuing" and hide any cancel/back button
  - If voluntary (user navigated here from settings): show normal change password form
    with cancel option
- Form fields: Current Password, New Password, Confirm New Password
- Submit via an API endpoint (see step 6)
- On success: redirect to "/" (home)
- Use MudBlazor components consistent with Login.razor styling

**Password Policy (enforce on client AND server):**
- Minimum 12 characters
- At least one uppercase letter
- At least one lowercase letter
- At least one digit
- At least one special character (!@#$%^&*()_+-=[]{}|;':\",./<>?)
- Cannot be "admin" or the username
- Show real-time validation feedback using MudBlazor validation

### 6. Create Change Password API endpoint

**File:** `admin/dotnet/src/LucidAdmin.Web/Controllers/AccountController.cs`

Add a new POST endpoint at `account/change-password` (NOT under /api/auth — this is for
the Blazor cookie-auth flow):

```csharp
[HttpPost("change-password")]
[Authorize]
public async Task<IActionResult> ChangePassword(
    [FromForm] string currentPassword,
    [FromForm] string newPassword,
    [FromForm] string confirmPassword)
```

Logic:
1. Get current user from claims (`ClaimTypes.NameIdentifier`)
2. Verify `currentPassword` against stored hash
3. Validate `newPassword` matches `confirmPassword`
4. Enforce password policy (same rules as client-side)
5. Verify new password != current password
6. Hash new password with `_passwordHasher.HashPassword(newPassword)`
7. Update user record: set new PasswordHash, set `MustChangePassword = false`
8. Log an audit event (`AuditAction.PasswordChanged` — add to enum if needed)
9. Re-sign-in the user (refresh the cookie with updated claims, MustChangePassword=false)
10. Redirect to "/"

On failure, redirect back to `/change-password?error=<message>`.

### 7. Add PasswordChanged audit action

**File:** `admin/dotnet/src/LucidAdmin.Core/Enums/AuditAction.cs`

Add `PasswordChanged` to the enum if it doesn't already exist.

### 8. Add route guard for MustChangePassword

**File:** `admin/dotnet/src/LucidAdmin.Web/Components/Layout/MainLayout.razor`

In the main layout (NOT LoginLayout), add logic that checks the `MustChangePassword` claim.
If it's "True" and the current URI is NOT `/change-password` and NOT `/account/logout`,
redirect to `/change-password`. This prevents the user from navigating anywhere in the
portal without changing their password first.

Use `NavigationManager` and `AuthenticationStateProvider` to check this.

### 9. Protect the admin account from deletion/disable

**File:** `admin/dotnet/src/LucidAdmin.Web/Endpoints/AuthEndpoints.cs` (or wherever user
management is handled)

If there's any endpoint or UI that can delete or disable users, add a guard:
- The user with Username == "admin" cannot be deleted
- The user with Username == "admin" cannot have IsEnabled set to false
- Return a clear error message: "The built-in administrator account cannot be deleted or disabled."

### 10. Update Login.razor

**File:** `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Login.razor`

Remove the `<MudAlert>` block at the bottom that shows "Default credentials: admin/admin".
This is a security risk. If you want to keep a hint for first-time setup, change it to:
"First-time login? Use the default credentials configured during installation."
(No actual credentials shown.)

### 11. Add "Change Password" link to navigation

**File:** `admin/dotnet/src/LucidAdmin.Web/Components/Layout/NavMenu.razor`

Add a "Change Password" link somewhere accessible (e.g., in a user menu or settings section)
so users can voluntarily change their password after initial setup.

## Verification Steps

1. Delete the existing `lucid-admin.db` to force a fresh seed
2. Start the portal: `cd admin/dotnet/src/LucidAdmin.Web && dotnet run`
3. Navigate to http://localhost:5000
4. Login with admin/admin
5. Verify you are redirected to /change-password (NOT home)
6. Verify you cannot navigate to any other page while MustChangePassword is active
7. Try submitting a weak password (e.g., "test") — should be rejected with policy message
8. Try submitting "admin" as new password — should be rejected
9. Submit a valid new password (e.g., "LucidAdmin2026!")
10. Verify redirect to home page
11. Log out and log back in with new password — should go directly to home (no forced change)
12. Verify the audit log has a PasswordChanged event

## Files Modified
- `admin/dotnet/src/LucidAdmin.Core/Entities/User.cs`
- `admin/dotnet/src/LucidAdmin.Core/Enums/AuditAction.cs`
- `admin/dotnet/src/LucidAdmin.Web/Program.cs`
- `admin/dotnet/src/LucidAdmin.Web/Controllers/AccountController.cs`
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Login.razor`
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/ChangePassword.razor` (NEW)
- `admin/dotnet/src/LucidAdmin.Web/Components/Layout/MainLayout.razor`
- `admin/dotnet/src/LucidAdmin.Web/Components/Layout/NavMenu.razor`
- `admin/dotnet/src/LucidAdmin.Infrastructure/Data/Migrations/` (auto-generated)

## Do NOT modify
- `Argon2PasswordHasher.cs` — it's working correctly
- `IPasswordHasher.cs` — interface is sufficient
- `AuthEndpoints.cs` — that's the JWT API path, not the Blazor cookie flow (unless adding delete guard)
