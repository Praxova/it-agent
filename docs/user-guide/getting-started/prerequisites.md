# Prerequisites and Planning

Before you deploy Praxova, work through this page completely. Deployments that
run into trouble almost always trace back to a prerequisite that wasn't in place —
a missing AD delegation, a firewall rule nobody knew about, or an LLM provider
decision that wasn't made until mid-install.

Thirty minutes here saves hours later.

---

## Infrastructure You Will Need

Praxova requires three servers. Two of them can be virtual machines.

### 1. Docker Host (Linux)

This is where the Admin Portal, LLM Container and IT Agent run. It does not need to be
domain-joined.

| Requirement | Minimum | Notes |
|-------------|---------|-------|
| OS | Ubuntu 22.04 LTS or later | Other Linux distros work; Ubuntu is tested |
| CPU | 4 vCPU | |
| RAM | 8 GB | 16 GB recommended if running a local LLM |
| Disk | 40 GB | More if storing local LLM models (~5–10 GB each) |
| Docker | CE 24+ with Compose plugin | See installation note below |
| GPU | NVIDIA, 8 GB+ VRAM | Required only if using a local LLM (llama.cpp) |
| Network | Can reach tool server (port 8443) and ServiceNow (port 443) | |

**Installing Docker CE:**
```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
# Log out and back in for the group change to take effect
```

### 2. Tool Server (Windows)

This is a domain-joined Windows server that executes Active Directory operations
on Praxova's behalf. It must be a member of the same domain you want Praxova
to manage.

| Requirement | Minimum | Notes |
|-------------|---------|-------|
| OS | Windows Server 2019 or later | Must be domain-joined |
| CPU | 2 vCPU | |
| RAM | 4 GB | |
| Disk | 20 GB | |
| Network | Can reach DC on port 636 (LDAPS) | Plain LDAP on port 389 is not supported |
| Network | Can reach Docker host on port 5001 | For certificate provisioning |
| .NET Runtime | Included in installer | No separate install needed |

### 3. Domain Controller

Your existing domain controller. Praxova does not require any software installed
on the DC — it connects via LDAPS (port 636) from the Tool Server.

| Requirement | Notes |
|-------------|-------|
| Windows Server 2016 or later | |
| AD DS role configured | |
| LDAPS enabled on port 636 | See LDAPS note below |

> **LDAPS must be enabled.** Plain LDAP (port 389) carries credentials in the
> clear and is not acceptable for Praxova's AD connection. Most domain controllers
> have LDAPS available automatically if they have a certificate from an AD
> Certificate Services (AD CS) CA. If you are unsure whether LDAPS is working on
> your DC, see [Verifying LDAPS](../installation/verify-ldaps.md).

---

## Active Directory Service Account

You need to create a dedicated service account for Praxova before you deploy.
This account is what the Tool Server uses to execute AD operations.

**This account should not be a Domain Admin.** Praxova uses delegated permissions
scoped to specific OUs — the minimum access required for the tasks it performs.

### Create the Account

Run this on your Domain Controller as a Domain Admin:

```powershell
New-ADUser `
  -Name "svc-praxova" `
  -SamAccountName "svc-praxova" `
  -UserPrincipalName "svc-praxova@yourdomain.com" `
  -AccountPassword (ConvertTo-SecureString "YourStrongPassword!" -AsPlainText -Force) `
  -PasswordNeverExpires $true `
  -CannotChangePassword $true `
  -Enabled $true `
  -Description "Praxova IT Agent service account — do not delete"
```

Store the password securely — you will enter it into the Admin Portal during
initial configuration.

### Delegate Permissions

After creating the account, delegate the specific permissions it needs on the
OUs that contain the users Praxova will manage.

In **Active Directory Users and Computers**, right-click the target OU and choose
**Delegate Control**. Grant `svc-praxova` the following permissions:

| Permission | Required For |
|------------|-------------|
| Reset Password | Password reset tickets |
| Read and write `pwdLastSet` | Forcing password change at next login |
| Read and write `lockoutTime` | Account unlock tickets |
| Read and write `member` (on group objects) | Group membership changes |
| Read all properties (on user objects) | User lookup and validation |

> **Scope this to your managed OUs only.** If Praxova manages helpdesk users in
> `OU=Staff,DC=yourdomain,DC=com`, delegate to that OU — not to the domain root.
> The principle of least privilege applies here.

---

## ServiceNow

You need a ServiceNow instance and an account with API access.

| Requirement | Notes |
|-------------|-------|
| ServiceNow instance | Personal Developer Instance (PDI) works for evaluation |
| Release | Rome (2021) or later | |
| API account | Must have read/write access to the `incident` table |
| Assignment group | The group Praxova will monitor — note the exact name, it is case-sensitive |

If you do not have an existing account with API access, create a dedicated
service account in ServiceNow for Praxova. Avoid using a personal account —
if that person leaves, the integration breaks.

---

## LLM Provider Decision

Praxova uses a Large Language Model to classify tickets. You need to decide
which provider to use before you start.

### Option A — Local LLM (lamma.cpp)

Runs entirely on your infrastructure. No data leaves your network.

**Requires:** An NVIDIA GPU with at least 8 GB VRAM on the Docker host.
The default model (Llama 3.1 8B) needs approximately 5 GB of VRAM and
takes 3–5 minutes to download on first start.

Best for: Organizations with data sovereignty requirements, air-gapped
environments, or existing GPU capacity.

### Option B — Cloud LLM (OpenAI, Anthropic, Azure OpenAI)

No GPU required. Ticket descriptions are sent to the provider's API for
classification. Simpler to set up; ongoing API cost.

**Requires:** An API key from your chosen provider.

Best for: Organizations without available GPU hardware, or those evaluating
Praxova before committing to local infrastructure.

> **Note on data privacy:** When using a cloud LLM, ticket descriptions are
> sent to the provider's API. Review your provider's data processing terms
> before using this option in production.

---

## Pre-Deployment Planning Checklist

Work through this checklist before starting the installation. Every unchecked
item is a potential interruption mid-deployment.

### Network Connectivity

- [ ] Docker host can reach the Tool Server on **port 8443** (HTTPS)
- [ ] Docker host can reach ServiceNow on **port 443** (HTTPS)
- [ ] Tool Server can reach the Domain Controller on **port 636** (LDAPS)
- [ ] If a firewall sits between the Docker host and Tool Server, port 8443 is open
- [ ] If using a cloud LLM, Docker host can reach the provider API on port 443

### Active Directory

- [ ] `svc-praxova` service account created
- [ ] Password for `svc-praxova` stored securely
- [ ] Delegated permissions granted on the correct OUs
- [ ] LDAPS is working on the DC (test with `Test-NetConnection dc01 -Port 636`)
- [ ] You know which OUs contain the users Praxova will manage
- [ ] You know the FQDN of your Domain Controller (e.g. `dc01.yourdomain.com`)

### ServiceNow

- [ ] ServiceNow instance URL noted (e.g. `https://yourinstance.service-now.com`)
- [ ] API account username and password available
- [ ] Assignment group name noted — exact spelling, exact case
- [ ] API account has read/write access to the `incident` table

### LLM Provider

- [ ] Decision made: local or cloud provider
- [ ] If local: Docker host has a compatible NVIDIA GPU with 8 GB+ VRAM
- [ ] If cloud: API key obtained and available

### Credentials and Passphrases

- [ ] You have chosen a strong unseal passphrase (32+ characters)
  — This protects all credentials stored in Praxova. Treat it like a root password.
- [ ] Unseal passphrase is stored in a password manager or secure vault
  — If this is lost and you have no recovery key, stored credentials cannot be recovered.

---

## What You Will Configure During Installation

For reference, here is the information you will enter into the Admin Portal
during the initial setup steps. Gathering it now prevents interruptions later.

| Item | Where You Enter It | Example |
|------|--------------------|---------|
| ServiceNow instance URL | Service Accounts | `https://dev12345.service-now.com` |
| ServiceNow username | Service Accounts | `praxova-svc` |
| ServiceNow password | Service Accounts | — |
| ServiceNow assignment group | Agent configuration | `Help Desk` |
| AD domain | Service Accounts | `yourdomain.com` |
| AD domain controller hostname | Service Accounts | `dc01.yourdomain.com` |
| AD service account username | Service Accounts | `svc-praxova` |
| AD service account password | Service Accounts | — |
| LLM provider type | Service Accounts | `llama.cpp` / `OpenAI` / `Anthropic` |
| LLM API key (cloud only) | Service Accounts | — |
| Tool Server hostname | Tool Servers | `toolserver01.yourdomain.com` |

---

## Ready to Install

If all items above are checked, you are ready to begin.

→ [Installation: Docker Host Setup](../installation/docker-host.md)
