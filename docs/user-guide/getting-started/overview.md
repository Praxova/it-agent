# What Is Praxova IT Agent?

Praxova IT Agent is an AI-powered automation platform for IT helpdesk teams. It
watches your IT ticket queue, figures out what each ticket is asking for, and
resolves it — resetting passwords, managing group memberships, unlocking accounts,
and adjusting file permissions — without anyone on your team needing to touch it.

For tickets that require a human decision, it pauses and waits for your approval
before doing anything. For tickets outside its capabilities, it escalates cleanly
with full context attached.

The result: your Level 1 ticket volume drops, your team spends time on work that
actually needs a human, and every action the system takes is logged and auditable.

---

## The Problem It Solves

Most IT teams spend a significant chunk of their week on tickets that follow the
same script every time:

- "Reset my password" → look up the user, reset it, notify them
- "Add me to the VPN group" → verify the request, add them, confirm
- "Unlock my account" → check it's locked, unlock it, done

These tickets are low-risk, well-understood, and repetitive. They're also
interruptions. Every password reset at 4pm is someone on your team stopping
what they were doing to do something a trained system could handle in seconds.

Praxova handles this category of work automatically, around the clock, with
a complete record of every action it took.

---

## How It Works

When a ticket comes in, Praxova works through it in a predictable sequence:

**1. Detect** — Praxova monitors your ServiceNow assignment group. When a new
ticket arrives, it picks it up automatically.

**2. Classify** — An AI model reads the ticket and determines what kind of
request it is: password reset, group membership change, account unlock, and so
on. If the classification confidence is low, the ticket is flagged for human
review rather than guessed at.

**3. Route** — The classified ticket is handed to the appropriate workflow.
Each ticket type has its own resolution path — the steps for a password reset
are different from the steps for a file permission change.

**4. Validate** — Before taking any action, Praxova verifies pre-conditions:
does the user exist in Active Directory? Is this request within policy? If
validation fails, the ticket is escalated with an explanation.

**5. Approve (if required)** — For actions your team has configured to require
human sign-off — like adding a user to a privileged group — execution pauses.
The pending approval appears in the Admin Portal, and the system waits. Nothing
happens until a human approves it.  Approvals can be based on the LLM confidence
score allowing automatic approval when the LLM is given improved examples from 
your environment.

**6. Execute** — The action is carried out against Active Directory via a
dedicated Tool Server running on your domain. The agent itself never touches AD
directly and never holds domain credentials.  Agents get a JWT token that is only
valid for 5 minutes and scoped to the object the ticket is concerned with.

**7. Communicate and Close** — Praxova updates the ServiceNow ticket with what
it did (or why it escalated), and resolves it. The caller knows what happened
and why.

---

## What You're Deploying

Praxova has three components. You don't need to understand the internals to
deploy it, but knowing what each piece does helps when something needs attention.

### Admin Portal

A web-based control center where all configuration lives. You use it to:

- Connect Praxova to ServiceNow, Active Directory, and your LLM provider
- Build and manage automation workflows
- Review and approve pending actions
- Read the audit log of everything the system has done

The Admin Portal runs as a Docker container on a Linux server. It is the single
source of truth for all Praxova configuration.

### IT Agent

The automation engine. It polls your ServiceNow queue, classifies tickets,
runs workflows, and communicates results back. The agent has no credentials
of its own — it gets everything it needs from the Admin Portal at runtime.

The agent also runs as a Docker container, alongside the Admin Portal.

### LLM Container (optional)

A llama.cpp based container that allows you to run a local LLM to power your
agent.  Multipe LLM providers can be configured, allowing escalating behavior
where agents running local models can escalate to agents cloud models who 
ultimiately escalate to a human.

### Tool Server

A Windows service that runs on a domain-joined server and executes Active
Directory operations on the agent's behalf. It connects to your domain
controller over LDAPS (encrypted LDAP) using a dedicated service account with
carefully scoped delegated permissions.

This separation is intentional: the agent and portal run on Linux and have
no direct path into your AD environment. The Tool Server is the only component
that touches Active Directory, and it only accepts requests from a verified agent.

---

## Security by Design

Praxova was built to operate with elevated Active Directory privileges, which
means its security architecture was designed from the start — not added later.
A few things worth knowing before you deploy:

**Credentials are encrypted at rest.** ServiceNow passwords, AD bind credentials,
and API keys are stored using envelope encryption. A stolen database file alone
is not enough to read them.

**The agent never holds domain credentials.** AD credentials live in the Admin
Portal's encrypted secrets store. The Tool Server fetches them at runtime. The
agent — the piece doing the automation work — has no path to your AD credentials.

**All connections are encrypted.** Every link between components uses TLS.
Praxova generates its own internal certificate authority at install time, so
you don't need to purchase or provision certificates from an external CA.

**Least privilege by default.** The AD service account Praxova uses is granted
only the specific delegated permissions it needs — reset passwords on specific
OUs, modify group membership on specific groups. It is not a Domain Admin account.

**Everything is audited.** Every ticket processed, every action taken, every
approval decision made — all of it is written to an audit log in the Admin Portal.

---

## What Praxova Does Not Do

It helps to be clear about the boundaries:

- **Praxova is not autonomous.** It operates within the workflows you define,
  against the systems you connect it to. It does not make decisions outside
  those boundaries.

- **Praxova does not replace ServiceNow.** It reads from and writes to your
  ITSM. Tickets still live there. Praxova automates the resolution, not the
  system of record.

- **Praxova does not handle every ticket type on day one.** Version 1.0 covers
  the most common Level 1 requests: password resets, group membership, account
  unlocks, and file permissions. Tickets outside this scope escalate cleanly
  to your team with full context attached.

- **Praxova is not a chatbot or helpdesk interface.** It processes tickets from
  your existing ITSM. End users continue to submit requests the same way they
  always have.

---

## Next Steps

Ready to deploy? Start with the prerequisites checklist to make sure your
environment is prepared before you run your first command.

→ [Prerequisites and Planning](prerequisites.md)
