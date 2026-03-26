# Configuring Approval Gates

An approval gate is a pause point in a workflow. When the agent reaches one,
it stops, presents the proposed action to a human reviewer, and waits for
a decision before proceeding.

Approval gates are how most organizations start with Praxova — watching the
agent work and approving each action until the team is confident it is
making the right calls. Over time, as that confidence builds, the gates can
be loosened or removed entirely.

---

## How Approval Gates Work

When a workflow reaches an Approval step:

1. A **Pending Approval** record is created in the Admin Portal
2. The workflow execution is suspended — the agent moves on to other tickets
3. A reviewer sees the pending approval in **Approvals** in the portal
4. The reviewer reads the proposed action and clicks **Approve** or **Reject**
5. On the next poll cycle, the agent detects the decision and resumes the workflow

Approval state survives agent restarts. If the agent is stopped and restarted
while an approval is pending, the workflow resumes correctly when the approval
is acted on.

### What the Reviewer Sees

Each pending approval shows:

- **Ticket number** — the ServiceNow ticket that triggered this workflow
- **Proposed action** — a plain-English description of what the agent wants to do
  (e.g., "Reset password for jsmith in OU=Staff,DC=yourdomain,DC=com")
- **Classification** — the ticket type and confidence score
- **Context** — key details extracted from the ticket (affected user, target group, etc.)
- **Approve / Reject buttons** — with an optional notes field on rejection

Nothing in Active Directory changes until a reviewer approves it.

---

## The Default Approval Configuration

Out of the box, Praxova requires human approval for group membership changes.
Password resets and account unlocks are processed automatically (no approval step).

This default reflects where most organizations start: password resets are
low-risk and reversible, but adding someone to a security group has access
control implications that benefit from a human check.

You can change this for any workflow.

---

## Adding an Approval Gate to a Workflow

Open the workflow in the designer. In the toolbar, select **Approval** and
place it between the Validate step and the Execute step.

Connect it:

```
[Validate] → [Approval] → [Execute]
                │
                └── (rejected) → [Escalate]
```

The Approval step has two outgoing transitions:
- **Approved** — continues to the Execute step
- **Rejected** — routes to Escalate (the ticket is assigned to a human with
  the reviewer's rejection notes attached)

In the Approval step properties panel, configure:

| Field | Description |
|-------|-------------|
| **Description template** | The human-readable action description shown to reviewers. Use `{affected_user}`, `{target_group}`, `{ticket_number}` as placeholders. |
| **Auto-approve threshold** | Confidence score above which the gate is bypassed automatically. Set to `1.01` to always require human approval. |
| **Timeout** | Not yet implemented — approvals currently wait indefinitely. |

### Writing a Good Description Template

The description is what the reviewer reads when deciding to approve or reject.
Make it specific and unambiguous.

**Good:**
`Reset Active Directory password for {affected_user} ({affected_user_dn})`

**Too vague:**
`Perform the requested action for {affected_user}`

---

## The Auto-Approve Threshold — Your Trust Dial

The most powerful feature of the Approval step is the auto-approve threshold.
This is a confidence score above which the agent bypasses the approval gate
and proceeds automatically.

This lets you gradually extend automation as your confidence grows, without
changing the workflow structure:

| Setting | Effect |
|---------|--------|
| `1.01` (effectively impossible) | All tickets require human approval — full oversight |
| `0.95` | High-confidence tickets (the clear-cut ones) auto-approve |
| `0.85` | Most tickets auto-approve; ambiguous cases still require review |
| `0.00` | All tickets auto-approve — gate is effectively off |

**A suggested progression for new deployments:**

- **Weeks 1–2:** Set to `1.01`. Approve everything manually. Use this period to
  verify the agent is classifying correctly and the proposed actions make sense.
- **Weeks 3–4:** Drop to `0.95`. Clear-cut tickets flow through. You still see
  and approve anything the agent was uncertain about.
- **Month 2:** Drop to `0.85`. Review the audit log weekly rather than checking
  approvals daily.
- **Month 3+:** Consider removing the approval step entirely for ticket types
  where the accuracy has been consistently high.

There is no right answer for how fast to progress. Go at the pace that matches
your team's comfort level and the risk tolerance of your organization.

---

## Removing an Approval Gate

When you are ready to run a workflow without human approval, open the
workflow designer and delete the Approval step. Connect the Validate step
directly to the Execute step.

Save the workflow. The change takes effect immediately — no restart required.

You do not need to remove the gate all at once. If your organization handles
multiple ticket types, you might remove the approval gate from password resets
(high volume, low risk) while keeping it on group membership changes indefinitely.

---

## Processing Approvals

Pending approvals are shown in **Approvals** in the Admin Portal. The badge
count on the menu item shows how many are waiting.

### Approving

Click **Approve** on a pending entry. The agent will execute the proposed action
on its next poll cycle (within 30 seconds). The ServiceNow ticket will be
updated with what was done.

### Rejecting

Click **Reject** and add a note explaining why. The note is included in the
ServiceNow ticket update — the original requester will see it. The ticket
is moved to Escalated status and assigned to your helpdesk queue for manual
handling.

### What If an Approval Sits Unreviewed

Pending approvals do not time out. They wait until acted upon. The requester's
ticket stays open in ServiceNow in an intermediate state.

If your team needs to ensure approvals are reviewed within a specific timeframe,
the recommended approach is to check the Approvals queue as part of your daily
routine (see [Daily Operations](../operations/daily-operations.md)).
Automatic timeout with escalation is on the roadmap for a future release.

---

## Example: Adding Full Approval Coverage for a New Deployment

If you want every single action approved during your initial deployment period
regardless of ticket type, add an Approval step to every sub-workflow and set
the auto-approve threshold to `1.01`.

This gives you complete visibility into everything the agent proposes before
it touches Active Directory — the safest possible starting configuration.

As your confidence grows, adjust the thresholds workflow-by-workflow rather than
all at once. Start with the lowest-risk ticket type (typically account unlocks),
verify the agent is making good decisions, then extend auto-approve to the next
type.

---

## Next Steps

The configuration section is complete. For ongoing operation, see:

→ [Daily Operations](../operations/daily-operations.md)
→ [Troubleshooting](../operations/troubleshooting.md)
