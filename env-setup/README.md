# Environment Setup Scripts

This directory contains scripts to bootstrap development and test environments.

## Directory Structure

```
env-setup/
├── README.md           # This file
├── servicenow/         # ServiceNow PDI setup
│   ├── setup_pdi.py    # Main setup script
│   └── import/         # Update sets to import
└── dc/                 # Domain Controller setup
    ├── Setup-TestEnvironment.ps1   # Master setup script
    ├── Create-MockUsers.ps1        # Create test users
    ├── Create-MockGroups.ps1       # Create test groups
    └── Create-MockShares.ps1       # Create test file shares
```

## ServiceNow PDI Setup

### Prerequisites

1. Create a ServiceNow Personal Developer Instance (PDI)
   - Go to https://developer.servicenow.com/
   - Sign up / Sign in
   - Request a PDI instance
   - Note your instance URL (e.g., dev12345.service-now.com)

2. Get your admin credentials (shown on PDI page)

### Running Setup

```bash
cd env-setup/servicenow

# Set credentials (or use .env file)
export SERVICENOW_INSTANCE=dev12345
export SERVICENOW_USERNAME=admin
export SERVICENOW_PASSWORD=your_password

# Run setup
python setup_pdi.py
```

### What It Creates

- API user with appropriate roles
- Assignment group for the agent
- Escalation group for humans
- Test incidents for development

### PDI Maintenance

PDI instances are **deleted after 10 days of inactivity**. To prevent this:

1. Log in to the developer portal occasionally
2. Or enable `keep_alive` in servicenow.yaml

After a PDI is deleted, you'll need to:
1. Request a new PDI
2. Update your instance URL in configuration
3. Re-run the setup script

## Domain Controller Setup

### Prerequisites

1. Windows Server with AD DS role installed
2. Administrative access to the domain
3. PowerShell 5.1 or later

### Running Setup

Run PowerShell as Administrator on the Domain Controller:

```powershell
cd env-setup\dc

# Run master setup (creates everything)
.\Setup-TestEnvironment.ps1

# Or run individual scripts
.\Create-MockUsers.ps1
.\Create-MockGroups.ps1
.\Create-MockShares.ps1
```

### What It Creates

**Users** (in OU=LucidTest,OU=Users):
- TestUser01 through TestUser10
- Password: TempPass123! (configurable)
- Attributes set for testing

**Groups** (in OU=LucidTest,OU=Groups):
- LucidTest-ReadOnly
- LucidTest-Contributors
- LucidTest-Managers
- LucidTest-VPNUsers

**File Shares**:
- \\DC\LucidTestShare (with test folder structure)
- Various NTFS permissions for testing

**Service Account**:
- svc-lucid-agent (for tool server)
- Minimal permissions for testing

### Cleanup

To remove test data:

```powershell
.\Setup-TestEnvironment.ps1 -Cleanup
```

## Idempotency

All scripts are designed to be **idempotent** - you can run them multiple times safely:

- If objects already exist, they're skipped or updated
- No duplicate creation
- Safe to re-run after PDI reset or DC rebuild

## Troubleshooting

### ServiceNow

**"Instance not found"**
- Check your instance URL
- Ensure PDI hasn't been deleted (check developer portal)

**"Authentication failed"**
- Verify credentials
- Check if password has expired

### Domain Controller

**"Access denied"**
- Run PowerShell as Administrator
- Ensure you have Domain Admin rights

**"OU not found"**
- Scripts create OUs automatically
- Check AD structure if errors persist

## Security Notes

- Test credentials are intentionally simple
- **Never use these scripts in production**
- Service account has minimal permissions
- All test data is clearly labeled
