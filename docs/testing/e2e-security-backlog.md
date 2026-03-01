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

## BL-002: Rename default agent from "test-agent" to production-appropriate name

**Severity:** Low (cosmetic / release polish)
**Found:** 2026-02-28
**Phase:** E2E Testing — API Key Setup / Agent Pairing

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
