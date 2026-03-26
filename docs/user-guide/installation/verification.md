# End-to-End Verification

This page walks you through confirming that your Praxova installation is
working correctly from end to end — from a ticket arriving in ServiceNow
to a resolved action in Active Directory and a closed ticket.

Work through these checks in order. Each one builds on the previous. If
a check fails, the troubleshooting note points you to the right place
before you continue.

**Before you start:** Both the Docker stack and Tool Server must be fully
deployed and configured. Complete [Docker Host Setup](docker-host.md) and
[Tool Server Installation](tool-server.md) first.

---

## Check 1 — All Components Are Healthy

Confirm every component is running and reachable.

```bash
# Container status — all should show "running"
docker compose ps

# Admin Portal
curl -s http://localhost:5000/api/health/ | jq
# Expected: { "status": "Healthy", "sealed": false }

# LLM Server (local LLM only — skip if using a cloud provider)
curl -sk https://localhost:8443/health | jq
# Expected: { "status": "ok" }
```

From the Tool Server machine:

```powershell
# Tool Server
Invoke-RestMethod -Uri "https://toolserver01.yourdomain.com:8443/api/v1/health"
# Expected: { "status": "Healthy", "adConnected": true }
```

**If `adConnected` is `false`:** Stop here and work through
[Verifying LDAPS](verify-ldaps.md) before continuing. The rest of this
verification requires a working AD connection.

---

## Check 2 — Agent Is Connected and Polling

Watch the agent logs to confirm it has loaded its configuration from the
portal and is actively polling ServiceNow:

```bash
docker compose logs --tail=30 agent-helpdesk-01
```

Look for lines similar to these near the end of the output:

```
Agent 'helpdesk-agent' configuration loaded
  LLM: llm-ollama (Local LLM)
  ServiceNow: servicenow-basic
Running in daemon mode. Polling every 30s.
```

If you see authentication errors or "portal unavailable" messages, confirm
the `LUCID_API_KEY` in your `.env` file matches the key you created in the
portal, then restart the agent:

```bash
docker compose restart agent-helpdesk-01
```

---

## Check 3 — Workflows Are Loaded

Open the Admin Portal and navigate to **Workflows**. You should see the
default workflows that were seeded on first startup:

| Workflow | Type |
|----------|------|
| `it-helpdesk-dispatcher` | Dispatcher |
| `password-reset-sub` | Sub-workflow |
| `group-membership-sub` | Sub-workflow |
| `file-permissions-sub` | Sub-workflow |

If these are missing, the workflow seeder did not run. Restart the portal
to trigger it:

```bash
docker compose restart admin-portal
docker compose logs -f admin-portal
# Watch for: "Workflow seeder: X workflows created"
```

---

## Check 4 — Process a Test Ticket

This is the definitive test. You will create a real ticket in ServiceNow
and watch Praxova classify and resolve it.

### 4.1 Create the Test Ticket

In your ServiceNow instance, create a new Incident:

- **Assignment group:** your monitored group (exact name, exact case)
- **Caller:** a user that exists in your Active Directory
- **Short description:** `Please reset my password`
- **Description:** `I've been locked out of my account. My username is [ad-username].`

Replace `[ad-username]` with the SAM account name of a real user in the
OU you delegated to `svc-praxova`.

### 4.2 Watch the Agent Process It

```bash
docker compose logs -f agent-helpdesk-01
```

Within one poll cycle (up to 30 seconds), you should see the ticket picked
up and classified. The log output will look roughly like this:

```
Polling ServiceNow assignment group: Help Desk
New ticket found: INC0001234 — Please reset my password
Classifying ticket INC0001234...
  Classification: password_reset (confidence: 0.94)
Routing to sub-workflow: password-reset-sub
  Validating user: jdoe — found in AD
  Executing: ad-password-reset for jdoe
  Result: success
Notifying ServiceNow: INC0001234 resolved
```

### 4.3 Confirm the Result in ServiceNow

Open the ticket in ServiceNow. It should now be in **Resolved** state with
a work note from Praxova describing what was done, including the temporary
password if a reset was performed.

### 4.4 Confirm the Action in Active Directory

On a machine with AD tools, verify the password was actually reset:

```powershell
# The account's password last set date should be recent
Get-ADUser jdoe -Properties PasswordLastSet | Select Name, PasswordLastSet
```

---

## Check 5 — Verify an Approval Gate

Group membership changes require human approval by default. This check
confirms the approval workflow is functioning.

### 5.1 Create a Group Membership Ticket

In ServiceNow, create another Incident:

- **Assignment group:** your monitored group
- **Caller:** the same or a different AD user
- **Short description:** `Please add me to the VPN Users group`
- **Description:** `I need access to VPN. Please add my account [ad-username]
  to the VPN Users security group.`

### 5.2 Confirm the Approval Request Appears

Within one poll cycle, the ticket should be classified and an approval
request created. In the Admin Portal, navigate to **Approvals**. You should
see a pending approval entry describing the proposed group membership change.

The agent will not execute the change until you act on this approval.

### 5.3 Approve It

Click **Approve** on the pending entry. Within the next poll cycle, the agent
will detect the approval decision, execute the group membership change, update
the ServiceNow ticket, and close it.

Watch the agent log to confirm:

```bash
docker compose logs -f agent-helpdesk-01
# Look for: "Approval INC0001235 approved — resuming workflow"
```

---

## Check 6 — Verify the Audit Log

Navigate to **Audit Log** in the Admin Portal. You should see entries for
both tickets processed above, showing:

- The ticket number and classification
- Each workflow step executed
- The AD operation performed and its result
- Timestamp and agent identifier for every action

The audit log is your record of everything Praxova has done. If a ticket
resolution is ever questioned, this is where you look.

---

## Check 7 — Confirm Escalation Works

The last thing to verify is that tickets Praxova cannot handle are
escalated cleanly — not silently dropped or incorrectly resolved.

Create one more ticket:

- **Short description:** `My Outlook calendar is not syncing`
- **Description:** `My calendar hasn't synced since yesterday. Nothing appears
  on my mobile device.`

This is not a ticket type Praxova v1.0 handles. Within one poll cycle it
should be classified with low confidence or as `unknown`, and escalated.

In ServiceNow, the ticket should receive a work note explaining that it has
been routed to the helpdesk team for manual handling. The ticket should
**not** be resolved — it should remain open and assigned to your team.

Confirm in the Admin Portal **Audit Log** that the escalation is recorded.

---

## Verification Complete

If all seven checks passed, your Praxova installation is working correctly:

- ✅ All components healthy and connected
- ✅ Agent polling and authenticated
- ✅ Workflows loaded
- ✅ End-to-end ticket resolution working
- ✅ Human approval gates functioning
- ✅ Audit log capturing all activity
- ✅ Out-of-scope tickets escalating cleanly

Your installation is ready for production use.

---

## What to Do If a Check Fails

| Symptom | Where to Look |
|---------|---------------|
| Component not healthy | [Docker Host Setup](docker-host.md) — check logs for errors |
| Agent not polling | Confirm `LUCID_API_KEY` in `.env`, restart agent |
| Workflows missing | Restart portal, watch for seeder log messages |
| Ticket not picked up | Confirm assignment group name matches exactly — case-sensitive |
| Classification wrong or low confidence | Normal during initial deployment — see [Classification and Training](../configuration/classification.md) |
| AD operation fails | Confirm `svc-praxova` delegation is correct — see [Prerequisites](../getting-started/prerequisites.md) |
| `adConnected: false` | See [Verifying LDAPS](verify-ldaps.md) |
| Approval not appearing | Confirm capability mappings are saved in the portal |
| Escalation resolves instead of escalating | Known issue TD-004 — see [Known Limitations](../reference/known-limitations.md) |

---

## Next Steps

Your system is running. The next section covers day-to-day operations —
what a healthy system looks like, how to read the audit log, and how to
improve classification accuracy over time.

→ [Daily Operations](../operations/daily-operations.md)
