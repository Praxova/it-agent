# Active Directory Delegation

Praxova's tool server connects to Active Directory using a dedicated service
account — `svc-praxova` by convention, though you can name it anything. This
account is not a Domain Admin. It holds only the specific delegated permissions
required for the operations Praxova performs, scoped to the OUs and groups you
designate as Praxova-managed.

This page covers exactly what permissions are required, why each one exists, how
to grant them, and how to verify the delegation is correct.

---

## Why Not Domain Admin?

The simplest possible setup would be to give Praxova's service account Domain
Admin rights. It would work immediately, with no delegation configuration required.

Domain Admin is also far more access than Praxova needs, and the excess creates
real risk. Domain Admin accounts can read all AD objects, modify any user, create
and destroy OUs, and modify domain trust relationships. If Praxova's service
account were compromised, so would everything in the domain.

Delegated permissions limit that risk. If the service account is stolen, the
attacker inherits only what Praxova needs: the ability to reset passwords and
modify group memberships on specific OUs and groups. They cannot touch anything
outside those boundaries.

This is a standard principle — least privilege — and it is worth the extra
configuration time.

---

## Step 1 — Create the Service Account

In **Active Directory Users and Computers** (or via PowerShell), create a user
account in a service accounts OU. A dedicated OU keeps service accounts separate
from user accounts and makes applying Group Policy easier.

Suggested location: `OU=ServiceAccounts,DC=yourdomain,DC=com`

```powershell
New-ADUser `
  -Name "Praxova Service Account" `
  -SamAccountName "svc-praxova" `
  -UserPrincipalName "svc-praxova@yourdomain.com" `
  -Path "OU=ServiceAccounts,DC=yourdomain,DC=com" `
  -AccountPassword (Read-Host -AsSecureString "Password") `
  -PasswordNeverExpires $true `
  -CannotChangePassword $true `
  -Enabled $true
```

> **Password policy:** The service account password must meet your domain's
> complexity requirements. Choose a long, randomly generated password and store
> it in your password manager. You will enter it into the Praxova portal during
> configuration.
>
> `PasswordNeverExpires $true` prevents Praxova from losing AD access due to
> an expired service account password. If your security policy requires periodic
> rotation, you will need to update the password in the Praxova portal each time
> you rotate it in AD.

---

## Step 2 — Scope Your Managed OUs

Identify which OUs contain the users Praxova will manage. Praxova's permissions
will be delegated to these OUs — not to the entire directory.

For example, if your user accounts are in:
- `OU=Staff,DC=yourdomain,DC=com`
- `OU=Contractors,DC=yourdomain,DC=com`

You will delegate permissions to both of these OUs in the steps below.

Do not delegate to the root of the domain or to an OU containing computer
objects, administrative accounts, or service accounts. Keep the scope as narrow
as your ticket volume allows.

---

## Step 3 — Grant Delegated Permissions

The following permissions are required. Grant each one using the AD Delegation
of Control Wizard or via PowerShell. Both methods are shown below.

### Required Permissions

| Permission | Object Type | Scope | Required For |
|-----------|-------------|-------|-------------|
| Reset Password | User objects | Managed user OUs | Password reset |
| Write `pwdLastSet` | User objects | Managed user OUs | Force password change at next logon |
| Write `lockoutTime` | User objects | Managed user OUs | Account unlock |
| Read all user attributes | User objects | Managed user OUs | User existence checks, pre-validation |
| Write `member` | Group objects | Managed groups | Add/remove users from groups |

No other permissions are required for v1.0 operations.

### Method A — Delegation of Control Wizard

This is the GUI approach. Run it once per managed OU.

1. Open **Active Directory Users and Computers**
2. Right-click the target OU → **Delegate Control**
3. Click **Next**, then **Add** and select `svc-praxova`
4. On the Tasks to Delegate page, select **Create a custom task to delegate**
5. Select **Only the following objects in the folder** → check **User objects**
6. Check **Property-specific** and select the following properties:
   - `Reset Password` (under General)
   - `Read and write lockoutTime`
   - `Read and write pwdLastSet`
   - `Read all properties` (under Property-specific)
7. Complete the wizard

Repeat for each managed OU.

For group membership delegation, run the wizard again targeting the specific
groups Praxova will manage:

1. Right-click the OU containing your managed groups → **Delegate Control**
2. Select `svc-praxova`
3. Custom task → **Only the following objects** → **Group objects**
4. Property-specific → **Read and write member**

### Method B — PowerShell

For scripted or repeatable deployments, use PowerShell. This approach is easier
to audit and re-run if permissions are ever reset.

```powershell
Import-Module ActiveDirectory

$ServiceAccount = Get-ADUser -Identity "svc-praxova"
$ServiceAccountSID = $ServiceAccount.SID

# ── Target OUs ────────────────────────────────────────────────────────────────
# Add all OUs containing users that Praxova will manage
$ManagedOUs = @(
    "OU=Staff,DC=yourdomain,DC=com",
    "OU=Contractors,DC=yourdomain,DC=com"
)

# ── Common GUIDs ──────────────────────────────────────────────────────────────
# Extended rights and property GUIDs are constant across all AD deployments
$ResetPasswordGuid    = [Guid]"00299570-246d-11d0-a768-00aa006e0529"
$PwdLastSetGuid       = [Guid]"bf967a0a-0de6-11d0-a285-00aa003049e2"
$LockoutTimeGuid      = [Guid]"28630ebf-41d5-11d1-a9c1-0000f80367c1"

$SchemaGuid_User  = [Guid]"bf967aba-0de6-11d0-a285-00aa003049e2"
$SchemaGuid_Group = [Guid]"bf967a9c-0de6-11d0-a285-00aa003049e2"
$MemberGuid       = [Guid]"bf9679c0-0de6-11d0-a285-00aa003049e2"
$AllPropertiesGuid = [Guid]"00000000-0000-0000-0000-000000000000"

foreach ($OU in $ManagedOUs) {
    $ACL = Get-Acl "AD:$OU"
    $Identity = [System.Security.Principal.IdentityReference] $ServiceAccountSID

    # Reset Password (extended right)
    $ACL.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        $Identity,
        [System.DirectoryServices.ActiveDirectoryRights]::ExtendedRight,
        [System.Security.AccessControl.AccessControlType]::Allow,
        $ResetPasswordGuid,
        [System.DirectoryServices.ActiveDirectorySecurityInheritance]::Descendents,
        $SchemaGuid_User
    )))

    # Write pwdLastSet
    $ACL.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        $Identity,
        [System.DirectoryServices.ActiveDirectoryRights]::WriteProperty,
        [System.Security.AccessControl.AccessControlType]::Allow,
        $PwdLastSetGuid,
        [System.DirectoryServices.ActiveDirectorySecurityInheritance]::Descendents,
        $SchemaGuid_User
    )))

    # Write lockoutTime
    $ACL.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        $Identity,
        [System.DirectoryServices.ActiveDirectoryRights]::WriteProperty,
        [System.Security.AccessControl.AccessControlType]::Allow,
        $LockoutTimeGuid,
        [System.DirectoryServices.ActiveDirectorySecurityInheritance]::Descendents,
        $SchemaGuid_User
    )))

    # Read all properties on user objects
    $ACL.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        $Identity,
        [System.DirectoryServices.ActiveDirectoryRights]::ReadProperty,
        [System.Security.AccessControl.AccessControlType]::Allow,
        $AllPropertiesGuid,
        [System.DirectoryServices.ActiveDirectorySecurityInheritance]::Descendents,
        $SchemaGuid_User
    )))

    # Write member on group objects (for add/remove group membership)
    $ACL.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        $Identity,
        [System.DirectoryServices.ActiveDirectoryRights]::WriteProperty,
        [System.Security.AccessControl.AccessControlType]::Allow,
        $MemberGuid,
        [System.DirectoryServices.ActiveDirectorySecurityInheritance]::Descendents,
        $SchemaGuid_Group
    )))

    Set-Acl -Path "AD:$OU" -AclObject $ACL
    Write-Host "Permissions applied to: $OU"
}
```

Save this script and run it from a domain-joined machine with AD administrative
rights. Review the OU list at the top before running — keep it as narrow as
your requirements allow.

---

## Step 4 — Configure LDAPS

The tool server connects to Active Directory over LDAPS (port 636). The DC's
certificate must be trusted by the tool server.

See [Verify LDAPS](../installation/verify-ldaps.md) for the full configuration
walkthrough, including how to test the LDAPS connection and import the DC
certificate into the tool server's trust store if needed.

---

## Step 5 — Verify the Delegation

Before connecting Praxova, confirm the permissions are actually in place.

### Using ADSIEdit

1. Open **ADSIEdit** on your domain controller
2. Connect to the Default Naming Context
3. Navigate to your managed OU
4. Right-click → **Properties** → **Security** tab
5. Click **Advanced** → find `svc-praxova` in the list
6. Verify the entries match the permissions in the table above

### Using PowerShell

```powershell
$OU = "OU=Staff,DC=yourdomain,DC=com"

(Get-Acl "AD:$OU").Access |
    Where-Object { $_.IdentityReference -like "*svc-praxova*" } |
    Select-Object IdentityReference, ActiveDirectoryRights, ObjectType, InheritedObjectType |
    Format-Table -AutoSize
```

This will print the ACEs granted to `svc-praxova` on that OU. You should see
entries for the extended right (Reset Password), WriteProperty entries for
`pwdLastSet`, `lockoutTime`, and `member`, and a ReadProperty entry.

### Test the Connection from Praxova

After configuring the AD service account in the portal:

1. Navigate to **Service Accounts** → select your AD service account
2. Use the **Test Connection** button

A successful test confirms that the tool server can bind to AD using the
`svc-praxova` credentials and reach the configured domain controller over LDAPS.

---

## Troubleshooting AD Operation Failures

If Praxova is running but AD operations are failing, the most common causes are
a permission that was not granted, a permission scoped to the wrong OU, or an
LDAPS trust issue. Work through these checks in order.

### Check 1 — Confirm the service account can bind

If the connection test in the portal fails, the account credentials are wrong or
the LDAPS certificate is not trusted. Check:

- Is `svc-praxova` enabled in AD?
- Has the password expired? (`PasswordNeverExpires` should be set)
- Is the LDAPS certificate trusted by the tool server? See [Verify LDAPS](../installation/verify-ldaps.md)

### Check 2 — Confirm the OU scope is correct

Delegation is scoped. If a user is in `OU=Staff` and you delegated to
`OU=Contractors`, the operation will fail with an insufficient access error.

Run the PowerShell verification query above for each OU that contains users
showing failed operations.

### Check 3 — Check the Praxova audit log

The audit log in the portal records the exact error returned by the tool server
for each failed operation. Navigate to **Audit Log** and filter by the failing
ticket number or operation type. The error detail will tell you whether the
failure is a permissions error, a user-not-found error, or something else.

```
Access denied: svc-praxova does not have permission to reset password on CN=jsmith,OU=Finance,...
```

An error like this confirms a missing or incorrectly scoped delegation — the
user `jsmith` is in `OU=Finance`, which is not in the managed OUs list.

### Check 4 — Verify no conflicting Deny ACEs

AD Deny ACEs take precedence over Allow ACEs regardless of inheritance. If a
parent OU has an explicit Deny for `svc-praxova` on any of the required
permissions, it will override the Allow you granted on the child OU.

Check the full ACL on the affected OU with `Get-Acl` and look for any entries
with `AccessControlType = Deny` applied to `svc-praxova`.

---

## Security Maintenance

### Reviewing Permissions Periodically

AD delegations do not expire. Review `svc-praxova`'s permissions annually or
after any domain restructuring:

```powershell
# Show all permissions for svc-praxova across managed OUs
$OUs = @("OU=Staff,DC=yourdomain,DC=com", "OU=Contractors,DC=yourdomain,DC=com")

foreach ($OU in $OUs) {
    Write-Host "`nOU: $OU"
    (Get-Acl "AD:$OU").Access |
        Where-Object { $_.IdentityReference -like "*svc-praxova*" } |
        Select-Object ActiveDirectoryRights, ObjectType, AccessControlType |
        Format-Table
}
```

Compare the output against the permissions table at the top of this page. Any
permission not in that table should be investigated.

### Rotating the Service Account Password

If you rotate `svc-praxova`'s password in AD, you must update it in the Praxova
portal before restarting the tool server or the next AD operation will fail:

1. In AD, change the `svc-praxova` password
2. In the Praxova portal, navigate to **Service Accounts** → select the AD account
3. Update the password field and save
4. Use **Test Connection** to verify the new credential works

The portal stores the updated password encrypted immediately. No restart is required.
