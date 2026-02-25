# Development & Demo Infrastructure Plan

**Date**: 2026-02-15  
**Status**: Planning  
**Related**: ADR-014 (Certificate Management), ADR-015 (Secrets Management)

---

## 1. Overview

Rebuild the entire Praxova development and demo infrastructure on a dedicated ESXi
host, managed by OpenTofu (infrastructure-as-code), with post-provisioning handled
by a Praxova DevOps agent. The rebuild itself will be recorded as video content.

This replaces the current VMware Workstation-based lab environment which has suffered
from stability issues (blue screens, flaky LDAPS, manual configuration drift).

---

## 2. Physical Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Physical Network (single switch)                               │
│                                                                 │
│  ┌─────────────────────┐      ┌─────────────────────────────┐  │
│  │  Alton's Workstation│      │  ESXi Host                  │  │
│  │  (Physical)         │      │  (Old workstation)          │  │
│  │                     │      │                             │  │
│  │  - Ubuntu 22.04     │      │  - ESXi 8.x                │  │
│  │  - Docker Compose   │      │  - vSwitch0 → physical NIC  │  │
│  │  - OpenTofu CLI     │      │                             │  │
│  │  - Claude Code      │      │  VMs:                       │  │
│  │  - RTX 5080 (Ollama)│      │  ├── DC01 (Win 2022)       │  │
│  │                     │      │  ├── (future: Linux srv)    │  │
│  │  Docker Stack:      │      │  ├── (future: member srv)   │  │
│  │  ├── Admin Portal   │      │  └── (future: MID server)  │  │
│  │  ├── Python Agent   │      │                             │  │
│  │  ├── Ollama         │      │                             │  │
│  │  └── Tool Server    │      │                             │  │
│  └──────────┬──────────┘      └──────────────┬──────────────┘  │
│             │                                │                  │
│             └────────── same subnet ─────────┘                  │
│                      172.16.119.0/24                             │
└─────────────────────────────────────────────────────────────────┘
```

### Key Decisions

- **Workstation stays physical**: RTX 5080 for Ollama, Docker Compose for Praxova stack
- **ESXi hosts infrastructure VMs**: Domain controller, future Linux servers, etc.
- **Single flat network**: Both machines on the same physical switch, same subnet
- **Simple vSwitch**: One vSwitch with one physical NIC, one or two port groups

### ESXi Networking

```
vSwitch0
├── Physical NIC (vmnic0) → physical switch
└── Port Groups:
    ├── "Management" — ESXi management traffic
    └── "Lab-Network" — VM traffic (172.16.119.0/24)
```

No VLANs, no distributed switches, no complexity. The workstation and all VMs
communicate on the same L2 segment.

---

## 3. OpenTofu (Infrastructure-as-Code)

### Why OpenTofu Over Terraform

- **License**: OpenTofu is MPL-2.0 (genuinely open source). Terraform switched to BSL
  in August 2023. BSL allows personal/internal use (Praxova's use case is fine with
  either), but OpenTofu is cleaner for an open-source project.
- **Compatibility**: Drop-in replacement. Same HCL syntax, same providers, same state
  format. The vSphere provider works identically.
- **Community**: Linux Foundation backed, active development, growing ecosystem.
- **Story**: "We use open source tools to build open source software" — consistent
  with Praxova's Apache 2.0 values.

### vSphere Provider

The `hashicorp/vsphere` provider manages ESXi/vCenter resources. For a standalone
ESXi host (no vCenter), it connects directly to the ESXi API. Capabilities:

- Create/destroy VMs from templates
- Configure networking (port groups, vSwitches)
- Manage datastores and disks
- VM customization (hostname, IP, domain join)
- Snapshots and clones

### MCP Integration

There is a Terraform MCP server (HashiCorp official, released early 2025) that
exposes Terraform/OpenTofu operations as MCP tools:

- `terraform_plan` — generate and review execution plans
- `terraform_apply` — apply infrastructure changes
- `terraform_show` — inspect current state
- `terraform_output` — read output values
- `terraform_validate` — check configuration syntax
- Resource documentation lookup from provider registry

This enables a Praxova DevOps agent to manage infrastructure through the same
agent architecture used for helpdesk automation.

### Configuration Structure

```
infrastructure/
├── opentofu/
│   ├── main.tf              # Provider config, backend
│   ├── variables.tf         # Input variables
│   ├── terraform.tfvars     # Variable values (git-ignored)
│   ├── dc01.tf              # Domain Controller VM
│   ├── networking.tf        # vSwitch, port groups
│   ├── templates.tf         # VM template data sources
│   ├── outputs.tf           # IP addresses, hostnames
│   └── scripts/
│       ├── promote-dc.ps1   # AD DS promotion
│       ├── install-adcs.ps1 # AD Certificate Services
│       ├── setup-test-env.ps1 # Test users, groups, OUs
│       └── configure-ldaps.ps1 # Verify LDAPS working
├── packer/                  # (optional) VM template builds
│   └── win2022-base.pkr.hcl
└── README.md
```

### Example Configuration

```hcl
# main.tf
terraform {
  required_providers {
    vsphere = {
      source  = "hashicorp/vsphere"
      version = "~> 2.6"
    }
  }
}

provider "vsphere" {
  user                 = var.vsphere_user
  password             = var.vsphere_password
  vsphere_server       = var.vsphere_server
  allow_unverified_ssl = true
}

# dc01.tf
resource "vsphere_virtual_machine" "dc01" {
  name             = "DC01"
  resource_pool_id = data.vsphere_resource_pool.pool.id
  datastore_id     = data.vsphere_datastore.datastore.id

  num_cpus = 4
  memory   = 8192
  guest_id = "windows2019srvNext_64Guest"

  network_interface {
    network_id = data.vsphere_network.lab.id
  }

  disk {
    label = "disk0"
    size  = 80
  }

  clone {
    template_id = data.vsphere_virtual_machine.win2022_template.id

    customize {
      windows_options {
        computer_name  = "DC01"
        admin_password = var.admin_password
      }

      network_interface {
        ipv4_address = "172.16.119.20"
        ipv4_netmask = 24
      }
      ipv4_gateway    = "172.16.119.1"
      dns_server_list = ["172.16.119.20", "8.8.8.8"]
    }
  }
}
```

---

## 4. DC Build: AD CS for Proper LDAPS

The single biggest improvement over the current environment: install AD Certificate
Services during DC provisioning. This eliminates the self-signed certificate problems
that blocked LDAPS in the current lab.

### What AD CS Does Automatically

When AD CS is installed as an Enterprise Root CA on a Domain Controller:

1. Creates a trusted root CA certificate
2. Auto-enrolls a Domain Controller certificate with correct EKUs:
   - Server Authentication (1.3.6.1.5.5.7.3.1)
   - Client Authentication (1.3.6.1.5.5.7.3.2)
   - Smart Card Logon (1.3.6.1.5.5.7.3.4) — bonus
   - KDC Authentication (1.3.6.1.5.5.8.10.2) — bonus
3. Configures Schannel to use the cert automatically
4. LDAPS works immediately on port 636
5. All domain-joined machines automatically trust the CA
6. Certificate auto-renewal is built in

This is the "proper" enterprise way and it looks professional in demos. No manual
cert creation, no export/import gymnastics, no Schannel debugging.

### Post-Provision Scripts

**Step 1: Promote to Domain Controller**
```powershell
# promote-dc.ps1
Install-WindowsFeature AD-Domain-Services -IncludeManagementTools

Install-ADDSForest `
    -DomainName "montanifarms.com" `
    -DomainNetbiosName "MONTANIFARMS" `
    -SafeModeAdministratorPassword (ConvertTo-SecureString "YourDSRMPassword!" -AsPlainText -Force) `
    -InstallDns:$true `
    -Force:$true

# Server reboots automatically
```

**Step 2: Install AD Certificate Services**
```powershell
# install-adcs.ps1 (run after reboot)
Install-WindowsFeature ADCS-Cert-Authority -IncludeManagementTools

Install-AdcsCertificationAuthority `
    -CAType EnterpriseRootCA `
    -CACommonName "MontaniFarms-CA" `
    -KeyLength 2048 `
    -HashAlgorithmName SHA256 `
    -ValidityPeriod Years `
    -ValidityPeriodUnits 10 `
    -Force

# Force certificate auto-enrollment
gpupdate /force
certutil -pulse

# Verify LDAPS works
Start-Sleep 10
$tcp = New-Object System.Net.Sockets.TcpClient
$tcp.Connect("localhost", 636)
$ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, {$true})
$ssl.AuthenticateAsClient($env:COMPUTERNAME + ".montanifarms.com")
Write-Host "LDAPS OK - Cert: $($ssl.RemoteCertificate.Subject)"
$ssl.Dispose()
$tcp.Dispose()
```

**Step 3: Create Test Environment**
```powershell
# setup-test-env.ps1 (existing Setup-TestEnvironment.ps1, enhanced)
# - Test OUs, users, groups
# - Service accounts (svc-praxova-agent, etc.)
# - LucidAdmin-Admins, LucidAdmin-Operators, LucidAdmin-Viewers groups
# - Test file shares with NTFS permissions
# - Password policies
```

**Step 4: Export CA Cert for Container Trust**
```powershell
# export-ca-cert.ps1
$caCert = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like "*MontaniFarms-CA*" }
Export-Certificate -Cert $caCert -FilePath "C:\temp\montanifarms-ca.cer" -Type CERT
certutil -encode "C:\temp\montanifarms-ca.cer" "C:\temp\montanifarms-ca.crt"

# This PEM file goes into docker/certs/ca-trust/ on the workstation
# All containers trust the enterprise CA → LDAPS just works
Get-Content "C:\temp\montanifarms-ca.crt"
```

---

## 5. DevOps Agent

### Concept

A new Praxova agent type that manages infrastructure provisioning and configuration.
Uses the same agent architecture (Monitor → Classify → Act → Report) as the helpdesk
agent.

### Capabilities

| Capability | Description |
|------------|-------------|
| `vm-provision` | Run OpenTofu plan/apply via MCP |
| `vm-configure` | Execute post-provision scripts via WinRM/SSH |
| `ad-promote` | Promote server to domain controller |
| `ad-install-adcs` | Install and configure AD Certificate Services |
| `ad-setup-test-env` | Create test users, groups, OUs, shares |
| `servicenow-create-ticket` | Generate test tickets for demo/testing |
| `docker-compose` | Manage Docker Compose stacks |
| `cert-export` | Export CA certs for container trust distribution |

### Integration with Existing Architecture

```
Admin Portal
├── IT Helpdesk Agent (existing)
│   └── Monitors: ServiceNow → resolves tickets
│
├── Trust Infrastructure Agent (ADR-014, ADR-015)
│   └── Monitors: Certs & secrets → rotates, renews
│
├── DevOps Agent (new)
│   ├── Monitors: Infrastructure state, git repos, CI/CD triggers
│   ├── Tools: OpenTofu MCP, WinRM, SSH, Docker
│   └── Workflows:
│       ├── "Provision Lab" — full environment from scratch
│       ├── "Create Test Tickets" — generate ServiceNow test data
│       ├── "Rebuild DC" — destroy + recreate domain controller
│       └── "Update Praxova" — pull latest, rebuild containers
│
└── (future: Compliance Agent, Onboarding Agent)
```

### DevOps Agent as Demo Content

The DevOps agent is itself a demonstration of Praxova's extensibility. It proves that
the agent architecture isn't limited to helpdesk automation — it's a general-purpose
operational automation platform.

---

## 6. Video Strategy

### Video 1: "Building the Lab from Scratch"

**Content**: Infrastructure provisioning with OpenTofu + DevOps agent  
**Duration**: ~15-20 minutes  
**Audience**: Technical decision makers, DevOps teams

Outline:
1. Show the ESXi host, explain the physical setup
2. Walk through the OpenTofu configs
3. Run `tofu plan` — show what will be created
4. Run `tofu apply` — watch the DC VM spin up
5. Show post-provisioning: AD DS promotion, AD CS install
6. Verify LDAPS works immediately (because AD CS did it right)
7. Run test environment setup script
8. Export CA cert, show container trust setup
9. End state: fully functional domain environment

Key message: "If this DC dies tomorrow, we run one command and we're back in 20 minutes."

### Video 2: "Configuring Praxova"

**Content**: Admin portal walkthrough, service accounts, tool servers, workflows  
**Duration**: ~15-20 minutes  
**Audience**: IT managers, helpdesk leads

Outline:
1. Start from the lab built in Video 1
2. Walk through the admin portal: service accounts, tool servers, capabilities
3. Show AD settings with LDAPS working (green connection test)
4. Configure workflows, show the classification training
5. Demonstrate the supervised learning loop
6. Emphasize the classification improvement loop differentiator

Key message: "Praxova learns your environment. It's not install-and-forget."

### Video 3: "Praxova in Action"

**Content**: End-to-end ticket resolution demo  
**Duration**: ~10-15 minutes  
**Audience**: Everyone — this is the hero video

Outline:
1. Create tickets (or have DevOps agent create them)
2. Watch the helpdesk agent classify, route, and resolve
3. Show password reset end-to-end
4. Show group membership change end-to-end
5. Show the audit trail, show the dashboard
6. Show low-confidence escalation
7. End with value proposition and next steps

Key message: "50% of your Level 1 tickets, resolved automatically."

### Meta-Story

The rebuild-as-content angle is powerful across all three videos. You're not just
demoing the product — you're demoing the operational philosophy:

- Video 1 proves: reproducible infrastructure, agent-based provisioning
- Video 2 proves: centralized management, security-first configuration
- Video 3 proves: the core value proposition works end-to-end

"This environment was built by agents, configured through a portal, and runs itself."

---

## 7. Migration Plan

### Phase 1: ESXi Setup (1-2 hours)

- [ ] Install ESXi on old workstation
- [ ] Configure management network
- [ ] Configure vSwitch and port groups
- [ ] Verify connectivity from workstation to ESXi management
- [ ] Upload Windows Server 2022 ISO to datastore

### Phase 2: VM Template (1-2 hours)

- [ ] Create Windows Server 2022 VM manually
- [ ] Install VMware Tools
- [ ] Windows Update
- [ ] Sysprep
- [ ] Convert to template

### Phase 3: OpenTofu Configuration (1-2 hours)

- [ ] Install OpenTofu on workstation
- [ ] Write provider and variable configs
- [ ] Write DC01 VM resource
- [ ] Write networking config
- [ ] Test `tofu plan` — verify provider can talk to ESXi
- [ ] Test `tofu apply` — create the DC VM

### Phase 4: Post-Provisioning (1-2 hours)

- [ ] Run AD DS promotion script
- [ ] Run AD CS installation script
- [ ] Verify LDAPS works locally on DC
- [ ] Run test environment setup script
- [ ] Export CA cert
- [ ] Update container trust store
- [ ] Verify LDAPS from container

### Phase 5: Praxova Stack Validation (1-2 hours)

- [ ] Update docker-compose.yml with new DC IP (if changed)
- [ ] Rebuild containers with new CA cert
- [ ] Test admin portal AD authentication
- [ ] Test admin portal LDAPS connection
- [ ] Test agent ticket processing end-to-end
- [ ] Test tool server AD operations

### Phase 6: Video Recording

- [ ] Record Video 1 (may need to `tofu destroy` and redo for clean recording)
- [ ] Record Video 2
- [ ] Record Video 3

---

## 8. Future Expansion

Once the ESXi host is running with OpenTofu management, adding new VMs is trivial:

| VM | Purpose | When |
|----|---------|------|
| DC01 | Domain Controller + AD CS | Phase 3 (immediate) |
| LINUX01 | Linux agent testing target | When Linux agent work begins |
| MEMBER01 | Member server for tool server | If tool server moves off containers |
| MID01 | ServiceNow MID server | If needed for ServiceNow integration |
| CLIENT01 | Windows 10/11 test client | For end-user perspective demos |

Each VM is a new `.tf` file and a `tofu apply`. The DevOps agent can manage the
entire fleet.

---

## 9. Open Questions

1. What are the ESXi host specs (CPU, RAM, storage)? This determines how many VMs
   we can run simultaneously.
2. Do we keep the existing DC on VMware Workstation running during the transition,
   or cut over completely?
3. Should the OpenTofu state be stored locally or in a remote backend (S3, etc.)?
4. Do we need a DHCP server on the lab network, or are all IPs static?
5. Should the DevOps agent be a separate Praxova agent type or a capability added
   to the existing agent?
6. What ESXi license? Free ESXi has limitations (no API access for some operations).
   The free tier should work for OpenTofu's vSphere provider but worth confirming.
