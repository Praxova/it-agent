# Troubleshooting

This guide is organized by symptom. Find what you are seeing and work through
the diagnosis steps in order — they are sequenced from most to least likely cause.

---

## Portal Issues

### "Sealed" status — portal won't process requests

The portal is running but the health endpoint returns `"sealed": true`.

The portal requires its unseal passphrase to decrypt stored credentials.
Without it, the portal serves the UI but cannot perform any operations that
involve credentials — agent authentication, ServiceNow connections, AD operations.

**Diagnosis:**
```bash
curl -s http://localhost:5000/api/health/ | jq
# Confirms: { "status": "Healthy", "sealed": true }
```

**Fix:**
1. Confirm `PRAXOVA_UNSEAL_PASSPHRASE` is set in `.env` (development) or
   `/etc/praxova/unseal.env` (production)
2. Confirm the passphrase matches what was used when the portal database was
   first initialized — a different passphrase will not unseal it
3. Restart the portal:
```bash
docker compose restart admin-portal
docker compose logs -f admin-portal
# Watch for: "Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE"
```

If the passphrase is correct but the portal stays sealed, check for errors
in the startup logs around the secrets store initialization.

> **If the passphrase is lost:** See [Recovery Key](../security/secrets-management.md).
> If both the passphrase and recovery key are lost, stored credentials cannot
> be recovered and will need to be re-entered.

---

### Portal UI loads but login fails

You can reach the portal login page but credentials are rejected.

- Confirm you are using the correct username (`admin` for the local account)
- Confirm the password — if you have not changed it yet, the default is `admin`
- If you previously changed the password and have forgotten it, there is no
  self-service reset for the local account. Contact your Praxova administrator
  or rebuild the portal database (this destroys all configuration)

---

## Agent Issues

### Agent shows "Offline" in the portal

The portal has not received a heartbeat from the agent recently.

**Diagnosis:**
```bash
# Is the container running?
docker compose ps

# What do the agent logs say?
docker compose logs --tail=50 agent-helpdesk-01
```

Common causes and fixes:

**Missing or invalid API key:**
The agent authenticates to the portal using an API key. If the key is
missing, expired, or wrong, every portal request returns 401 and the
agent stops.
```bash
# Check the key is set
docker compose exec agent-helpdesk-01 printenv LUCID_API_KEY
# If empty or wrong — update .env and restart
docker compose restart agent-helpdesk-01
```

**Portal unreachable from agent container:**
```bash
# Test portal connectivity from inside the agent container
docker compose exec agent-helpdesk-01 \
  curl -s http://admin-portal:5000/api/health/ | jq
```
If this fails, the Docker network may have an issue. Try restarting the
full stack: `docker compose down && docker compose up -d`

**Agent container exited:**
If `docker compose ps` shows the agent as `exited`, check for a crash:
```bash
docker compose logs agent-helpdesk-01 | tail -30
```
A Python traceback in the logs indicates a startup error. Common causes:
misconfigured service account in the portal, or the portal is sealed.

---

### Agent is running but not picking up tickets

The agent is online and the portal shows a recent heartbeat, but tickets
in ServiceNow are not being processed.

**Check the assignment group name:**
The assignment group in the agent configuration must exactly match the
group name in ServiceNow — including capitalization and spaces.

Navigate to **Agents** in the portal, open your agent, and verify the
**Assignment Group** field. Compare it character-by-character against
the group name in ServiceNow.

**Check ServiceNow connectivity:**
```bash
docker compose logs --tail=50 agent-helpdesk-01 | grep -i servicenow
```
Look for authentication errors or connection timeouts. If the ServiceNow
service account password has changed, update it in **Service Accounts**
in the portal.

**Check that tickets are assigned to the group:**
In ServiceNow, confirm the test ticket's Assignment Group field is set
to the monitored group, not just the Category or another field.

---

## Active Directory Issues

### Tool Server shows `adConnected: false`

The Tool Server is running but cannot reach the Domain Controller on port 636.

Work through the [LDAPS Verification](../installation/verify-ldaps.md) guide.
The most common causes are:

- Port 636 blocked by a firewall between the Tool Server and DC
- The DC has no valid certificate for LDAPS
- The DC's certificate issuing CA is not trusted by the Tool Server

---

### Password reset fails — "Access Denied" or "Insufficient Rights"

The Tool Server reached the DC, authenticated, and attempted the operation,
but AD rejected it.

This is an AD delegation issue. The `svc-praxova` account does not have the
**Reset Password** extended right on the OU containing the target user.

**Verify the delegation:**
On the Domain Controller, open **Active Directory Users and Computers**.
Enable **View → Advanced Features**. Navigate to the target OU, right-click
**Properties → Security**. Look for `svc-praxova` with `Reset Password`
and `Write pwdLastSet` listed.

If the permissions are missing, re-run the Delegate Control wizard on the
target OU. See [Prerequisites](../getting-started/prerequisites.md) for the
full list of required permissions.

**Confirm the user is in the delegated OU:**
If the target user is in a different OU than the one you delegated to,
the delegation doesn't apply. Either move the user to the correct OU, or
extend the delegation to cover the user's OU.

---

### Group membership change fails

Same diagnosis as password reset — check the `Write member` permission on
the target group object via the **Security** tab in AD Users and Computers.

Note that the delegation for group membership is on the **group object**,
not on the OU. You must grant `Write member` on each specific group that
Praxova manages, or on an OU containing those groups.

---

## Classification Issues

### Tickets are being escalated instead of resolved

Every ticket, or a category of tickets, is being classified with low
confidence and routed to escalation rather than being processed.

**Check the classification in the audit log:**
Navigate to **Audit Log** and find a recent escalated ticket. Look at the
Classify step entry — it will show the classification result and confidence
score. A score below your configured threshold will trigger escalation.

**The most common cause is ambiguous ticket descriptions.** If users write
"I can't log in" without specifying whether they need a password reset or
an account unlock, the classifier may not be confident enough to act.

**What to do:**
Add more representative examples to the classification example sets in
**Workflows → Example Sets**. Focus on the actual phrasing your users use.
Even 5–10 new examples per ticket type will improve accuracy noticeably.

---

### A ticket was classified as the wrong type and resolved incorrectly

The classifier picked the wrong ticket type and the wrong sub-workflow ran.

Check the Audit Log entry for that ticket — the Classify step shows what
the LLM decided and the confidence score.

**Immediate action:** If the action taken was harmful (wrong password reset,
wrong group membership change), reverse it in Active Directory manually.
The Audit Log entry shows exactly what was changed.

**Prevent recurrence:** Add the misclassified ticket's description as a
correct example in the appropriate example set, and as a negative example
in the example set it was incorrectly matched to.

---

## Workflow Issues

### A ticket was marked resolved but the action wasn't taken

The ServiceNow ticket shows resolved with a Praxova work note, but the
AD operation did not actually occur.

This is a known issue in the current release. If a sub-workflow fails
internally and the dispatcher workflow has no failure transition configured,
the ticket can be incorrectly marked resolved rather than escalated.

**Check the Audit Log** for that ticket number. If the ToolCall step shows
`result: failed`, the action failed but the workflow did not escalate.

**Immediate action:** Perform the AD operation manually. Update the
ServiceNow ticket to reflect what happened.

**Workaround:** Monitor the Audit Log daily for `result: failed` entries
on ToolCall steps. Any such entry warrants manual follow-up.

This will be fully resolved in a future release. See
[Known Limitations](../reference/known-limitations.md).

---

### An approval has been sitting in the queue for a long time

Pending approvals do not time out — they wait until a human acts. If an
approval has been waiting unexpectedly long:

- Check whether the notification for that approval was delivered (if you
  have notification workflows configured)
- If you want to reject it, click **Reject** in the Approvals queue and
  add a note — the ticket will be escalated back to the helpdesk team

There is currently no automatic expiry for approvals. If your team needs
approvals to time out after a set period, this can be implemented as a
workflow step. Automatic approval timeout is on the roadmap.

---

## Full Reset (Last Resort)

If the portal database is corrupted or you need to start completely fresh,
this procedure destroys all configuration and data:

```bash
docker compose down
docker volume rm praxova-admin-data praxova-admin-logs
docker compose up -d
```

**This cannot be undone.** You will lose:
- All service accounts and stored credentials
- All workflow definitions
- All API keys
- All audit history
- The internal PKI (generated certificates become invalid)

After a reset, work through the full
[Docker Host Setup](../installation/docker-host.md) configuration steps
again, and re-provision the Tool Server TLS certificate
(the old certificate will no longer be trusted).

LLM model files are stored in a separate volume (`praxova-llm-models`) and
are **not** affected by this reset.

---

## Getting Help

If you have worked through the relevant sections above and the issue
persists, collect the following before seeking support:

```bash
# System state
docker compose ps
curl -s http://localhost:5000/api/health/ | jq

# Recent logs (last 100 lines from each container)
docker compose logs --tail=100 admin-portal > portal.log
docker compose logs --tail=100 agent-helpdesk-01 > agent.log

# Tool Server health (run from tool server)
# Invoke-RestMethod https://toolserver01.yourdomain.com:8443/api/v1/health
```

Raise an issue at the
[Praxova GitHub repository](https://github.com/praxova/praxova-it-agent/issues)
with the log output and a description of what you were doing when the
problem occurred.
