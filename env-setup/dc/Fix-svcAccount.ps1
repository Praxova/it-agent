# Run on DC as Administrator

# Variables
$ServiceAccount = "svc-lucid-agent"
$Domain = "montanifarms.com"
$DomainDN = "DC=montanifarms,DC=com"

# Get the service account's SID
$Account = Get-ADUser $ServiceAccount
$SID = New-Object System.Security.Principal.SecurityIdentifier($Account.SID)

# Get the Users OU (or wherever your test users are)
# Adjust this path if your users are in a different OU
$TargetOU = "OU=Lucid-IT-Test,$DomainDN"

# Get the ACL for the OU
$ACL = Get-Acl "AD:\$TargetOU"

# Create the "Reset Password" permission
# GUID for "Reset Password" extended right: 00299570-246d-11d0-a768-00aa006e0529
$ResetPasswordGuid = [GUID]"00299570-246d-11d0-a768-00aa006e0529"

$ACE = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
    $SID,
    "ExtendedRight",
    "Allow",
    $ResetPasswordGuid,
    "Descendents",
    [GUID]"bf967aba-0de6-11d0-a285-00aa003049e2"  # User object GUID
)

# Add the permission
$ACL.AddAccessRule($ACE)
Set-Acl "AD:\$TargetOU" $ACL

Write-Host "Password reset permission delegated to $ServiceAccount on $TargetOU" -ForegroundColor Green

# Verify
Get-Acl "AD:\$TargetOU" | Select-Object -ExpandProperty Access | 
    Where-Object { $_.IdentityReference -like "*$ServiceAccount*" }
