# Classification and Training Examples

Classification is the step where Praxova reads a ticket and decides what kind
of request it is. Everything that follows — which workflow runs, whether it
escalates, what AD operation is attempted — depends on getting this right.

This page explains how classification works and how to improve it over time.

---

## How Classification Works

When a ticket arrives, the Classify step sends the ticket description to your
configured LLM along with a set of training examples. The LLM reads the ticket,
compares it against the examples, and returns a structured result:

```
{
  "ticket_type": "password-reset",
  "confidence": 0.94,
  "affected_user": "jsmith",
  "target_group": null,
  "reasoning": "Clear password reset request with username identified"
}
```

The `ticket_type` determines which sub-workflow runs. The `confidence` score
determines whether to act automatically or route to human review.

### Confidence Thresholds

Every classification comes with a confidence score between 0 and 1. You
control what happens at different confidence levels:

| Confidence | Default Behavior |
|------------|-----------------|
| ≥ 0.85 | Proceed with the identified workflow |
| 0.60 – 0.84 | Proceed but flag for human review in the audit log |
| < 0.60 | Escalate to the human queue — do not guess |
| `unknown` type | Always escalate regardless of confidence |

These thresholds can be adjusted in the Classify step properties in the
workflow designer. Lower thresholds mean more automation but more risk
of wrong actions. Higher thresholds mean more escalations but higher
accuracy on what does get automated.

---

## Example Sets — The Training Data

Classification quality is directly determined by the examples you provide.
The LLM learns what "password reset" means in your environment by seeing
how your users phrase those requests.

Example sets are managed in **Workflows → Example Sets** in the Admin Portal.

### The Default Example Set

Praxova ships with a built-in example set called `it-dispatch-classification`
that covers the standard ticket types with generic examples. These work
reasonably well out of the box, but they are not trained on your organization's
vocabulary.

Your users probably do not say "I require a password reset for my account."
They say "locked out again" or "can't get into my email" or "forgot my pw."
The default examples do not know this. You do.

### Viewing and Editing Examples

Navigate to **Workflows → Example Sets** and open `it-dispatch-classification`.

You will see the existing examples grouped by ticket type. Each example has:

- **Short description** — the ticket subject line (what the LLM sees as input)
- **Description** — the ticket body (optional but helps with ambiguous cases)
- **Expected type** — the `ticket_type` value this example should produce
- **Expected confidence** — the confidence score this example demonstrates
- **Notes** — your own notes about why this example is useful (not sent to the LLM)

### Adding a Good Example

A good example is realistic phrasing from your actual users — not a textbook
definition of the ticket type.

**Do add:**
- "locked out again, same thing as last week"
- "need access to the shared drive on FS01"
- "forgot my credentials for the Orion system"
- "can't log into my laptop after the password expired"

**Avoid:**
- "I would like to request a password reset for my domain account"
- "Please grant me access to the file share resource"

The first set is how real users write tickets. The second set is how IT
documentation writers describe tickets. The LLM will perform better with
the first set in your environment.

---

## Building a Strong Example Set

### How Many Examples Do You Need?

For ticket types already covered by the defaults, 5–10 organization-specific
examples per type is enough to meaningfully improve accuracy. You do not need
to replace the defaults — new examples are added alongside them.

For entirely new ticket types you are adding, start with at least 5 examples
before enabling that type in the dispatcher. Fewer than that and the classifier
will struggle to recognize the pattern reliably.

### Variety Matters More Than Volume

Ten diverse examples are more valuable than fifty similar ones. Cover the
range of ways your users phrase the same request:

- Different levels of detail ("reset my pw" vs "I've been locked out after
  too many failed attempts and need my password reset before my 9am meeting")
- Different terminology ("VPN" vs "remote access" vs "GlobalProtect")
- Requests made on behalf of others ("need to reset Han Solo's password")
- Requests with missing information ("I need access" — without specifying to what)

### Negative Examples

You can add examples that demonstrate what a ticket type is NOT. If the classifier
is confusing password resets with account unlocks, add a clear account unlock
example so the LLM learns the distinction. The contrast between the two categories
in the example set helps more than adding more examples of just one type.

---

## The Improvement Loop in Practice

The most effective way to improve classification accuracy is a weekly
habit, not a one-time setup task.

**When you review escalated tickets each week, ask:**

1. Was this escalated because the classifier wasn't confident, or because
   it was genuinely out of scope?
2. If it was escalated due to low confidence but a human handled it easily —
   what would a good example for that ticket look like?
3. If it was classified as the wrong type — what made it ambiguous? Can
   an example help the classifier distinguish it from the type it was
   confused with?

Add one or two examples per week based on real tickets you reviewed. After
a month of this habit, you will notice the escalation rate for those ticket
types dropping and the confidence scores rising.

### Tracking Improvement

The **Audit Log** shows the confidence score for every classification. Over
time you can observe:

- Which ticket types consistently classify with high confidence (no attention needed)
- Which types hover near the escalation threshold (a few targeted examples will help)
- Which types regularly come back as `unknown` (may need new examples or a new workflow)

---

## Rulesets — Behavioral Guardrails

Alongside examples, Praxova uses rulesets to define hard constraints on
agent behavior. These are plain-language instructions given to the LLM
alongside every classification and execution step.

Navigate to **Workflows → Rulesets** to view and edit them.

### Default Rulesets

Praxova ships with two built-in rulesets:

**Security Rules** — Things the agent must never do, regardless of what
the ticket says:
- Never reset passwords for service accounts or admin accounts
- Never modify membership of privileged groups (Domain Admins, etc.)
- Flag requests involving accounts in sensitive OUs for manual review

**Escalation Rules** — Conditions that always trigger escalation:
- Classification confidence below threshold
- Ticket type is `unknown`
- Affected user cannot be found in AD
- Requested resource does not exist

### Adding Your Own Rules

Click **New Rule** in any ruleset to add a rule. Rules are written in
plain English — they are instructions to the LLM, not code expressions.

Examples of useful custom rules for your environment:
- "Never process requests for accounts belonging to the Finance team without
  manager approval, even for password resets."
- "If the ticket mentions the word 'urgent' or 'emergency', route to human
  review regardless of ticket type."
- "Never grant access to shares containing 'HR' or 'Payroll' in the path."

Keep rules clear and specific. Vague rules ("be careful with sensitive
accounts") are less reliable than specific ones ("never modify accounts
in the OU=Executives,DC=yourdomain,DC=com").

---

## Next Steps

→ [Configuring Approval Gates](approval-gates.md)
