# E2E Security Testing — Non-Critical Backlog

Issues discovered during end-to-end security testing that are not blockers
but should be addressed before release.

---

## BL-001: Recovery Key page shows "No recovery key to display" on first setup

**Severity:** Low (cosmetic / UX)
**Found:** 2026-02-28
**Phase:** E2E Testing — Initial Portal Setup (post-DB nuke)
**Screenshot:** `/home/alton/Pictures/Screenshots/Screenshot from 2026-02-28 12-49-33.png`

**Observed behavior:**
After nuking the database and starting fresh, navigating to
`https://10.0.0.51:5001/setup/recovery-key` displays a card with the message
"No recovery key to display." and a "GO TO DASHBOARD" link. The recovery key
*was* generated — it appeared in the portal container logs — but the UI page
did not render it.

**Expected behavior:**
1. The recovery key should display on this page during initial setup so the
   admin can copy/save it.
2. The page should include clear guidance explaining what the recovery key is
   for (break-glass / lockout recovery) and that the admin must save it
   securely — it will not be shown again.

**Workaround:**
Retrieve the recovery key from portal container logs:
```bash
docker compose logs admin-portal | grep -i "recovery"
```

**Likely cause (to investigate):**
The recovery key may be generated and logged before the Blazor page is ready
to render it, or the page may rely on a state/session value that isn't being
set during the initial setup flow. Could also be a race condition between the
setup wizard steps.

**Fix suggestions:**
- Ensure the setup flow passes the recovery key to the Razor page (query param,
  TempData, or setup wizard state).
- Add explanatory text: "This is your emergency recovery key. Save it in a
  secure location. You will need it if you are locked out of the portal and
  cannot authenticate normally. This key will not be shown again."
- Consider a "Copy to clipboard" button and a confirmation checkbox
  ("I have saved this key") before allowing navigation away.

---

## BL-002: ~~Rename default agent from "test-agent" to production-appropriate name~~ RESOLVED

**Severity:** Low (cosmetic / release polish)
**Found:** 2026-02-28  |  **Resolved:** 2026-03-13
**Phase:** E2E Testing — API Key Setup / Agent Pairing
**Resolution:** Renamed to `helpdesk-01` / "Helpdesk Dispatch Agent" in seeder, docker-compose, and docs.

**Observed behavior:**
The default agent name used throughout documentation, `.env.example`, test
scripts, and initial setup flows is `test-agent`. This appears in the portal
UI (API Keys page, agent list, audit log) and looks unprofessional for a
v1.0 release.

**Expected behavior:**
The default agent name should reflect production use. Suggested alternatives:
- `helpdesk-agent-01` (matches the container name pattern `agent-helpdesk-01`)
- `praxova-agent-01`
- `agent-01`

**Scope of change:**
- `docker-compose.yml` — container/service name and `AGENT_NAME` env var
- `.env.example` — comments referencing the agent name
- `docs/DEV-QUICKREF.md` — API key setup instructions, curl examples
- `agent/scripts/` — any test scripts referencing the name
- E2E test plan — update test steps that reference `test-agent`
- README / getting-started docs

**Note:** The agent name is user-configurable in the portal, so this is about
the *default* experience out of the box, not a hardcoded limitation.

---

## BL-003: Replace packer account with dedicated deploy/admin account on Docker host

**Severity:** Low (operational hygiene / demo readiness)
**Found:** 2026-02-28
**Phase:** E2E Testing — Agent Deployment

**Observed behavior:**
Routine operations on the Docker host (10.0.0.51) — restarting containers,
updating `.env`, checking logs — are performed via `ssh packer@10.0.0.51`.
The `packer` account is the Packer/OpenTofu provisioning account, likely with
elevated privileges intended for VM image builds, not day-to-day operations.

**Why it matters:**
- Blurs audit trail — provisioning actions and routine ops share one identity
- Overprivileged for the task — only Docker and file access are needed
- Looks unprofessional in demo/training videos
- Bad security hygiene for a security-focused product

**Recommended fix:**
- Create a dedicated `praxova-deploy` (or `deploy`) account on the Docker host
- Add it to the `docker` group for container management
- Scope file access to the Praxova project directory only
- Update all documentation and scripts to reference the new account
- Reserve `packer` for infrastructure provisioning only

---

## BL-004: Tool server AD credentials bypass portal secrets management (v1.0 blocker)

**Severity:** High (security architecture gap — must fix before release)
**Found:** 2026-02-28
**Phase:** E2E Testing — Tool Server AD Operations

**Observed behavior:**
The tool server reads AD service account credentials (`ServiceAccountUsername`,
`ServiceAccountPassword`) directly from its local `appsettings.json` file on
tool01. The password is stored in plaintext on the tool server's filesystem.

When these values are empty, the tool server falls back to process identity
(`LocalSystem` / `TOOL01$`), which gets "Access is denied" from AD because
the computer account lacks delegated permissions.

The current workaround is to manually set the credentials in appsettings.json
on tool01 and restart the service.

**Expected behavior (per ADR-006 and ADR-015):**
The portal is the single source of truth for all credentials. The tool server
should fetch AD credentials from the portal through the capability routing
system, not from a local config file. The flow should be:

```
Tool server starts up
  → Authenticates to portal (mTLS)
  → Fetches its assigned capability mappings
  → For each mapping, retrieves the associated ServiceAccount credentials
    via the portal's ISecretsService (envelope-encrypted in DB)
  → Caches credentials in memory (never persisted locally)
  → Uses credentials for AD LDAPS bind operations
```

**Why this matters:**
- Plaintext password on the filesystem violates the security architecture
- Defeats the purpose of ADR-015 envelope encryption (Argon2id + AES-256-GCM)
- Credential rotation requires manual file edits + service restart on each
  tool server instead of a single portal update
- No audit trail for credential access
- Inconsistent with how the agent fetches its credentials (via portal API)

**Architectural context:**
The `ICredentialProvider` abstraction layer was designed early (see ADR-006)
to support multiple backends:
- `DatabaseEncrypted` — current portal implementation (AES-256-GCM, working)
- `EnvironmentVariable` — legacy/dev fallback
- `HashiCorpVault` — planned for v2.0
- `AzureKeyVault` — planned for v2.0

The tool server needs to become a consumer of this abstraction via the portal
API, rather than maintaining its own local credential store.

**Implementation approach:**
1. Add a portal API endpoint for tool servers to fetch their assigned
   service account credentials (authenticated via mTLS client cert)
2. Tool server calls this endpoint at startup and caches credentials
   in memory
3. Remove `ServiceAccountPassword` from `ToolServerSettings.cs` and
   `appsettings.json`
4. Add periodic refresh so credential rotation in the portal propagates
   without tool server restart
5. Fallback: if portal is unreachable, use cached credentials from last
   successful fetch (bounded TTL)

**References:**
- ADR-006: ServiceAccount as Unified Provider Pattern
- ADR-015: Secrets Storage (envelope encryption)
- Chat: "Tool server software deployment pipeline issue" (2026-02-25)
- Chat: "Security architecture discussion and assessment" (2026-02-28)

---

## BL-005: Tool server has no automatic heartbeat to portal (v1.0 blocker)

**Severity:** High (capabilities are invisible without heartbeat)
**Found:** 2026-03-01
**Phase:** E2E Testing — Capability Routing

**Observed behavior:**
After deploying the tool server and configuring capability mappings in the portal,
the capability routing endpoint (`GET /api/capabilities/{name}/servers`) returns
an empty server list. All capability mappings show Health Status = "Unknown" in
the portal UI.

The routing endpoint filters for `HealthStatus.Healthy` by default (in
`CapabilityRoutingEndpoints.cs` → `ParseStatusFilter()`). Since the tool server
never sends a heartbeat, its status stays "Unknown" and it is never returned
as an available server for any capability.

The agent sees `No Tool Server found for capability: ad-password-reset` and
workflows fail silently — the ticket is marked "completed" without the action
actually executing.

**Workaround:**
Manually POST a heartbeat to flip the status:
```
POST /api/tool-servers/{guid}/heartbeat
{ "status": "Healthy", "capabilities": [] }
```

**Expected behavior:**
The tool server should send periodic heartbeats to the portal, similar to how
the agent sends heartbeats via `POST /api/agents/by-name/{name}/runtime/heartbeat`.

**Implementation approach:**
1. Add a background `IHostedService` in the tool server (`PortalHeartbeatService`)
2. On a configurable interval (default 60 seconds), POST to the portal:
   - Tool server ID, status, timestamp
   - AD connectivity result (from `TestConnectionAsync()`)
   - List of capabilities the tool server can handle
3. Use the same portal connection settings added in BL-004 (`Portal:Url`,
   `Portal:ToolServerId`, `Portal:ApiKey`)
4. If portal is unreachable, log a warning and retry next interval
5. On startup, send an immediate heartbeat so the tool server is discoverable
   without waiting for the first interval

**Secondary issue — silent failure path:**
When capability routing returns no servers, the workflow should escalate rather
than silently complete. This is related to TD-004 (failed workflows route to
escalation) in TECHNICAL_DEBT.md. The logs show:
```
WARNING: No transition condition matched for step sub-file-permissions
INFO: No outgoing transition from sub-file-permissions, completing
INFO: INC0010155 completed successfully after approval
```
This ticket did NOT complete successfully — it should have escalated. The
dispatcher workflow needs `outcome == 'failed'` transitions on all sub-workflow
steps.

**References:**
- `CapabilityRoutingEndpoints.cs` — `ParseStatusFilter()` defaults to Healthy
- `ToolServerEndpoints.cs` — existing heartbeat endpoint
- TD-004 in `docs/TECHNICAL_DEBT.md` — failed workflow escalation
- BL-004 — portal connection settings already added to tool server

---

## BL-005: Tool server has no automatic heartbeat to portal (v1.0 blocker)

**Severity:** High (capabilities are invisible without heartbeat)
**Found:** 2026-03-01
**Phase:** E2E Testing — Capability Routing

**Observed behavior:**
After deploying the tool server and configuring capability mappings in the portal,
the capability routing endpoint (`GET /api/capabilities/{name}/servers`) returns
an empty server list. All capability mappings show Health Status = "Unknown" in
the portal UI.

The routing endpoint filters for `HealthStatus.Healthy` by default (in
`CapabilityRoutingEndpoints.cs` → `ParseStatusFilter()`). Since the tool server
never sends a heartbeat, its status stays "Unknown" and it is never returned
as an available server for any capability.

The agent sees "No Tool Server found for capability: ad-password-reset" and
workflows fail silently — the ticket is marked "completed" without the action
actually executing.

**Workaround:**
Manually POST a heartbeat to flip the status:
```
POST /api/tool-servers/{guid}/heartbeat
{ "status": "Healthy", "capabilities": [] }
```

**Expected behavior:**
The tool server should send periodic heartbeats to the portal, similar to how
the agent sends heartbeats via `POST /api/agents/by-name/{name}/runtime/heartbeat`.

**Implementation approach:**
1. Add a background `IHostedService` in the tool server (`PortalHeartbeatService`)
2. On a configurable interval (default 60 seconds), POST to the portal:
   - Tool server ID, status, timestamp
   - AD connectivity result (from `TestConnectionAsync()`)
   - List of capabilities the tool server can handle
3. Use the same portal connection settings added in BL-004 (`Portal:Url`,
   `Portal:ToolServerId`, `Portal:ApiKey`)
4. If portal is unreachable, log a warning and retry next interval
5. On startup, send an immediate heartbeat so the tool server is discoverable
   without waiting for the first interval

**Secondary issue — silent failure path:**
When capability routing returns no servers, the workflow should escalate rather
than silently complete. This is related to TD-004 (failed workflows route to
escalation) in TECHNICAL_DEBT.md. The logs show:
```
WARNING: No transition condition matched for step sub-file-permissions
INFO: No outgoing transition from sub-file-permissions, completing
INFO: INC0010155 completed successfully after approval
```
This ticket did NOT complete successfully — it should have escalated. The
dispatcher workflow needs `outcome == 'failed'` transitions on all sub-workflow
steps.

**References:**
- `CapabilityRoutingEndpoints.cs` — `ParseStatusFilter()` defaults to Healthy
- `ToolServerEndpoints.cs` — existing heartbeat endpoint
- TD-004 in `docs/TECHNICAL_DEBT.md` — failed workflow escalation
- BL-004 — portal connection settings already added to tool server

---
