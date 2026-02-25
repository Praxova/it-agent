# MCP Infrastructure Analysis for Praxova DevOps Agent

**Date**: 2026-02-16  
**Status**: Research Complete  
**Related**: DEV-INFRASTRUCTURE.md, ADR-014, ADR-015, TEST-DATA-STRATEGY.md

---

## 1. Executive Summary

The MCP ecosystem for infrastructure automation is surprisingly mature. Between
OpenTofu/Terraform MCP servers and vSphere MCP servers, approximately **80% of
what we need exists as open-source MCP servers today**. The remaining 20% falls
into post-provisioning configuration (WinRM/PowerShell execution, AD promotion,
certificate management) which requires either a custom MCP server or shell-based
tool execution through an existing general-purpose MCP.

**Bottom line**: We do NOT need to build most of this from scratch. We need to
compose existing MCP servers and fill a few specific gaps.

---

## 2. Available MCP Servers (Inventory)

### 2.1 OpenTofu Registry MCP (Official)

**Source**: https://github.com/opentofu/opentofu-mcp-server  
**Transport**: stdio or SSE (hosted at mcp.opentofu.org)  
**Language**: Node.js  
**License**: MPL-2.0

**What it does** (registry lookup ONLY):
| Tool | Description |
|------|-------------|
| `search-opentofu-registry` | Search providers, modules, resources, data sources |
| `get-provider-details` | Detailed info on a specific provider |
| `get-module-details` | Detailed info on a specific module |
| `get-resource-docs` | Documentation for a specific resource type |
| `get-datasource-docs` | Documentation for a specific data source type |

**What it does NOT do**: No plan, apply, destroy, state management. This is a
reference tool — it helps an agent look up correct HCL syntax and provider docs
while writing configurations. Think of it as "man pages for OpenTofu."

**Our use**: Agent uses this to look up vSphere provider resource syntax when
generating or modifying `.tf` files. Helpful but not essential if the agent
already has the docs in context.

---

### 2.2 HashiCorp Terraform MCP Server (Official)

**Source**: https://github.com/hashicorp/terraform-mcp-server  
**Transport**: stdio or StreamableHTTP  
**Language**: Go  
**License**: MPL-2.0  
**Status**: Beta (as of early 2026)

**What it does**:
| Toolset | Tools | Description |
|---------|-------|-------------|
| Registry | `resolveProviderDocID`, `getProviderDocs`, `searchModules`, `getModuleDetails` | Provider/module documentation lookup |
| HCP TF/TFE | Workspace CRUD, variable management, run management, variable sets | Full HCP Terraform/Enterprise workspace ops |
| Operations | `plan`, `apply`, `destroy` (disabled by default, requires `ENABLE_TF_OPERATIONS=true`) | **CLI execution** — init, plan, apply, destroy |

**Critical detail**: The operations toolset (plan/apply/destroy) exists but is
**disabled by default** and marked as destructive. Must set `ENABLE_TF_OPERATIONS=true`.
This is the closest thing to "official" infrastructure execution via MCP.

**Compatibility note**: Despite the "Terraform" name, this works with OpenTofu
since it wraps CLI execution. The registry lookup uses Terraform Registry APIs
which are compatible with OpenTofu's registry.

**Our use**: Primary IaC execution engine. Agent calls plan → reviews output →
calls apply. State inspection via show/output.

---

### 2.3 Community Terraform CLI MCP (mjrestivo16)

**Source**: https://github.com/mjrestivo16/mcp-terraform  
**Transport**: stdio  
**Language**: Node.js (TypeScript)  
**License**: MIT  
**Stars**: ~62

**What it does** (25 tools, full CLI wrapper):

| Category | Tools |
|----------|-------|
| **Core Operations** | `tf_version`, `tf_init`, `tf_validate`, `tf_plan`, `tf_apply`, `tf_destroy` |
| **State Management** | `tf_show`, `tf_state_list`, `tf_state_show`, `tf_state_pull`, `tf_state_rm`, `tf_state_mv` |
| **Workspace** | `tf_workspace_list`, `tf_workspace_new`, `tf_workspace_select`, `tf_workspace_delete` |
| **Output & Inspection** | `tf_output`, `tf_graph`, `tf_providers`, `tf_providers_lock` |
| **Import & Taint** | `tf_import`, `tf_taint`, `tf_untaint` |
| **Configuration** | `tf_fmt`, `tf_get` |
| **Refresh** | `tf_refresh` |

**Key features**:
- `auto_approve` parameter on apply/destroy (skips interactive prompt)
- `dir` parameter on every tool (point at specific working directory)
- Returns stdout/stderr from actual CLI execution
- Works with both `terraform` and `tofu` CLI (configurable)

**Our use**: More comprehensive than HashiCorp's official server for CLI ops.
State management tools are valuable for debugging. The `tf_import` tool would
help with importing existing infrastructure.

**Trade-off vs HashiCorp official**: More tools but community-maintained.
HashiCorp's is more polished but fewer CLI tools currently.

---

### 2.4 vSphere MCP Server (giuliolibrando)

**Source**: https://github.com/giuliolibrando/vmware-vsphere-mcp-server  
**Transport**: StreamableHTTP (port 8000)  
**Language**: Python (FastMCP + pyvmomi)  
**License**: Not specified (GitHub public)  
**Dockerized**: Yes

**What it does**:

| Category | Tools |
|----------|-------|
| **VM Management** | `list_vms`, `get_vm_details`, `power_on_vm`, `power_off_vm`, `restart_vm` |
| **VM Lifecycle** | `delete_vm` (with confirmation), `modify_vm_resources` (CPU/RAM) |
| **Snapshots** | `create_snapshot`, `list_snapshots`, `delete_snapshot` |
| **Templates** | `list_templates` |
| **Infrastructure** | `list_hosts`, `list_datastores`, `list_networks`, `list_datacenters` |
| **Monitoring** | `get_vm_performance`, `get_host_performance`, `list_events`, `list_alarms` |
| **Bulk Ops** | `bulk_power_operation` |
| **Reporting** | `get_environment_report`, `get_resource_utilization` |

**Safety**: Destructive operations require `confirm=True` parameter.

**What it does NOT do**: No VM creation from template (clone), no VM
customization (hostname/IP/domain), no template creation from VM. This is
primarily a monitoring and operations server, not a provisioning server.

**Our use**: Useful for VM power management, monitoring, cleanup. But it
**cannot provision new VMs** — that's OpenTofu's job via the vSphere provider.

---

### 2.5 ESXi MCP Server (bright8192)

**Source**: https://github.com/bright8192/esxi-mcp-server  
**Transport**: REST API  
**Language**: Python (pyvmomi)  
**License**: Not specified

**What it does**: Similar to giuliolibrando but simpler. VM CRUD including
**create VM** (basic — CPU, RAM, datastore, network), power operations, info.

**Limitation**: Creates blank VMs, not from templates. No Windows customization
(hostname, IP, domain join). Useful for ESXi without vCenter.

**Our use**: Limited. OpenTofu + vSphere provider does everything this does
and more.

---

### 2.6 VMware MCP Server (adrianlizman) — Most Feature-Rich

**Source**: https://github.com/adrianlizman/vmware-mcp-server  
**Transport**: MCP (stdio/SSE)  
**Language**: Python  
**License**: Not specified  
**Features**: Ollama integration, n8n workflow support

**What it does** (comprehensive):
- VM lifecycle: Create, delete, start, stop, clone, **migrate**
- Host management: Add/remove hosts, maintenance mode
- Snapshots: Full CRUD + revert
- Network: Virtual switches, port groups, VLANs
- Storage: Datastores, volumes, disk operations
- Performance monitoring, security (RBAC), audit trails

**Our use**: Most capable vSphere MCP, but likely overkill for our single-ESXi
setup. Worth monitoring if we need direct vSphere operations beyond what
OpenTofu provides.

---

### 2.7 Multi-vCenter MCP (scym)

**Source**: https://lobehub.com/mcp/scym-vmware-mcp  
**Transport**: MCP  
**Language**: Python (FastMCP + pyvmomi SOAP API)

Supports multiple vCenter connections. Enterprise-focused. Overkill for us.

---

## 3. Our Requirements (from DEV-INFRASTRUCTURE.md)

Map every operation the DevOps agent needs to perform:

### Phase 1: ESXi Setup (Manual — not MCP-automated)

| # | Operation | MCP Available? | Server |
|---|-----------|---------------|--------|
| 1.1 | Install ESXi on hardware | ❌ No | Physical install, not automatable |
| 1.2 | Configure management network | ❌ No | ESXi console, one-time setup |
| 1.3 | Configure vSwitch + port groups | ⚠️ Partial | OpenTofu vSphere provider can manage, but initial bootstrap is manual |
| 1.4 | Upload ISO to datastore | ❌ No | Manual or SCP/datastore browser |

**Verdict**: Phase 1 is a one-time manual bootstrap. Not worth automating.

---

### Phase 2: VM Template Creation

| # | Operation | MCP Available? | Server | Notes |
|---|-----------|---------------|--------|-------|
| 2.1 | Create blank VM on ESXi | ✅ Yes | OpenTofu `vsphere_virtual_machine` | Or vSphere MCP `create_vm` |
| 2.2 | Attach Windows ISO + boot | ⚠️ Partial | OpenTofu can attach ISO via `cdrom` block | Needs `vsphere_file` to upload ISO first |
| 2.3 | Windows OS install | ❌ No | Requires unattend.xml + manual or Packer | **GAP**: Interactive install, needs autounattend |
| 2.4 | Install VMware Tools | ❌ No | Part of OS install process | Include in autounattend or post-install |
| 2.5 | Windows Update | ❌ No | Post-install, needs WinRM/PowerShell | **GAP**: No WinRM MCP server |
| 2.6 | Sysprep | ❌ No | PowerShell command via WinRM | **GAP** |
| 2.7 | Convert VM to template | ⚠️ Partial | OpenTofu can't directly, vSphere API can | **GAP**: Need `mark_as_template` tool |

**Verdict**: Template creation is the biggest gap. Two approaches:
1. **Packer** (recommended): Automates ISO → installed OS → template pipeline.
   No MCP server exists for Packer, but an agent can execute `packer build`
   via shell.
2. **Manual once**: Create template by hand, let OpenTofu clone from it.
   This is what DEV-INFRASTRUCTURE.md already assumes.

---

### Phase 3: VM Provisioning via OpenTofu

| # | Operation | MCP Available? | Server | Notes |
|---|-----------|---------------|--------|-------|
| 3.1 | Write/modify .tf files | ✅ Yes | Filesystem MCP + OpenTofu Registry MCP | Agent writes HCL with docs lookup |
| 3.2 | `tofu init` | ✅ Yes | Community TF MCP (`tf_init`) or HashiCorp MCP | Full CLI wrapper |
| 3.3 | `tofu plan` | ✅ Yes | Community TF MCP (`tf_plan`) or HashiCorp MCP | Returns plan output for review |
| 3.4 | `tofu apply` | ✅ Yes | Community TF MCP (`tf_apply`) | With `auto_approve` option |
| 3.5 | `tofu destroy` | ✅ Yes | Community TF MCP (`tf_destroy`) | With `auto_approve` option |
| 3.6 | `tofu show` (state) | ✅ Yes | Community TF MCP (`tf_show`) | Inspect current state |
| 3.7 | `tofu output` | ✅ Yes | Community TF MCP (`tf_output`) | Read output values (IPs, etc.) |
| 3.8 | `tofu state list` | ✅ Yes | Community TF MCP (`tf_state_list`) | Debug state issues |
| 3.9 | `tofu validate` | ✅ Yes | Community TF MCP (`tf_validate`) | Pre-flight check |
| 3.10 | `tofu import` | ✅ Yes | Community TF MCP (`tf_import`) | Import existing resources |

**Verdict**: ✅ Fully covered. The community TF MCP server wraps all CLI ops.
Configure it to use `tofu` binary instead of `terraform`.

---

### Phase 4: Post-Provisioning (DC Build)

| # | Operation | MCP Available? | Server | Notes |
|---|-----------|---------------|--------|-------|
| 4.1 | Wait for VM to boot + respond | ⚠️ Partial | vSphere MCP can check power state | Need WinRM connectivity check |
| 4.2 | Run `promote-dc.ps1` (AD DS) | ❌ No | **GAP**: No WinRM MCP server | Need remote PowerShell execution |
| 4.3 | Wait for reboot after promotion | ⚠️ Partial | vSphere MCP power state check | Need to detect DC services ready |
| 4.4 | Run `install-adcs.ps1` (AD CS) | ❌ No | **GAP**: Same — WinRM needed | Remote PowerShell |
| 4.5 | Run `setup-test-env.ps1` | ❌ No | **GAP**: Same | Remote PowerShell |
| 4.6 | Run `export-ca-cert.ps1` | ❌ No | **GAP**: Same | Remote PowerShell |
| 4.7 | Copy CA cert to workstation | ❌ No | **GAP**: SCP/SMB file transfer | Need file retrieval from Windows |
| 4.8 | Update container trust store | ✅ Yes | Filesystem MCP / shell | Copy cert, update Docker config |
| 4.9 | Verify LDAPS connectivity | ⚠️ Partial | Shell (`openssl s_client`) | Agent can run via shell MCP |

**Verdict**: Post-provisioning is the primary gap. Every operation that runs
a PowerShell script on the Windows VM requires remote execution capability.

---

### Phase 5: Docker Stack Management

| # | Operation | MCP Available? | Server | Notes |
|---|-----------|---------------|--------|-------|
| 5.1 | `docker compose up -d` | ✅ Yes | Shell/Desktop Commander MCP | CLI wrapper |
| 5.2 | `docker compose down` | ✅ Yes | Shell MCP | CLI wrapper |
| 5.3 | `docker compose build` | ✅ Yes | Shell MCP | CLI wrapper |
| 5.4 | `docker compose logs` | ✅ Yes | Shell MCP | CLI wrapper |
| 5.5 | Health checks | ✅ Yes | Shell MCP + HTTP fetch | curl endpoints |

**Verdict**: ✅ Fully covered via shell execution. No specialized Docker MCP
needed for compose operations.

---

## 4. Gap Analysis Summary

### Gaps That Need Filling

| Gap ID | Description | Severity | Options |
|--------|-------------|----------|---------|
| **GAP-1** | **WinRM/PowerShell remote execution** | 🔴 Critical | Build custom MCP server, or use Ansible MCP, or shell `Invoke-Command` via pwsh |
| **GAP-2** | **VM template creation from ISO** | 🟡 Medium | Use Packer (shell execution), or create template manually once |
| **GAP-3** | **File transfer from Windows VM** | 🟡 Medium | SMB mount + copy, or WinRM file retrieval, or PowerShell `Copy-Item` over PSSession |
| **GAP-4** | **Windows VM boot/ready detection** | 🟢 Low | Poll WinRM port 5985/5986 via shell, or use OpenTofu provisioner wait |
| **GAP-5** | **Convert VM to template** | 🟢 Low | One-time manual step, or use govc CLI, or vSphere API call |

### What's Fully Covered

| Capability | MCP Server(s) |
|------------|---------------|
| OpenTofu plan/apply/destroy/state | Community TF MCP (mjrestivo16) |
| OpenTofu provider/resource docs | OpenTofu Registry MCP (official) |
| vSphere VM power management | vSphere MCP (giuliolibrando or adrianlizman) |
| vSphere monitoring & reporting | vSphere MCP (giuliolibrando) |
| VM snapshots | vSphere MCP (giuliolibrando) |
| File system operations | Desktop Commander / Filesystem MCP |
| Docker Compose management | Shell execution via any shell MCP |
| Git operations | GitHub MCP (already in your Agora setup) |

---

## 5. Recommended MCP Stack for DevOps Agent

### Tier 1: Essential (use immediately)

```
DevOps Agent MCP Servers:
├── opentofu-cli          # Community TF MCP (mjrestivo16) — configured for `tofu` binary
│                         # Provides: init, plan, apply, destroy, state, import, etc.
├── opentofu-registry     # Official OpenTofu MCP — SSE at mcp.opentofu.org
│                         # Provides: provider docs, resource syntax lookup
├── vsphere-mgmt          # vSphere MCP (giuliolibrando) — Docker, connects to ESXi
│                         # Provides: VM power ops, monitoring, snapshots, reporting
├── filesystem             # Already available — read/write .tf files, certs, configs
├── desktop-commander      # Already available — shell execution for docker, openssl, etc.
└── github                 # Already available — git ops for infrastructure repo
```

### Tier 2: Fill the Gaps

```
Custom / Additional:
├── winrm-exec            # CUSTOM BUILD — PowerShell remote execution over WinRM
│                         # Tools: run_script, copy_file, test_connection
│                         # This is the one MCP we definitely need to build
│
└── (optional) packer     # Shell wrapper for `packer build` — only if automating
                          # template creation. Lower priority since template is
                          # created once.
```

---

## 6. Solving GAP-1: WinRM Remote Execution

This is the critical gap. Three approaches, ranked:

### Option A: Custom WinRM MCP Server (Recommended)

Build a small FastMCP Python server using `pywinrm` library.

```
Tools:
├── winrm_test_connection(host, username, password, use_ssl)
│   → Returns: {reachable: bool, os_version: str}
│
├── winrm_run_powershell(host, username, password, script, use_ssl, timeout)
│   → Returns: {exit_code: int, stdout: str, stderr: str}
│
├── winrm_run_script_file(host, username, password, script_path, use_ssl, timeout)
│   → Returns: {exit_code: int, stdout: str, stderr: str}
│
├── winrm_copy_file_to(host, username, password, local_path, remote_path)
│   → Returns: {success: bool, bytes_transferred: int}
│
├── winrm_copy_file_from(host, username, password, remote_path, local_path)
│   → Returns: {success: bool, bytes_transferred: int}
│
└── winrm_wait_for_ready(host, username, password, timeout_seconds, poll_interval)
    → Returns: {ready: bool, wait_time: int, os_info: str}
```

**Effort**: ~1-2 days. pywinrm is mature. FastMCP boilerplate is minimal.
Registers in Agora MCP registry alongside other Praxova MCP servers.

### Option B: PowerShell Core on Linux + Shell MCP

Use `pwsh` (PowerShell Core) installed on the workstation. Agent executes
`Invoke-Command -ComputerName DC01 -ScriptBlock {...}` via Desktop Commander.

**Pros**: No custom MCP needed.  
**Cons**: Requires pwsh installed, PSRemoting configured, less structured
output, harder for agent to parse results.

### Option C: Ansible MCP Server

An Ansible MCP server exists in the community. Could use Ansible's `win_shell`
and `win_copy` modules.

**Pros**: Battle-tested Windows remote execution.  
**Cons**: Heavy dependency (Ansible + inventory + playbooks), overkill for
running 4-5 scripts.

**Recommendation**: Option A. Small, focused, registers cleanly in Agora,
matches our MCP-first architecture.

---

## 7. Complete DevOps Agent Workflow (with MCP mapping)

```
PHASE 2: Template Creation (one-time, semi-manual)
═══════════════════════════════════════════════════
Human: Creates VM, installs Windows, syspresp, converts to template
Agent: Can assist with verification via vSphere MCP
  └─ vsphere-mgmt.list_templates() → verify template exists

PHASE 3: Provision DC01
═══════════════════════
Agent Step 1: Write OpenTofu configuration
  ├─ opentofu-registry.get-resource-docs("vsphere_virtual_machine")
  ├─ filesystem.write_file("infrastructure/opentofu/dc01.tf", content)
  └─ opentofu-cli.tf_validate(dir="infrastructure/opentofu")

Agent Step 2: Plan and apply
  ├─ opentofu-cli.tf_init(dir="infrastructure/opentofu")
  ├─ opentofu-cli.tf_plan(dir="infrastructure/opentofu")
  │   └─ Agent reviews plan output, confirms correctness
  ├─ opentofu-cli.tf_apply(dir="infrastructure/opentofu", auto_approve=true)
  └─ opentofu-cli.tf_output(dir="infrastructure/opentofu")
      └─ Returns: dc01_ip = "172.16.119.20"

Agent Step 3: Wait for VM ready
  ├─ vsphere-mgmt.get_vm_details("DC01") → confirm powered on
  └─ winrm-exec.winrm_wait_for_ready(host="172.16.119.20", timeout=300)

PHASE 4: Post-Provisioning
══════════════════════════
Agent Step 4: Promote to Domain Controller
  ├─ winrm-exec.winrm_run_script_file(
  │     host="172.16.119.20",
  │     script_path="infrastructure/opentofu/scripts/promote-dc.ps1"
  │   )
  └─ winrm-exec.winrm_wait_for_ready(host="172.16.119.20", timeout=600)
      # Longer timeout — DC promotion triggers reboot

Agent Step 5: Install AD Certificate Services
  ├─ winrm-exec.winrm_run_script_file(
  │     host="172.16.119.20",
  │     script_path="infrastructure/opentofu/scripts/install-adcs.ps1"
  │   )
  └─ winrm-exec.winrm_run_powershell(
        host="172.16.119.20",
        script="Test-NetConnection -ComputerName localhost -Port 636"
      )
      # Verify LDAPS is listening

Agent Step 6: Create test environment
  └─ winrm-exec.winrm_run_script_file(
        host="172.16.119.20",
        script_path="infrastructure/opentofu/scripts/setup-test-env.ps1"
      )

Agent Step 7: Export and distribute CA certificate
  ├─ winrm-exec.winrm_run_script_file(
  │     host="172.16.119.20",
  │     script_path="infrastructure/opentofu/scripts/export-ca-cert.ps1"
  │   )
  ├─ winrm-exec.winrm_copy_file_from(
  │     host="172.16.119.20",
  │     remote_path="C:\\temp\\montanifarms-ca.crt",
  │     local_path="/home/alton/Documents/lucid-it-agent/docker/certs/montanifarms-ca.crt"
  │   )
  └─ filesystem.write_file(... update docker-compose to mount cert ...)

PHASE 5: Rebuild Praxova Stack
══════════════════════════════
Agent Step 8: Rebuild containers with new CA trust
  ├─ desktop-commander.start_process("docker compose build --no-cache")
  ├─ desktop-commander.start_process("docker compose up -d")
  └─ desktop-commander.start_process("docker compose ps")  # verify healthy

Agent Step 9: Validate end-to-end
  ├─ desktop-commander.start_process("curl -k https://localhost:5000/health")
  ├─ winrm-exec.winrm_run_powershell(
  │     host="172.16.119.20",
  │     script="Get-ADUser -Filter * -SearchBase 'OU=TestUsers,DC=montanifarms,DC=com' | Measure"
  │   )
  └─ Agent reports: "Infrastructure ready. DC01 online, LDAPS verified,
     X test users created, Praxova stack healthy."
```

---

## 8. MCP Server Configuration for Agora Registry

```yaml
# Agora MCP Registry entries for DevOps agent

# --- Tier 1: Existing/Available ---

opentofu-cli:
  name: "OpenTofu CLI Operations"
  command: "node"
  args: ["/opt/mcp-servers/mcp-terraform/dist/index.js"]
  env:
    TERRAFORM_WORKING_DIR: "/home/alton/Documents/lucid-it-agent/infrastructure/opentofu"
    # Note: Configure to use `tofu` binary — may need fork or env var
  description: "OpenTofu/Terraform CLI operations: init, plan, apply, destroy, state management"
  transport: stdio

opentofu-registry:
  name: "OpenTofu Registry Lookup"
  transport: sse
  endpoint: "https://mcp.opentofu.org/sse"
  description: "Provider docs, module search, resource syntax for OpenTofu configurations"

vsphere-mgmt:
  name: "vSphere Management"
  command: "docker"
  args: ["compose", "-f", "/opt/mcp-servers/vsphere-mcp/docker-compose.yml", "up"]
  env:
    VCENTER_HOST: "172.16.119.10"  # ESXi host IP
    VCENTER_USER: "root"
    VCENTER_PASSWORD: "${ESXI_PASSWORD}"
    INSECURE: "True"
  description: "VM power ops, monitoring, snapshots, infrastructure reporting"
  transport: streamable-http
  endpoint: "http://localhost:8000/mcp"

# --- Tier 2: Custom Build ---

winrm-exec:
  name: "WinRM Remote Execution"
  command: "python"
  args: ["-m", "praxova_winrm_mcp.server"]
  env:
    DEFAULT_HOST: "172.16.119.20"
    USE_SSL: "true"
    # Credentials via secrets management (ADR-015)
  description: "PowerShell remote execution, file transfer, connectivity testing over WinRM"
  transport: stdio
```

---

## 9. Key References (for indexing)

### MCP Servers to Evaluate/Install

| ID | Name | URL | Priority |
|----|------|-----|----------|
| REF-MCP-01 | OpenTofu Registry MCP (official) | https://github.com/opentofu/opentofu-mcp-server | Medium |
| REF-MCP-02 | HashiCorp Terraform MCP (official) | https://github.com/hashicorp/terraform-mcp-server | Medium |
| REF-MCP-03 | Community TF CLI MCP (mjrestivo16) | https://github.com/mjrestivo16/mcp-terraform | **High** |
| REF-MCP-04 | vSphere MCP (giuliolibrando) | https://github.com/giuliolibrando/vmware-vsphere-mcp-server | **High** |
| REF-MCP-05 | vSphere MCP (adrianlizman) | https://github.com/adrianlizman/vmware-mcp-server | Low (reference) |
| REF-MCP-06 | ESXi MCP (bright8192) | https://github.com/bright8192/esxi-mcp-server | Low |
| REF-MCP-07 | Multi-vCenter MCP (scym) | https://lobehub.com/mcp/scym-vmware-mcp | Low |
| REF-MCP-08 | tfmcp (Rust, nwiizo) | https://github.com/nwiizo/tfmcp | Low (reference) |
| REF-MCP-09 | AWS Terraform MCP (awslabs) | https://awslabs.github.io/mcp/servers/terraform-mcp-server | Low (AWS-specific) |

### Libraries for Custom MCP Build

| ID | Name | URL | Purpose |
|----|------|-----|---------|
| REF-LIB-01 | pywinrm | https://github.com/diyan/pywinrm | Python WinRM client for GAP-1 |
| REF-LIB-02 | FastMCP | https://github.com/jlowin/fastmcp | Python MCP server framework |
| REF-LIB-03 | pyvmomi | https://github.com/vmware/pyvmomi | vSphere Python SDK (used by vSphere MCPs) |
| REF-LIB-04 | Packer | https://www.packer.io/ | VM template automation (GAP-2) |

### Documentation

| ID | Name | URL | Purpose |
|----|------|-----|---------|
| REF-DOC-01 | vSphere Provider (OpenTofu) | https://registry.terraform.io/providers/hashicorp/vsphere/latest/docs | HCL resource reference |
| REF-DOC-02 | MCP Specification | https://modelcontextprotocol.io/ | Protocol reference |
| REF-DOC-03 | WinRM Configuration | https://learn.microsoft.com/en-us/windows/win32/winrm/installation-and-configuration-for-windows-remote-management | Windows side setup |
| REF-DOC-04 | Packer vSphere Builder | https://developer.hashicorp.com/packer/integrations/hashicorp/vsphere | Template automation |

---

## 10. Build Priority & Effort

| Priority | Item | Effort | Depends On |
|----------|------|--------|------------|
| 1 | Install + configure Community TF CLI MCP | 2 hours | OpenTofu installed on workstation |
| 2 | Install + configure vSphere MCP (Docker) | 1 hour | ESXi accessible from workstation |
| 3 | **Build WinRM MCP server** | 1-2 days | pywinrm, FastMCP, WinRM enabled on Windows VMs |
| 4 | Register all in Agora MCP registry | 1 hour | Items 1-3 complete |
| 5 | Write OpenTofu configs (.tf files) | 2-4 hours | Template exists on ESXi |
| 6 | Write/update post-provision PowerShell scripts | 2-4 hours | Existing scripts as baseline |
| 7 | Test full end-to-end agent workflow | 2-4 hours | Items 1-6 complete |
| 8 | (Optional) Packer template automation | 4-8 hours | If automating template creation |

**Total estimated effort**: ~3-5 days for the full agent-driven infrastructure pipeline.

---

## 11. Open Questions

1. **OpenTofu vs Terraform binary**: The community TF CLI MCP likely calls
   `terraform` by default. Need to verify it can be configured to call `tofu`
   instead, or fork/patch.

2. **ESXi free license API access**: The vSphere provider and vSphere MCP
   servers use the vSphere API. Free ESXi license has read-only API. Need
   VMUG Advantage ($200/yr) or similar for full API access, OR use the
   direct ESXi host client approach that some providers support.

3. **WinRM authentication**: Basic auth over HTTP (insecure but easy for lab)
   vs NTLM/Kerberos over HTTPS (production-grade). For lab, basic auth with
   `AllowUnencrypted` is fastest to get working.

4. **State backend**: Local `.tfstate` file vs remote (S3-compatible, Consul).
   For single-developer lab, local is fine. Commit state to git or back up.

5. **Should we fork mjrestivo16/mcp-terraform?**: To add OpenTofu-specific
   support and any customizations, or contribute upstream?
