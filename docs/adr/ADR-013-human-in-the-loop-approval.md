# ADR-013: Human-in-the-Loop Approval Steps

**Status**: Proposed  
**Date**: 2026-02-06  
**Decision Makers**: Alton  
**Depends On**: ADR-011 (Composable Workflows — Complete)

---

## 1. Problem Statement

IT departments won't hand their Active Directory keys to an AI on day one.
Even when the agent demonstrates perfect classification and correct tool server
calls in a demo, the production conversation always goes the same way:

> "This is impressive. But before it actually resets a password or adds someone
> to a security group, we need a human to approve it. At least until we trust it."

This is entirely reasonable. The path to full automation runs through a trust-building
phase where humans verify the agent's decisions before they're executed. Today,
Lucid workflows run start-to-finish without pause — there's no way to insert a
checkpoint where a human reviews and approves the agent's proposed action.

The goal is a new **Approval** step type that:

1. Pauses the workflow and presents the agent's proposed action to a human
2. Waits for the human to approve, reject, or modify
3. Resumes or escalates based on the decision
4. Can be placed anywhere in a workflow — after classification, before execution,
   or both
5. Supports a **confidence-based auto-approve threshold** so IT teams can gradually
   give the agent more autonomy without redesigning the workflow
6. Can be removed from the workflow entirely when the team is ready for full
   automation — a simple drag-and-delete in the workflow designer

### The Trust Gradient

The killer feature is the auto-approve confidence threshold. A workflow might start
configured as:

```
Week 1:  Auto-approve threshold = 1.01 (effectively: always require human)
Week 4:  Auto-approve threshold = 0.95 (high-confidence tickets go through)
Week 8:  Auto-approve threshold = 0.80 (most tickets auto-approved)
Week 12: Remove the Approval step entirely (full automation)
```

This transforms the go-live from a binary "flip the switch" event into a gradual
dial. The admin can adjust the threshold from the portal without touching the
workflow structure, watching the approval queue shrink as they gain confidence.

---

## 2. Architecture Decision: Portal Approval Queue

### Approaches Considered

| Approach | Description | Verdict |
|----------|-------------|---------|
| **A: Full Workflow Suspension** | Serialize entire ExecutionContext to DB, terminate workflow, resume from saved state on approval | ❌ Over-engineered. Python runtime objects (LLM drivers, HTTP clients) can't be serialized. Complex distributed state. |
| **B: ServiceNow Ticket State Loop** | Write "awaiting approval" to ticket, end workflow. Re-process ticket on state change. | ❌ Loses context between runs. Re-classification may differ. Tied to ServiceNow only. |
| **C: Portal Approval Queue** | Capture context variables as JSON, POST to Admin Portal, workflow suspends. Agent polls for decisions. Resume from stored context. | ✅ **Selected.** Context preserved. Trigger-agnostic. Natural portal UI. Right complexity for MVP. |

### Why Approach C Wins

The `ExecutionContext` in the Python runtime stores all workflow state as a simple
dictionary of string keys to JSON-serializable values. Things like `ticket_type`,
`confidence`, `affected_user`, `ticket_id` — all primitive data. This dict is
trivially serializable to JSON and back.

We don't need to serialize the workflow engine, LLM drivers, or HTTP clients.
We just need to save the data dict, remember which step we paused at, and when
the approval comes back, create a fresh `ExecutionContext` with the saved data
and resume execution from the step after the Approval node.

The approval queue lives in the Admin Portal — the same place admins already manage
workflows, examples, and capability mappings. No new application to deploy.

---

## 3. Data Model

### New Entity: `ApprovalRequest`

```
ApprovalRequest
  ├── Id: int (PK)
  ├── WorkflowName: string          # Which workflow created this
  ├── StepName: string               # Which Approval step
  ├── AgentName: string              # Which agent instance
  ├── TicketId: string               # ServiceNow or other ticket ID
  ├── TicketShortDescription: string # For display in queue
  │
  ├── ProposedAction: string         # Human-readable description of what
  │                                  # the agent wants to do (templated)
  ├── ContextSnapshot: string (JSON) # Full execution context variables
  ├── ResumeAfterStep: string        # Step name to resume from after approval
  ├── WorkflowDefinitionId: int      # FK to WorkflowDefinition (for resume)
  │
  ├── AutoApproveThreshold: decimal? # Confidence threshold for auto-approval
  ├── Confidence: decimal?           # The confidence score (if from classification)
  ├── WasAutoApproved: bool          # Whether this was auto-approved
  │
  ├── Status: ApprovalStatus         # Pending | Approved | Rejected | AutoApproved
  │                                  # | TimedOut | Cancelled
  ├── Decision: string?              # Optional note from approver
  ├── DecidedBy: string?             # Username of approver (or "system" for auto)
  ├── DecidedAt: DateTime?           # When the decision was made
  │
  ├── TimeoutMinutes: int?           # Auto-escalate after N minutes (null = no timeout)
  ├── ExpiresAt: DateTime?           # Computed: CreatedAt + TimeoutMinutes
  │
  ├── CreatedAt: DateTime
  └── UpdatedAt: DateTime
```

### ApprovalStatus Enum

```csharp
public enum ApprovalStatus
{
    Pending,         // Waiting for human decision
    Approved,        // Human approved
    Rejected,        // Human rejected
    AutoApproved,    // Confidence met threshold, no human needed
    TimedOut,        // No decision within timeout period
    Cancelled        // Workflow was cancelled externally
}
```

---

## 4. API Endpoints

### Agent-Facing (Python → Admin Portal)

```
POST /api/approvals
  Body: {
    "workflowName": "IT-Dispatch",
    "stepName": "confirm-classification",
    "agentName": "test-agent",
    "ticketId": "INC0010016",
    "ticketShortDescription": "Password reset for jsmith",
    "proposedAction": "Classified as password-reset (confidence: 0.92). Route to password reset sub-workflow.",
    "contextSnapshot": { "ticket_type": "password-reset", "confidence": 0.92, ... },
    "resumeAfterStep": "confirm-classification",
    "workflowDefinitionId": 5,
    "autoApproveThreshold": 0.95,
    "confidence": 0.92,
    "timeoutMinutes": 480
  }
  Response: { "id": 42, "status": "Pending", ... }

  (If confidence >= threshold, the portal can immediately set status to
   AutoApproved and return that status — the agent doesn't need to wait.)

GET /api/approvals/actionable?agentName=test-agent
  Returns: Array of ApprovalRequests where status is Approved, Rejected,
           AutoApproved, or TimedOut (decisions the agent needs to act on).
  The agent polls this alongside its ServiceNow poll.

POST /api/approvals/{id}/acknowledge
  Body: { "agentName": "test-agent" }
  Marks the approval as consumed so it doesn't appear in future polls.
```

### Portal-Facing (Blazor UI → API)

```
GET /api/approvals?status=Pending&page=1&pageSize=20
  Returns: Paginated list of pending approvals for the queue UI

GET /api/approvals/{id}
  Returns: Full approval details including context snapshot

PUT /api/approvals/{id}/decide
  Body: { "status": "Approved", "decision": "Looks correct, proceed.", "decidedBy": "admin" }
  or:   { "status": "Rejected", "decision": "Wrong user identified.", "decidedBy": "admin" }
```

---

## 5. New Step Type: Approval

### Workflow Designer Representation

The Approval step appears as a distinct node type in the workflow designer, visually
differentiated (suggested: orange/amber color) to make human checkpoints obvious at
a glance.

### Step Configuration Schema

```json
{
  "description_template": "Classified as {{ticket_type}} with {{confidence}} confidence. Proposed action: route to {{ticket_type}} sub-workflow.",
  "auto_approve_threshold": 0.95,
  "timeout_minutes": 480,
  "timeout_action": "escalate",
  "context_fields_to_display": ["ticket_type", "confidence", "affected_user", "reasoning"]
}
```

| Field | Purpose |
|-------|---------|
| `description_template` | Mustache-style template rendered with context variables. This becomes the `proposedAction` the human sees. |
| `auto_approve_threshold` | If `confidence` in context >= this value, auto-approve without human review. Set to `null` or `> 1.0` to always require human. Set to `0.0` to always auto-approve (effectively bypassing the step). |
| `timeout_minutes` | How long to wait for a decision before taking `timeout_action`. Null = wait forever. |
| `timeout_action` | What to do on timeout: `"escalate"` (follow rejection path) or `"auto_approve"` (assume approved). Default: `"escalate"`. |
| `context_fields_to_display` | Which context variables to show the human in the approval detail view. Prevents overwhelming them with internal data. |

### Outgoing Transitions

An Approval step has exactly two outgoing transitions:

```
[Approval Step]
  ├── outcome == "approved"  → (next step in happy path)
  └── outcome == "rejected"  → (escalation or alternative path)
```

Auto-approved requests follow the "approved" path. Timed-out requests follow
the "rejected" path (unless `timeout_action` is `auto_approve`).

---

## 6. Python Runtime: ApprovalExecutor

### New Executor: `approval.py`

```python
class ApprovalExecutor(BaseStepExecutor):
    """
    Handles Approval steps by either auto-approving or submitting
    to the Admin Portal approval queue and suspending the workflow.
    """

    async def execute(self, step, context, rulesets):
        config = step.configuration  # parsed from ConfigurationJson

        # 1. Check auto-approve threshold
        confidence = context.get("confidence")
        threshold = config.get("auto_approve_threshold")

        if threshold is not None and confidence is not None:
            if float(confidence) >= float(threshold):
                logger.info(f"Auto-approved: confidence {confidence} >= {threshold}")
                return StepResult(
                    status="complete",
                    output={"outcome": "approved", "auto_approved": True}
                )

        # 2. Build proposed action description from template
        template = config.get("description_template", "Approval required.")
        proposed_action = self._render_template(template, context)

        # 3. Submit to Admin Portal approval queue
        approval_id = await self._submit_approval_request(
            context=context,
            step=step,
            proposed_action=proposed_action,
            config=config
        )

        # 4. Suspend workflow — return a special "suspended" status
        return StepResult(
            status="suspended",
            output={
                "approval_id": approval_id,
                "outcome": "pending"
            }
        )
```

### Workflow Engine Changes

The `WorkflowEngine` needs to handle the `suspended` status:

```python
# In workflow_engine.py execute loop:

result = await executor.execute(step, context, rulesets)

if result.status == "suspended":
    # Store the suspension info
    self._save_suspension(
        ticket_id=context.get("ticket_id"),
        workflow_name=workflow.name,
        step_name=step.name,
        approval_id=result.output.get("approval_id"),
        context_snapshot=context.to_dict()
    )
    # Update ticket status
    await self._update_ticket_status(context, "Pending Approval")
    # Return — workflow execution ends here for now
    return WorkflowResult(status="suspended", ...)
```

### Approval Polling Loop

The agent's main loop gains a second poll alongside ServiceNow:

```python
async def main_loop():
    while True:
        # Poll 1: New tickets from ServiceNow
        new_tickets = await servicenow.poll_queue()
        for ticket in new_tickets:
            await workflow_engine.execute(ticket)

        # Poll 2: Approval decisions from Admin Portal
        decisions = await admin_client.get_actionable_approvals()
        for decision in decisions:
            if decision.status in ("Approved", "AutoApproved"):
                await workflow_engine.resume(
                    workflow_name=decision.workflow_name,
                    resume_after_step=decision.resume_after_step,
                    context_snapshot=decision.context_snapshot,
                    ticket_id=decision.ticket_id
                )
            elif decision.status in ("Rejected", "TimedOut"):
                await workflow_engine.handle_rejection(
                    ticket_id=decision.ticket_id,
                    reason=decision.decision or "Approval rejected/timed out"
                )
            # Acknowledge so it doesn't appear again
            await admin_client.acknowledge_approval(decision.id)

        await asyncio.sleep(poll_interval)
```

### Workflow Engine Resume

```python
async def resume(self, workflow_name, resume_after_step, context_snapshot, ticket_id):
    """Resume a suspended workflow from the step after the approval."""

    # 1. Rebuild execution context from snapshot
    context = ExecutionContext.from_dict(context_snapshot)

    # 2. Look up the workflow definition
    workflow = self._get_workflow(workflow_name)

    # 3. Find the approval step and follow its "approved" transition
    approval_step = workflow.get_step(resume_after_step)
    context.set("outcome", "approved")
    next_step = self._evaluate_transitions(approval_step, context)

    # 4. Continue execution from the next step
    await self._execute_from(next_step, context, workflow)
```

---

## 7. Portal UI: Approval Queue

### New Page: `Approvals/Index.razor`

The Approvals page is the human operator's primary workspace during the
trust-building phase.

**Queue View**:
- Table of pending approvals sorted by creation time (oldest first)
- Columns: Ticket ID, Short Description, Workflow, Proposed Action, Confidence,
  Waiting Since, Expires In
- Color-coded confidence badges: green (>0.9), yellow (0.7-0.9), red (<0.7)
- Quick-action buttons: ✅ Approve / ❌ Reject inline
- Batch approve for multiple similar items
- Filter by workflow, agent, confidence range, age

**Detail View** (click a row or "Details" button):
- Full proposed action description (rendered from template)
- Context variables table (filtered by `context_fields_to_display`)
- Ticket details: Short description, full description, caller
- Classification reasoning (if available from classify step)
- Approve / Reject buttons with optional comment field
- Link to ServiceNow ticket

**Dashboard Metrics** (top of page):
- Pending count
- Average wait time
- Auto-approved today vs human-approved today
- Rejection rate (high rate may indicate classification problems)
- Timeout count (indicates humans aren't reviewing fast enough)

### Navigation

Add "Approvals" to the main nav with a badge showing pending count.
This should be prominent — it's the page operators will live on during
the trust-building phase.

```
📊 Dashboard
📋 Workflows
🔍 Examples
📜 Rulesets
⏳ Approvals (3)    ← NEW, with pending count badge
🖥️ Tool Servers
🔑 Service Accounts
⚙️ Settings
```

---

## 8. Example: Dispatch Workflow with Approval Steps

### Before (Full Automation)
```
Trigger → Classify → Route-to-SubWorkflow → [SubWorkflow executes] → End
```

### After (Human-in-the-Loop)
```
Trigger → Classify → [Approval: Confirm Classification] → Route-to-SubWorkflow
                         ↓ Rejected                           |
                       Escalate                               ↓
                                                   [SubWorkflow with internal Approval]
                                                              |
                                                      [Approval: Confirm Action]
                                                         ↓ Rejected
                                                       Escalate
```

### Concrete Example: Password Reset

```
Ticket: "User padme.amidala forgot her password"

Step 1: Classify
  → Output: {ticket_type: "password-reset", confidence: 0.92, affected_user: "padme.amidala"}

Step 2: Approval (Confirm Classification)
  → auto_approve_threshold: 0.95
  → 0.92 < 0.95 → Requires human approval
  → Posted to approval queue:
    "Classified as password-reset (confidence: 0.92).
     Affected user: padme.amidala.
     Proposed action: Route to password reset sub-workflow."
  → Workflow suspends

  [Human reviews in Portal]
  → Admin clicks "Approve"

Step 3: Route to pw-reset-sub
  → Enters password reset sub-workflow

Step 4 (sub): Validate user exists
  → Output: {user_found: true, user_dn: "CN=padme.amidala,..."}

Step 5 (sub): Approval (Confirm Execution)
  → auto_approve_threshold: 0.98
  → No confidence available for this step → always requires human
  → Posted to approval queue:
    "Reset password for padme.amidala (DN: CN=padme.amidala,OU=Users,...).
     Tool: ad-password-reset on YOURDC01."
  → Workflow suspends

  [Human reviews in Portal]
  → Admin clicks "Approve"

Step 6 (sub): Execute password reset
  → Tool server resets password
  → Success

Step 7: Update ticket, notify user, close
```

### Week 4: Admin Adjusts Threshold

Admin changes the first Approval step's `auto_approve_threshold` from 0.95 to 0.85.
Now the 0.92-confidence password reset would auto-approve — no human needed.

The admin watches the approval queue thin out. When they're comfortable, they lower
the second Approval threshold too. Eventually, they remove both Approval steps
entirely from the workflow in the designer.

---

## 9. Implementation Phases

### Phase A1: Data Model and API (C#)

**Goal**: ApprovalRequest entity, database migration, API endpoints.

1. Create `ApprovalRequest` entity in `LucidAdmin.Core`
2. Add `DbSet<ApprovalRequest>` to `LucidDbContext`
3. Create EF Core migration
4. Implement API endpoints:
   - `POST /api/approvals` (agent submits)
   - `GET /api/approvals/actionable?agentName=` (agent polls)
   - `POST /api/approvals/{id}/acknowledge` (agent marks consumed)
   - `GET /api/approvals?status=` (portal lists)
   - `GET /api/approvals/{id}` (portal detail)
   - `PUT /api/approvals/{id}/decide` (portal approve/reject)
5. Add auto-approve logic in `POST /api/approvals`:
   If `confidence >= autoApproveThreshold`, immediately set status to
   `AutoApproved` and return that status.
6. Add timeout check: background task or query filter that marks expired
   approvals as `TimedOut`.

**Estimated effort**: 4-5 hours

---

### Phase A2: ApprovalExecutor (Python)

**Goal**: New step executor that handles auto-approve or suspension.

1. Create `agent/src/agent/runtime/executors/approval.py`:
   - Check auto-approve threshold against context confidence
   - If auto-approved, return `StepResult(status="complete", output={"outcome": "approved"})`
   - Otherwise, POST to Admin Portal approval queue
   - Return `StepResult(status="suspended", ...)`

2. Modify `WorkflowEngine` to handle `suspended` status:
   - Save suspension metadata
   - Update ticket status to "Pending Approval"
   - Exit the execution loop cleanly

3. Add `resume()` method to `WorkflowEngine`:
   - Rebuild `ExecutionContext` from stored snapshot
   - Find the step after the approval node
   - Continue execution

4. Add approval polling to the agent's main loop:
   - Poll `GET /api/approvals/actionable` on each cycle
   - On `Approved`/`AutoApproved`: call `engine.resume()`
   - On `Rejected`/`TimedOut`: escalate ticket
   - Acknowledge each processed approval

5. Register `ApprovalExecutor` in the executor registry for step type `Approval`.

**Estimated effort**: 4-6 hours

---

### Phase A3: Approval Queue UI (Blazor)

**Goal**: Portal page where humans review and decide on pending approvals.

1. Create `Approvals/Index.razor`:
   - Pending approvals table with key columns
   - Inline Approve/Reject buttons
   - Confidence color-coding
   - Filter by status, workflow, age
   - Auto-refresh (SignalR or polling)

2. Create `Approvals/Detail.razor` (or dialog):
   - Full proposed action description
   - Context variables (formatted for readability)
   - Ticket information
   - Approve/Reject with comment

3. Add "Approvals" to `NavMenu.razor` with pending count badge

4. Dashboard integration: Add pending approval count to Home.razor

**Estimated effort**: 4-6 hours

---

### Phase A4: Workflow Designer Integration

**Goal**: Approval step type in the visual designer.

1. Add `Approval` to the step type palette in the workflow designer
2. Approval nodes render in a distinct color (amber/orange)
3. Properties panel for Approval steps:
   - Description template (textarea with variable hints)
   - Auto-approve threshold (slider: 0.0 to 1.0, or "Always require human")
   - Timeout minutes (number input, or "No timeout")
   - Timeout action (dropdown: Escalate / Auto-approve)
   - Context fields to display (multi-select of available variables)
4. Validation: Approval steps must have exactly 2 outgoing transitions
   (approved and rejected)

**Estimated effort**: 3-4 hours

---

### Phase A5: Seeded Approval Workflows

**Goal**: Update seeded workflows to include Approval steps as demonstration.

1. Add Approval step to `IT-Dispatch` workflow after classification:
   ```
   Trigger → Classify → [Approval: Confirm Classification] → SubWorkflow → End
   ```

2. Add Approval step to `pw-reset-sub` before execution:
   ```
   Validate → [Approval: Confirm Password Reset] → Execute → Notify → End
   ```

3. Set initial thresholds high (0.99) so everything requires approval by default

4. Document the threshold adjustment process in a README or tooltip

**Estimated effort**: 1-2 hours

---

## 10. Design Details

### Template Rendering

The `description_template` uses simple `{{variable}}` substitution from context:

```
Template: "Classified as {{ticket_type}} (confidence: {{confidence}}). Affected user: {{affected_user}}."
Context:  {"ticket_type": "password-reset", "confidence": 0.92, "affected_user": "padme.amidala"}
Result:   "Classified as password-reset (confidence: 0.92). Affected user: padme.amidala."
```

No complex template engine needed — simple string replacement. If a variable is
missing, render as `{{variable_name}}` (visible placeholder) so the admin knows
their template references a variable that doesn't exist in context.

### Context Snapshot Scope

The `contextSnapshot` stored with the approval request includes ALL context
variables at the time of the approval step. This is the full execution state
needed to resume. The `context_fields_to_display` config controls which subset
the human sees in the UI — the rest is stored but hidden.

### Polling Frequency

The approval poll should match the ServiceNow poll interval (default: 30 seconds).
Both polls happen in the same main loop. The approval poll is lightweight — a single
GET request returning a small JSON array.

### Concurrent Approvals

Multiple tickets can have pending approvals simultaneously. Each has its own
`ApprovalRequest` row. The agent processes all actionable approvals each poll cycle.
The UI shows all pending approvals in the queue.

### Approval Without Classification

Not all approval steps follow a classification. An approval step in a sub-workflow
(e.g., "Confirm Password Reset") may not have a `confidence` value in context.
In that case:
- `auto_approve_threshold` has nothing to compare against → always requires human
- The admin can set `auto_approve_threshold: null` to explicitly disable auto-approve
- Future enhancement: allow threshold on any numeric context variable, not just
  `confidence`

---

## 11. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Agent crashes while approval is pending | Medium | Medium | Context is persisted in portal DB. On restart, agent re-polls actionable approvals and resumes. |
| Admin Portal is down when agent submits approval | Low | High | Agent retries with backoff. If portal is down long-term, workflow stays in classify step (no data lost). |
| Human never reviews approval (timeout) | Medium | Medium | Configurable timeout auto-escalates. Dashboard shows aging approvals. |
| Context snapshot is too large | Low | Low | Cap at 1MB. Context is normally small (ticket fields + classification output). |
| Multiple agents process same approval | Low | Medium | Agent name filter + acknowledge endpoint prevents double-processing. |
| Template references missing variable | Low | Low | Render as `{{var_name}}` placeholder, log warning. |

---

## 12. What We're NOT Doing

- **No modification on approval** — The human can only approve or reject, not edit
  the proposed action (e.g., change the affected user). That's a future feature.
  For now, reject and manually handle if the agent got it wrong.

- **No multi-level approval** — No "requires 2 approvers" or "manager must approve
  after tech lead." Single approver is sufficient for MVP.

- **No mobile/email notifications** — Approvals are visible only in the portal.
  No push notifications, emails, or Slack alerts when an approval is pending.
  Future enhancement.

- **No approval history on the ticket** — ServiceNow work notes say "Pending
  Approval" and later "Approved — resuming", but the detailed approval record
  lives in the portal DB, not in ServiceNow.

- **No partial resume** — If a workflow has 3 approval steps and the 2nd one is
  rejected, the entire workflow escalates. No "go back to step X and try again."

---

## 13. Effort Summary

| Phase | Description | Effort | Priority |
|-------|-------------|--------|----------|
| A1 | Data model + API endpoints | 4-5 hours | Must-have |
| A2 | ApprovalExecutor + engine suspend/resume | 4-6 hours | Must-have |
| A3 | Approval queue UI (Blazor) | 4-6 hours | Must-have |
| A4 | Workflow designer integration | 3-4 hours | Should-have |
| A5 | Seeded workflows with approval steps | 1-2 hours | Should-have |
| **Total** | | **16-23 hours** | |

A1+A2 are the minimum for functional approval. A3 makes it usable. A4+A5 are
designer polish and demo readiness.

---

## 14. Success Criteria

- [ ] Approval step pauses workflow and creates approval request in portal
- [ ] Auto-approve works when confidence exceeds threshold
- [ ] Human can approve/reject from portal UI
- [ ] Approved workflows resume from correct step with full context
- [ ] Rejected workflows escalate the ticket
- [ ] Timeout auto-escalates pending approvals
- [ ] Adjusting threshold changes auto-approve behavior without workflow redesign
- [ ] Removing Approval step from workflow enables full automation
- [ ] Multiple concurrent pending approvals handled correctly

---

## 15. References

- ADR-011 — Composable Workflows (dispatcher + sub-workflow execution)
- ADR-012 — Dynamic Classification (provides the confidence scores approval uses)
- TD-004 — Workflow engine silent failure on unmatched transitions (approval
  adds explicit approved/rejected transitions, partially addresses this)
- `agent/src/agent/runtime/workflow_engine.py` — Engine needs suspend/resume
- `agent/src/agent/runtime/executors/` — New approval.py executor

## Progress

| Phase | Description | Status | Date |
|-------|-------------|--------|------|
| A1 | Data model + API endpoints | ✅ Complete | 2026-02-06 |
| A2 | ApprovalExecutor + engine suspend/resume | 🔲 Not Started | — |
| A3 | Approval queue UI (Blazor) | 🔲 Not Started | — |
| A4 | Workflow designer integration | 🔲 Not Started | — |
| A5 | Seeded approval workflows | 🔲 Not Started | — |
