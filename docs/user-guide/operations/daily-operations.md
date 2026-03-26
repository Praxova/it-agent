# Daily Operations

This page describes what a healthy Praxova system looks like, what to check
regularly, and how to interpret what you see in the Admin Portal.

Once Praxova is deployed and verified, day-to-day involvement is light. The
system runs unattended and surfaces the things that need your attention —
pending approvals, escalated tickets, and occasional anomalies in the audit log.

---

## What a Healthy System Looks Like

### The Admin Portal Dashboard

Log into the Admin Portal and check these indicators:

| Indicator | Healthy State |
|-----------|--------------|
| Agent status | `Online` with a recent heartbeat timestamp |
| Tool Server status | `Online` with a recent heartbeat timestamp |
| Pending Approvals | Any number — these are waiting for your action |
| Recent Audit Events | Activity matching your ticket volume |
| Sealed indicator | Not shown — if the portal shows "Sealed", see below |

The agent sends a heartbeat to the portal every 30 seconds. If the last
heartbeat is more than a few minutes old, the agent has likely stopped or
lost connectivity.

### Container Health (from the Docker host)

```bash
# All containers should show "running" and healthy
docker compose ps

# Quick health checks
curl -s http://localhost:5000/api/health/ | jq
# Expected: { "status": "Healthy", "sealed": false }
```

---

## Your Daily Routine

### Check the Approvals Queue

Navigate to **Approvals** in the Admin Portal. Any pending approvals are
waiting for your decision before the agent can proceed. Tickets do not
time out — they wait indefinitely — but the end user is waiting too.

For each pending approval, review:
- What action is proposed (the **Proposed Action** field)
- Which user and resource are affected
- Which ticket requested it (linked ServiceNow number)

Click **Approve** or **Reject**. If you reject, add a note explaining why —
it will be included in the ServiceNow ticket update.

### Scan the Audit Log

Navigate to **Audit Log**. A quick daily scan catches anything unexpected
before it becomes a problem.

Look for:

**Unexpected resolutions** — A ticket marked resolved that you don't recognize,
or a ticket resolved without a corresponding approval for an action that should
require one. This can indicate a workflow configuration issue.

> **Known limitation:** In the current release, if a sub-workflow fails and
> the dispatcher has no failure path configured, the ticket may be incorrectly
> marked resolved instead of escalated. See
> [Known Limitations](../reference/known-limitations.md) for details.

**Repeated escalations on the same ticket type** — If the same category of
ticket keeps escalating rather than resolving, the agent may not have the
correct AD permissions for that operation, or the classification may be
routing incorrectly.

**Failed AD operations** — Look for audit entries with `result: failed` on
ToolCall steps. These indicate the Tool Server reached the DC but the
operation was rejected — usually an AD delegation issue.

### Check Escalated Tickets

Any ticket the agent escalated appears in your ServiceNow queue with a work
note explaining why. Review these regularly — a pattern of escalations on a
specific ticket type often means a new workflow is needed, or the classification
examples need tuning.

---

## Managing the Agent

### Start and Stop

```bash
# Stop the agent (safe — ServiceNow holds tickets statefully,
# nothing is lost, in-progress approvals remain pending)
docker stop praxova-agent-helpdesk-01

# Start the agent
docker start praxova-agent-helpdesk-01

# Restart (picks up .env changes and new portal configuration)
docker restart praxova-agent-helpdesk-01
```

### Watch Live Agent Activity

```bash
docker compose logs -f agent-helpdesk-01
```

Each ticket processed produces a log block showing classification,
routing decision, steps executed, and outcome. This is the most useful
view when investigating a specific ticket.

### Pause the Agent Without Stopping It

If you need to prevent new tickets from being processed temporarily
(for example, during a maintenance window on Active Directory), stop
the agent container. Any tickets already in flight and awaiting approval
will resume when you restart it.

---

## Improving Classification Over Time

Praxova gets more accurate the longer it runs in your environment, but only
if you actively feed it corrections. This is the classification improvement
loop — it is the most valuable operational habit you can build.

### How It Works

Every ticket the agent classifies gets a confidence score. High-confidence
classifications (above your configured threshold) are acted on immediately.
Low-confidence ones are routed to human review so they don't guess wrong.

Over time, your ticket vocabulary — your group names, your internal system
names, the way your users phrase requests — gets encoded into the example
sets that guide the classifier. By month three, Praxova should be noticeably
better at your specific environment than it was on day one.

### What You Do

When you review an escalated ticket and the work note says the agent was
uncertain about the classification:

1. Note what the ticket was actually asking for
2. Navigate to **Workflows → Example Sets** in the Admin Portal
3. Find the example set for the relevant ticket type
4. Add the ticket's short description and correct classification as a new example

The classifier uses these examples on every subsequent ticket. A dozen good
examples per ticket type makes a meaningful difference.

### What to Add as Examples

Good examples are short descriptions that are representative of how your
users actually phrase requests — not textbook definitions.

If your users say "I'm locked out again" instead of "please unlock my account",
add "I'm locked out again" as a password reset or account unlock example.
If your company has a system called "Orion" and users request access to it by
name, add that phrasing as a group access example.

Generic examples are already seeded at install time. Org-specific examples
are what make the difference.

---

## Portal Maintenance

### The Unseal Passphrase and Portal Restarts

The Admin Portal requires the unseal passphrase to decrypt stored credentials
every time it starts. In production, this is handled automatically via the
`/etc/praxova/unseal.env` file (see
[Securing the Unseal Passphrase](../security/secrets-management.md)).

If the portal restarts and shows `"sealed": true` in the health endpoint,
the passphrase was not available at startup. Check that the unseal
configuration is in place on the Docker host, then restart the portal:

```bash
curl -s http://localhost:5000/api/health/ | jq
# If sealed: true —
docker compose restart admin-portal
docker compose logs -f admin-portal
# Watch for: "Secrets store initialized and unsealed"
```

### Updating Container Images

When a new Praxova release is available:

```bash
# Pull or build the new images
docker compose pull          # if using a registry
# or
docker compose build         # if building from source

# Restart with the new images
# The portal handles database migrations automatically on startup
docker compose up -d

# Watch for successful migration
docker compose logs -f admin-portal
# Look for: "Database migration complete"
```

Your configuration, workflows, credentials, and audit history are stored in
a persistent Docker volume and survive container image updates.

---

## Next Steps

If something isn't working as expected, the troubleshooting guide covers the
most common problems and how to diagnose them.

→ [Troubleshooting](troubleshooting.md)
