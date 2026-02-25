# TD-007 Fix 1: Password Policy — Username Contains Check

## Context
Testing TD-007A revealed that `ValidatePasswordPolicy()` in `AccountController.cs` only rejects
passwords that exactly equal the username (case-insensitive). A password like "Admin12345!!" passes
because it's not an exact match to "admin". Enterprise password policies typically reject passwords
that CONTAIN the username.

## File to Modify
`admin/dotnet/src/LucidAdmin.Web/Controllers/AccountController.cs`

## Current Code (around line 247)
```csharp
if (string.Equals(password, "admin", StringComparison.OrdinalIgnoreCase))
    return "Password cannot be 'admin'.";
if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
    return "Password cannot be the same as your username.";
```

## Required Change
Replace the exact-match checks with contains checks:

```csharp
if (password.Contains("admin", StringComparison.OrdinalIgnoreCase))
    return "Password cannot contain the word 'admin'.";
if (username.Length >= 3 && password.Contains(username, StringComparison.OrdinalIgnoreCase))
    return "Password cannot contain your username.";
```

The `username.Length >= 3` guard prevents false positives if someone has a 1-2 character username
(unlikely but defensive).

## Update Error Messages in ChangePassword.razor
The `ChangePassword.razor` page may display policy hints to the user. If there's a policy summary
section, update the hint text to say "cannot contain" instead of "cannot be".

## Verification
After the fix:
1. `dotnet build` — no errors
2. Login as admin, go to /change-password
3. Try password "Admin12345!!" → should be REJECTED with "cannot contain the word 'admin'"
4. Try password "MyAdminPass1!" → should be REJECTED (contains "admin")
5. Try password "LucidSecure2026!" → should be ACCEPTED
6. Try password "admin" → should be REJECTED (contains "admin")
