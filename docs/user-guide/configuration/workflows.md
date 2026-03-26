# Understanding and Managing Workflows

Workflows are the heart of Praxova. They define exactly what happens when a
ticket arrives — how it is classified, what conditions route it to which action,
whether it needs human approval, and how it communicates the result.

This page explains how workflows are structured, what the default workflows do,
and how to make changes.

---

## How Workflows Are Structured

Praxova uses a two-level workflow model.

### The Dispatcher Workflow

There is one dispatcher workflow. Every ticket that arrives goes through it first.
The dispatcher does one thing: run classification, then route the ticket to the
right sub-workflow based on what the ticket is asking for.

```
Ticket arrives
      │
      ▼
 [Classify]  ← runs once, determines ticket type
      │
      ├── ticket_type == 'password-reset'    → [Password Reset sub-workflow]
      ├── ticket_type == 'group-membership'  → [Group Membership sub-workflow]
      ├── ticket_type == 'account-unlock'    → [Account Unlock sub-workflow]
      ├── ticket_type == 'file-permissions'  → [File Permissions sub-workflow]
      └── anything else                      → [Escalate]
```

Classification runs once in the dispatcher, not once per sub-workflow. This
is efficient and keeps the logic in one place.

### Sub-Workflows

Each ticket type has its own sub-workflow that handles the full resolution
sequence: validate, approve (if required), execute, notify, close. Sub-workflows
are independent — you can edit, test, and replace them without touching the
dispatcher or other sub-workflows.

---

## The Default Workflows

Praxova seeds these workflows automatically on first startup. You can view
and edit all of them in **Workflows** in the Admin Portal.

| Workflow | Type | Purpose |
|----------|------|---------|
| `it-helpdesk-dispatcher` | Dispatcher | Classifies and routes all incoming tickets |
| `password-reset-sub` | Sub-workflow | Password resets and forced password changes |
| `group-membership-sub` | Sub-workflow | Adding and removing AD group members |
| `account-unlock-sub` | Sub-workflow | Unlocking locked-out accounts |
| `file-permissions-sub` | Sub-workflow | Granting and revoking NTFS file share access |

---

## Workflow Step Types

Each step in a workflow has a type that determines what it does. Here is
what each type means in plain terms:

| Step Type | What It Does |
|-----------|-------------|
| **Trigger** | The entry point. Receives the ticket from ServiceNow and starts execution. |
| **Classify** | Sends the ticket to the LLM for classification. Returns a ticket type and confidence score. |
| **Condition** | A branch point. Evaluates a condition (e.g., `ticket_type == 'password-reset'`) and routes accordingly. |
| **Query** | Looks something up on the Tool Server without changing anything. For example: check if a user exists in AD. |
| **Validate** | Checks pre-conditions before taking action. Fails cleanly with a reason if validation does not pass. |
| **Approval** | Pauses execution and creates a pending approval in the portal. Resumes when a human approves or rejects. |
| **Execute** | Calls a capability on the Tool Server to perform an AD operation. |
| **Notify** | Adds a work note to the ServiceNow ticket. |
| **Escalate** | Routes the ticket to the human queue with a reason. Terminal step — the workflow ends here. |
| **End** | Successful completion. Closes the ServiceNow ticket. Terminal step. |
| **SubWorkflow** | Invokes another workflow as a child. Used by the dispatcher to hand off to sub-workflows. |

---

## Editing a Workflow

Navigate to **Workflows** in the Admin Portal and click a workflow name to
open the visual designer.

The designer shows each step as a node on a canvas, connected by arrows
(transitions). You can:

- **Move steps** by dragging them
- **Add a step** by clicking the step type in the toolbar and placing it
- **Connect steps** by dragging from one step's output port to another's input
- **Configure a step** by clicking it to open the properties panel on the right
- **Delete a step** by selecting it and pressing Delete
- **Set transition conditions** by clicking the arrow between two steps

Click **Save** to persist your changes. Changes take effect the next time
the agent polls for new tickets — no restart required.

> **The default workflows are safe to modify.** They are not locked or
> protected. If you make a change that breaks something, you can restore the
> defaults by deleting all workflows and restarting the portal — the seeder
> will recreate them. Back up your customizations first.

---

## Transition Conditions

Transitions between steps can be unconditional (always taken) or conditional
(taken only when an expression evaluates to true).

Conditions are written as simple expressions using the variables available
in the execution context at that point in the workflow:

| Example Condition | Meaning |
|-------------------|---------|
| `ticket_type == 'password-reset'` | Route here for password reset tickets |
| `confidence >= 0.85` | Route here when classification is high-confidence |
| `outcome == 'success'` | Route here when the previous step succeeded |
| `outcome == 'failed'` | Route here when the previous step failed |
| `outcome == 'escalated'` | Route here when a sub-workflow escalated |
| `affected_user != null` | Route here when a user was identified |

When multiple transitions leave a step, they are evaluated in order. The
first condition that matches is taken. If no conditions match, the ticket
is escalated as a safety measure.

---

## Adding a New Ticket Type

When you want Praxova to handle a ticket type it does not currently
support, the process involves three steps: add classification examples,
create a sub-workflow, and add a dispatcher route.

### Step 1 — Add Classification Examples

Navigate to **Workflows → Example Sets** and open the
`it-dispatch-classification` example set. Add examples representing
how your users phrase requests of the new type.

Use consistent naming for the `ticket_type` field in your examples —
this string must exactly match the condition you will add to the
dispatcher in Step 3.

For example, if adding VPN access requests:
- Example: "I need VPN access" → `ticket_type: "vpn-access"`
- Example: "Please set me up with VPN" → `ticket_type: "vpn-access"`
- Example: "My VPN stopped working" → `ticket_type: "vpn-access"`

Add at least 3–5 examples per new ticket type. More diverse examples
produce more accurate classification.

### Step 2 — Create a Sub-Workflow

Navigate to **Workflows → New** and build the resolution steps for
the new ticket type. At minimum, a sub-workflow needs:

`Trigger` → `Validate` → `Approval` (optional) → `Execute` → `Notify` → `End`

with an `Escalate` branch from any step that might fail.

### Step 3 — Add a Dispatcher Route

Open the `it-helpdesk-dispatcher` workflow. Find the Condition step
that branches on `ticket_type`. Add a new outgoing transition with the
condition `ticket_type == 'vpn-access'` pointing to a new SubWorkflow
step that references the sub-workflow you created in Step 2.

Save the dispatcher. The agent will use the updated routes on its next
poll cycle.

> **Note on classification in v1.0:** Adding new ticket types via examples
> currently requires a developer to also update the classification configuration
> in the agent. This limitation will be removed in v1.1, at which point new
> ticket types can be added entirely through the portal. See
> [Known Limitations](../reference/known-limitations.md) for details.

---

## Next Steps

→ [Classification and Training Examples](classification.md)
→ [Configuring Approval Gates](approval-gates.md)
