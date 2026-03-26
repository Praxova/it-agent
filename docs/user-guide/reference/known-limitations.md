# Known Limitations

This page documents known limitations and issues in the current release,
with workarounds where they exist. Each item includes a risk assessment
so you can make an informed decision about whether it affects your deployment.

These are tracked internally and will be resolved in future releases.

---

## HIGH — Requires Attention Before Production Use

### Portal authentication uses a local account only (TD-007)

**What it means:** The Admin Portal ships with a local `admin` account.
Active Directory authentication — where your team logs into the portal
using their domain credentials — is not yet implemented.

**Impact:** All portal access goes through the single local admin account.
There is no per-user access control, no role separation between admins and
read-only reviewers, and no integration with your existing identity management.

**Workaround:**
- Change the default `admin` password immediately after first login
- Restrict network access to the portal to trusted administrator machines only
- Store the admin password in a password manager — there is no self-service reset
- The local admin account is intentionally permanent and cannot be disabled.
  It is your break-glass recovery account for when AD is unreachable.

**Planned fix:** Active Directory authentication with group-to-role mapping
is the highest-priority item for the next release (v1.1).

---

## MEDIUM — Monitor in Production

### Failed workflows may incorrectly resolve tickets (TD-004)

**What it means:** If a sub-workflow (password reset, group membership, etc.)
fails internally, and the dispatcher workflow has no failure path configured,
the ticket may be marked as resolved in ServiceNow instead of being escalated
to your team.

**Impact:** A ticket can show as resolved in ServiceNow even though the
underlying AD operation failed or was never attempted. The end user believes
their request was handled when it was not.

**Workaround:**
- Monitor the **Audit Log** daily for ToolCall steps with `result: failed`
- Any such entry should be investigated and the action performed manually
  if needed
- The Audit Log is the source of truth — if it shows a failure, the ticket
  was not actually resolved regardless of its ServiceNow status

**Planned fix:** v1.1 will add failure transitions to the default workflows
and make "no matching transition" log a distinct warning rather than silently
completing.

---

### AD credentials stored on Tool Server disk (TD-010)

**What it means:** The `svc-praxova` AD service account password is currently
stored in `appsettings.json` on the Tool Server machine. The Admin Portal
stores credentials encrypted, but the Tool Server has not yet been updated
to fetch them from the portal at runtime.

**Impact:** The credential is readable by anyone with Administrator access
to the Tool Server machine. Rotating the `svc-praxova` password requires
manually editing `appsettings.json` and restarting the Tool Server service.

**Workaround:**
- Restrict Administrator access to the Tool Server machine to the minimum
  necessary personnel
- Ensure the Tool Server machine is domain-joined and covered by your
  standard endpoint security controls (EDR, privileged access management)
- Rotate `svc-praxova` password according to your password policy, updating
  `appsettings.json` each time

**Planned fix:** The Tool Server will fetch credentials from the portal API
at startup in a future release, eliminating local credential storage entirely.

---

### LDAPS connection test in portal UI may show incorrect status (TD-011)

**What it means:** The Active Directory Settings page includes a
**Test Connection** button to verify LDAPS connectivity from the portal.
This test may report failure even when the actual LDAPS connection used
by the Tool Server is working correctly.

**Impact:** The portal UI connection test is unreliable. Do not use it as
the definitive indicator of whether LDAPS is working.

**Workaround:**
- Use the Tool Server health endpoint as the authoritative LDAPS check:
  ```powershell
  Invoke-RestMethod https://toolserver01.yourdomain.com:8443/api/v1/health
  # adConnected: true = LDAPS is working
  ```
- Process a test ticket (see [End-to-End Verification](../installation/verification.md))
  to confirm AD operations are functioning end-to-end

**Planned fix:** The portal connection test will be updated to use the same
certificate trust path as the actual AD operations.

---

## LOW — Cosmetic or Edge Cases

### Tool Server registration shows an API key field (TD-009)

**What it means:** The Tool Server setup screen in the portal includes an
API key field that is no longer part of the authentication model. Agent-to-Tool
Server authentication uses mutual TLS and operation tokens — the API key
field is a remnant of an earlier design.

**Impact:** The field is confusing but harmless. Leave it blank. It is not
validated or enforced by the Tool Server.

**Planned fix:** The field will be removed from the UI in the next release.

---

### Multi-task tickets are partially processed (TD-005)

**What it means:** If a single ticket contains multiple requests (for example,
"please reset my password AND add me to the VPN group"), the classifier
identifies and processes the first request only. The second request is
silently ignored.

**Impact:** Users who bundle multiple requests into one ticket will have
only the first request fulfilled. They will need to submit a second ticket
for the remaining items.

**Workaround:** Advise users to submit one request per ticket. Add this as
guidance to your ServiceNow intake form or helpdesk documentation.

**Planned fix:** Multi-task ticket decomposition is on the v2.0 roadmap.

---

### Classification uses hardcoded categories in the agent (TD-001)

**What it means:** Adding new ticket types via the portal's example sets
requires a corresponding code update to the agent to recognize the new
classification. The portal UI and the agent are not yet fully in sync for
new categories.

**Impact:** The built-in ticket types (password reset, group membership,
account unlock, file permissions, software install) all work without any
code changes. This limitation only affects you if you want to add entirely
new ticket types beyond those.

**Workaround:** Adding new ticket types in v1.0 requires working with a
developer to add the category to the agent's classification configuration.

**Planned fix:** Dynamic classification driven entirely from the portal's
example sets is a v1.1 item — once resolved, new ticket types can be added
entirely through the portal with no code changes.

---

## Reporting Issues

If you encounter a problem not listed here, please open an issue at the
[Praxova GitHub repository](https://github.com/praxova/praxova-it-agent/issues).

Include a description of what you were doing, what you expected to happen,
what actually happened, and relevant log output (see
[Troubleshooting — Getting Help](../operations/troubleshooting.md)).
