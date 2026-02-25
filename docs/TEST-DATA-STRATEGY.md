# Test Data Strategy

**Date**: 2026-02-15
**Status**: Planning
**Related**: DEV-INFRASTRUCTURE.md, Classification Improvement Loop, Model Tuning

---

## 1. Overview

Praxova's core value proposition is the supervised learning loop — the system improves
through continuous feedback, becoming highly customized to each organization within six
months. To develop, test, and demonstrate this capability, we need high-quality synthetic
test data that is:

- **Consistent** across systems (AD users match ServiceNow users)
- **Realistic** enough to expose classification edge cases
- **Labeled** with known-correct outcomes for automated evaluation
- **Scalable** from dozens of users/tickets (development) to thousands/tens of thousands
  (load testing, model tuning, fine-tuning)

Synthetic data can be as good as real data if you create it correctly. The key is
realistic variation: real users don't write clean, well-structured tickets. They write
frustrated, incomplete, ambiguous messages — and the agent has to handle all of it.

### Beyond Test Data: An Organization Simulator

At scale, the jump from 50 users to 50,000 isn't "run the loop more times." It changes
the problem fundamentally:

- **At 50 users**, you know each one by name. Jane in Accounting, Bob her manager.
- **At 5,000 users**, you need a realistic *company*. Department sizes follow power-law
  distributions. Reporting chains are 3-7 levels deep. Some groups have 500 members,
  some have 3. There are regional offices, contractors, recent hires, people about to
  retire.
- **At 50,000 tickets**, you need realistic *temporal patterns*. Monday morning password
  reset spikes. Quarterly access reviews generate group-removal waves. New hire
  onboarding clusters at the start of each month.

The core abstraction: we're not generating test data, we're **simulating an
organization**. The users, their relationships, their behavior patterns, and the IT
events they generate are all emergent from the organization model.

### Product Potential

This system is itself a product component. Customers could use it during onboarding to
generate realistic test data for their environment — "here's 200 tickets that look like
your organization, let's see how classification performs before we go live." It also
serves as the foundation for model fine-tuning, load testing, and demo content.

---

## 2. The Consistency Chain

Every component in the system must agree on who the users are and what they have
access to. A broken link anywhere in this chain means the agent classifies correctly
but fails on execution (or worse, acts on the wrong user).

```
Single-Source Organization Model
         │
         ├──→ User Generator
         │         │
         │         ├──→ AD Creation Script (PowerShell)
         │         │      Users, groups, OUs exist in Active Directory
         │         │
         │         ├──→ ServiceNow User Import (REST API / CSV)
         │         │      Same users exist as ServiceNow callers
         │         │
         │         └──→ User Corpus (internal reference)
         │                Used by ticket generator
         │
         └──→ Ticket Generator
                   │
                   ├──→ ServiceNow Ticket Submission (REST API)
                   │      Realistic tickets from known callers
                   │
                   └──→ Ground Truth Labels (YAML)
                          Expected classification for evaluation
```

If "jane.doe" submits a password reset ticket in ServiceNow, she must exist in AD as
"jane.doe" in the correct OU with the correct group memberships, or the agent will
classify correctly but fail on execution.

---

## 3. System Architecture

### Package: praxova-testgen

A standalone Python package with CLI interface. Installable, testable, and eventually
shippable as part of Praxova.

```
┌──────────────────────────────────────────────────────────┐
│                    praxova-testgen                        │
│               (Python package / CLI tool)                 │
│                                                          │
│  ┌─────────────┐   ┌─────────────┐   ┌───────────────┐  │
│  │ Organization │──▶│    User     │──▶│   Ticket      │  │
│  │   Model      │   │  Generator  │   │  Generator    │  │
│  │              │   │             │   │               │  │
│  │ Departments  │   │ Identities  │   │ Templates     │  │
│  │ Hierarchy    │   │ Personas    │   │ Persona voice │  │
│  │ Groups       │   │ Memberships │   │ Ground truth  │  │
│  │ Resources    │   │ Traits      │   │ Distributions │  │
│  └─────────────┘   └──────┬──────┘   └──────┬────────┘  │
│                           │                  │           │
│                    ┌──────▼──────────────────▼────────┐  │
│                    │         Output Adapters           │  │
│                    │                                   │  │
│                    │  ├── Active Directory (PowerShell)│  │
│                    │  ├── ServiceNow (REST API / CSV)  │  │
│                    │  ├── Ground Truth (YAML)          │  │
│                    │  ├── Praxova API (direct submit)  │  │
│                    │  └── Raw JSON (for anything else) │  │
│                    └─────────────────────────────────┘  │
│                                                          │
│                    ┌─────────────────────────────────┐   │
│                    │      Evaluation Engine           │   │
│                    │                                  │   │
│                    │  ├── Run classifier              │   │
│                    │  ├── Compare to ground truth     │   │
│                    │  ├── Compute metrics             │   │
│                    │  ├── Confidence calibration      │   │
│                    │  └── Comparative reports (A/B)   │   │
│                    └─────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

### Project Structure

```
test-data/
├── pyproject.toml                    # Package definition
├── README.md
│
├── profiles/                         # Organization profiles
│   ├── dev.yaml                     #   50 users (development)
│   ├── demo.yaml                    #   100 users (video demos)
│   ├── tuning.yaml                  #   1000 users (prompt tuning)
│   └── enterprise.yaml             #   5000 users (load testing)
│
├── templates/                        # Ticket templates by type
│   ├── password_reset.yaml
│   ├── group_access_add.yaml
│   ├── group_access_remove.yaml
│   ├── file_permission.yaml
│   ├── escalation.yaml              # Things agent can't handle
│   └── edge_cases.yaml              # Disguised, multi-action, etc.
│
├── personas/                         # Writing style definitions
│   ├── distributions.yaml           #   How personas distribute in a population
│   └── quirks.yaml                  #   Language patterns per trait combo
│
├── src/
│   └── praxova_testgen/
│       ├── __init__.py
│       ├── cli.py                    # Click CLI entry point
│       │
│       ├── models/                   # Data models
│       │   ├── organization.py      #   Org, Department, Group, Resource
│       │   ├── user.py              #   User, Persona, Traits
│       │   ├── ticket.py            #   Ticket, GroundTruth, Label
│       │   └── evaluation.py        #   Metrics, Report, Comparison
│       │
│       ├── generators/
│       │   ├── org_generator.py     #   Build org from profile
│       │   ├── user_generator.py    #   Generate users with personas
│       │   ├── ticket_generator.py  #   Generate tickets with labels
│       │   └── llm_generator.py     #   LLM-assisted rewriting
│       │
│       ├── adapters/
│       │   ├── base.py              #   OutputAdapter ABC
│       │   ├── active_directory.py  #   PowerShell script generation
│       │   ├── servicenow.py        #   REST API / CSV export
│       │   ├── ground_truth.py      #   YAML label export
│       │   └── praxova_api.py       #   Direct Praxova submission
│       │
│       ├── evaluation/
│       │   ├── runner.py            #   Submit to classifier, collect results
│       │   ├── metrics.py           #   Accuracy, precision, recall, calibration
│       │   ├── comparator.py        #   A/B test analysis
│       │   └── reporter.py          #   Markdown + JSON reports
│       │
│       └── utils/
│           ├── names.py             #   Faker wrapper for consistent naming
│           ├── text.py              #   Persona-based text transformation
│           └── seed.py              #   Reproducible RNG management
│
├── ground-truth/                     # Generated + reviewed label sets
│   └── .gitkeep
│
├── results/                          # Evaluation run outputs
│   └── .gitkeep
│
└── tests/
    ├── test_org_generator.py
    ├── test_user_generator.py
    ├── test_ticket_generator.py
    └── test_evaluation.py
```

### CLI Interface

```bash
# Generate a dev environment (50 users, 200 tickets)
praxova-testgen generate --profile profiles/dev.yaml --seed 42

# Generate just users, output as AD PowerShell
praxova-testgen users --profile profiles/dev.yaml --adapter ad \
    --output scripts/create-users.ps1

# Generate just users, push to ServiceNow
praxova-testgen users --profile profiles/dev.yaml --adapter servicenow --submit

# Generate tickets with ground truth labels
praxova-testgen tickets --profile profiles/dev.yaml --count 200 \
    --output ground-truth/batch-001.yaml

# Generate tickets and submit to ServiceNow
praxova-testgen tickets --profile profiles/dev.yaml --count 50 \
    --adapter servicenow --submit

# Evaluate classifier against labeled dataset
praxova-testgen evaluate --dataset ground-truth/batch-001.yaml \
    --classifier http://localhost:8080

# Compare two evaluation runs
praxova-testgen compare --baseline results/run-001.json \
    --candidate results/run-002.json
```

---

## 4. Organization Model

### Profiles

Organization profiles define the shape of a simulated company. At small scale,
everything can be hand-specified. At large scale, you define *parameters* and the
model generates a realistic org structure.

```yaml
# profiles/dev.yaml (50 users, for development)
organization:
  name: "Montani Farms"
  domain: "montanifarms.com"
  netbios: "MONTANIFARMS"
  size: 50

  departments:
    - name: IT
      size_pct: 10
      max_depth: 2
    - name: Accounting
      size_pct: 15
      max_depth: 2
    - name: Engineering
      size_pct: 25
      max_depth: 3
    - name: Marketing
      size_pct: 15
      max_depth: 2
    - name: Human Resources
      size_pct: 10
      max_depth: 2
    - name: Operations
      size_pct: 25
      max_depth: 3

  group_policy:
    department_group: true          # every department gets a group
    resource_groups_per_dept: 2-4   # SharePoint, file shares, etc.
    cross_dept_groups: 3-5          # VPN-Users, Printers-HQ, etc.

  contractor_pct: 10
  new_hire_pct: 5                   # hired in last 30 days

  shared_resources:
    - path: "\\\\fileserver\\finance\\Q4-Budget"
      description: "Q4 budget documents"
      access_group: SharePoint-Finance
    - path: "\\\\fileserver\\engineering\\designs"
      description: "Engineering design files"
      access_group: Engineering-Staff
    - path: "\\\\fileserver\\hr\\personnel"
      description: "HR personnel files (restricted)"
      access_group: HR-Confidential

  protected_accounts:
    - username: administrator
      reason: "Built-in admin"
    - username: krbtgt
      reason: "Kerberos service"

  service_accounts:
    - username: svc-praxova-agent
      description: "Praxova IT Agent service account"
      ou: "OU=Service Accounts,DC=montanifarms,DC=com"
    - username: svc-praxova-ldap
      description: "Praxova LDAP bind account"
      ou: "OU=Service Accounts,DC=montanifarms,DC=com"
```

```yaml
# profiles/enterprise.yaml (5000 users, for load testing)
organization:
  name: "Montani Farms"
  domain: "montanifarms.com"
  size: 5000

  regional_offices:
    - name: HQ
      size_pct: 40
      location: "Charleston, WV"
    - name: West
      size_pct: 30
      location: "Portland, OR"
    - name: Europe
      size_pct: 30
      location: "London, UK"

  departments:
    - name: IT
      size_pct: 8
      max_depth: 5
      sub_teams:
        - Infrastructure
        - Development
        - Security
        - Helpdesk
    - name: Engineering
      size_pct: 20
      max_depth: 5
      sub_teams:
        - Mechanical
        - Electrical
        - Software
        - QA
    # ... each department has sub-teams,
    # regional variations, realistic org chart depth

  group_policy:
    department_group: true
    sub_team_groups: true
    resource_groups_per_dept: 5-10
    cross_dept_groups: 10-20
    regional_groups: true            # VPN-West, Printers-London, etc.

  contractor_pct: 12
  new_hire_pct: 3
  pending_termination_pct: 2         # Accounts to be disabled
```

### What the Org Generator Produces

From a profile, the org generator creates:

1. **Complete OU structure** — nested OUs matching department/sub-team hierarchy
2. **Groups** — department groups, resource groups, cross-department groups, regional
3. **User slots** — positions in the org chart to be filled by the user generator
4. **Manager chains** — realistic reporting hierarchy (directors → managers → staff)
5. **Group membership rules** — which groups each position should belong to
6. **Shared resources** — file paths and associated access groups

---

## 5. User Generator

### Persona Engine

Each generated user gets a persona that determines how they write tickets. The
persona is a rich model, not a single trait flag.

```yaml
persona:
  tech_literacy: low        # low | medium | high
  communication: terse      # terse | casual | formal | verbose
  patience: low             # affects urgency inflation
  accuracy: medium          # how often they provide correct info
  self_service: false       # did they try anything before submitting?
  language_quirks:
    - drops_punctuation
    - all_lowercase
    - uses_ellipsis
```

**Persona distributions** — most organizations look like this:

```yaml
# personas/distributions.yaml
tech_literacy:
  low: 0.40          # Most users
  medium: 0.40
  high: 0.20         # IT staff, engineers

communication:
  terse: 0.25
  casual: 0.40       # Most common
  formal: 0.25
  verbose: 0.10

patience:
  low: 0.30
  medium: 0.50
  high: 0.20

accuracy:
  low: 0.20          # Wrong usernames, vague descriptions
  medium: 0.50
  high: 0.30         # All info correct and complete

# Department overrides
overrides:
  IT:
    tech_literacy: { low: 0.05, medium: 0.35, high: 0.60 }
  Engineering:
    tech_literacy: { low: 0.10, medium: 0.40, high: 0.50 }
  Human Resources:
    communication: { formal: 0.60, casual: 0.30, terse: 0.05, verbose: 0.05 }
```

At 50 users, you can hand-assign personas. At 5,000, the system distributes them
according to these probabilities, with department-specific overrides.

### User Output Example

```yaml
# Generated user with persona
- username: jane.doe
  first_name: Jane
  last_name: Doe
  email: jane.doe@montanifarms.com
  department: Accounting
  title: Senior Accountant
  manager: bob.smith
  ou: "OU=Accounting,OU=Users,DC=montanifarms,DC=com"
  groups:
    - Accounting-Staff
    - SharePoint-Finance
    - Printers-HQ
  hire_date: "2023-06-15"
  persona:
    tech_literacy: low
    communication: casual
    patience: low
    accuracy: medium
    self_service: false
    language_quirks:
      - drops_punctuation
      - abbreviates_words
```

### Scaling User Generation

| Scale | Method | Notes |
|-------|--------|-------|
| 1-50 | Hand-crafted | Each user has a story, review all |
| 50-500 | Profile-driven + Faker | Faker for names, profile for structure |
| 500-5000 | Fully programmatic | Faker + persona distribution + org rules |
| 5000+ | Batch programmatic | Same as above with regional variations |

```python
# Example: programmatic user generation at scale
from faker import Faker

fake = Faker()

def generate_user(department, position, groups, rng):
    first = fake.first_name()
    last = fake.last_name()
    username = f"{first.lower()}.{last.lower()}"

    persona = assign_persona(department, rng)

    return User(
        username=username,
        first_name=first,
        last_name=last,
        email=f"{username}@montanifarms.com",
        department=department.name,
        title=position.title,
        manager=position.manager_username,
        ou=department.ou_path,
        groups=groups,
        persona=persona,
    )
```

---

## 6. Ticket Generator

### Ticket Templates

Templates define the TYPES of tickets that can be generated, with slots for
variation. These are the patterns the generator uses — not the tickets themselves.

```yaml
# templates/password_reset.yaml
templates:
  - id: password_reset_basic
    ticket_type: password_reset
    difficulty: easy
    variations:
      - short_description: "Forgot my password"
        description: "I forgot my password and can't log in. My username is {username}."
      - short_description: "can't login"
        description: "cant log in again!!! this is the 3rd time this week"
      - short_description: "Password expired"
        description: >
          Hi, my password expired over the weekend and now I'm completely
          locked out. Can someone please reset it? Thanks, {first_name}
      - short_description: "URGENT - locked out of account"
        description: >
          I have a presentation in 30 minutes and I can't get into my
          computer. Please help ASAP!!

  - id: password_reset_ambiguous
    ticket_type: password_reset
    difficulty: medium
    notes: "Could be password reset OR account unlock"
    variations:
      - short_description: "Account issue"
        description: >
          Something is wrong with my account. I keep trying to log in but
          it says my credentials are wrong. I haven't changed anything.
      - short_description: "Login problem after vacation"
        description: >
          Just got back from 2 weeks vacation and can't log in. Not sure
          if my password expired or what.

  - id: password_reset_for_someone_else
    ticket_type: password_reset
    difficulty: medium
    notes: "Manager requesting reset for their direct report"
    variations:
      - short_description: "Password reset for {affected_user}"
        description: >
          Hi, I'm {first_name} {last_name} ({title}). Can you please reset
          the password for {affected_first_name} {affected_last_name}?
          They're on my team and can't get to their computer right now.
```

```yaml
# templates/escalation.yaml
templates:
  - id: hardware_issue
    ticket_type: unknown
    should_escalate: true
    escalation_reason: "Hardware issue outside agent capability"
    variations:
      - short_description: "Computer broken"
        description: >
          my computer won't turn on this morning. I already tried the power
          button and checked the cables.
      - short_description: "laptop screen cracked"
        description: "I dropped my laptop and the screen is cracked."

  - id: software_install
    ticket_type: unknown
    should_escalate: true
    escalation_reason: "Software installation outside current agent capability"
    variations:
      - short_description: "Need Photoshop installed"
        description: >
          Hi, I need Adobe Photoshop installed on my workstation for the
          marketing campaign materials.

  - id: disguised_ticket
    ticket_type: password_reset
    difficulty: hard
    notes: "Appears to be one type but is actually another"
    variations:
      - short_description: "Can't access email"
        description: >
          I can't get into my email this morning. It keeps saying wrong
          password but I didn't change it. Also my Outlook is showing
          some weird error about certificates.
        # Actually a password reset, not an email/certificate issue

  - id: multi_action_onboarding
    ticket_type: unknown
    should_escalate: true
    difficulty: hard
    notes: "Multiple actions — current agent handles one per ticket"
    variations:
      - short_description: "New employee onboarding - {affected_user}"
        description: >
          New employee {affected_first_name} {affected_last_name} starting
          in {department}. Need: password reset, VPN access, {group_name}
          group, and access to {resource_path}.
```

### Ticket Distribution Model

At scale, you need realistic patterns, not uniform randomness.

```yaml
# Part of organization profile
ticket_distribution:
  # Type distribution (what kinds of tickets)
  types:
    password_reset: 0.45
    group_access_add: 0.18
    group_access_remove: 0.07
    file_permission: 0.08
    unknown_escalate: 0.22

  # Difficulty distribution (how hard to classify)
  difficulty:
    easy: 0.50          # Clear intent, all info provided
    medium: 0.30        # Some ambiguity or missing info
    hard: 0.15          # Disguised type, vague, multi-action
    adversarial: 0.05   # Actively misleading or edge case

  # Temporal patterns (when tickets arrive)
  temporal:
    monday_spike: 1.8       # Monday morning password resets
    friday_dip: 0.6         # Less activity Fridays
    month_start_spike: 1.4  # New hire onboarding
    quarter_end_spike: 1.3  # Access reviews

  # Relationship patterns
  relationships:
    manager_submits_for_report: 0.15  # Caller ≠ affected user
    repeat_caller_pct: 0.20           # Same user, multiple tickets
    batch_onboarding: occasional      # 5-10 new hires at once
```

### Generator Modes

| Mode | Method | Scale | Use Case |
|------|--------|-------|----------|
| Template | String substitution + persona transforms | 50-200 tickets | Development, demos |
| LLM-Assisted | Use Claude/GPT-4 to rewrite templates naturally | 200-2000 tickets | Prompt tuning |
| LLM-Bulk | LLM generates from persona descriptions | 2000-50000 tickets | Fine-tuning, load testing |

**Template mode** — fast, deterministic, good for development:

```python
def generate_password_reset(user, template, rng):
    """Generate a password reset ticket from template + user data."""
    variation = rng.choice(template["variations"])

    description = variation["description"].format(
        username=user.username,
        first_name=user.first_name,
        last_name=user.last_name,
        title=user.title,
        department=user.department,
        manager_name=get_manager_name(user),
    )

    # Apply persona voice
    description = apply_persona(description, user.persona)

    return Ticket(
        short_description=variation["short_description"].format(**user.dict()),
        description=description,
        caller=user.username,
        urgency=weighted_choice(rng, [1, 2, 2, 3, 3, 3]),
    )


def apply_persona(text, persona):
    """Transform text to match user's writing persona."""
    if persona.communication == "terse":
        text = shorten(text)
    if "drops_punctuation" in persona.language_quirks:
        text = drop_punctuation(text)
    if "all_lowercase" in persona.language_quirks:
        text = text.lower()
    if persona.patience == "low":
        text = add_urgency(text)
    return text
```

**LLM-assisted mode** — higher quality, for tuning datasets:

```
Generate 20 password reset tickets with the following constraints:

Callers (pick randomly):
- jane.doe (Accounting, casual writer, not tech-savvy, impatient)
- bob.smith (Accounting Manager, formal, not tech-savvy)
- mike.chen (IT Director, technical, formal, concise)

Requirements:
- Vary writing style: formal to barely literate
- Vary completeness: some include username, some don't
- Vary urgency: some panicked, some routine
- Include 2-3 that LOOK like password resets but might actually be
  something else (account lockout from failed MFA, expired cert
  blocking Outlook, etc.)
- Each ticket should feel like a real person wrote it

Output as YAML with short_description, description, caller, and
recommended classification label.
```

Human reviews LLM output, corrects misclassifications, adds to ground truth.
LLM generates volume, human ensures quality.

---

## 7. Ground Truth Labels

Every generated ticket gets a known-correct classification. This is the answer key
for measuring agent accuracy.

```yaml
# ground-truth/batch-001.yaml
metadata:
  batch_id: "batch-001"
  generated_date: "2026-02-20"
  generator_version: "1.0"
  profile: "dev.yaml"
  total_tickets: 50
  distribution:
    password_reset: 20
    group_access_add: 12
    group_access_remove: 6
    file_permission: 4
    unknown_escalate: 8

tickets:
  - id: TEST-001
    template_id: password_reset_basic
    short_description: "cant login again"
    description: >
      i keep getting locked out of my account every morning.
      can someone fix this? this is jane from accounting
    caller: jane.doe
    urgency: 2
    expected:
      ticket_type: password_reset
      confidence_min: 0.8
      affected_user: jane.doe
      target_group: null
      target_resource: null
      should_escalate: false
      escalation_reason: null

  - id: TEST-002
    template_id: group_access_add_direct
    short_description: "Need access to Engineering SharePoint"
    description: >
      Hi team, I just transferred from Marketing to Engineering and I
      need access to the Engineering SharePoint site. My new manager
      is Lisa Wang. Thanks!
    caller: sarah.kim
    urgency: 3
    expected:
      ticket_type: group_access_add
      confidence_min: 0.7
      affected_user: sarah.kim
      target_group: SharePoint-Engineering
      should_escalate: false

  - id: TEST-003
    template_id: hardware_issue
    short_description: "computer broken"
    description: >
      my computer won't turn on this morning. I already tried the
      power button and unplugging it and plugging it back in.
    caller: bob.smith
    urgency: 2
    expected:
      ticket_type: unknown
      should_escalate: true
      escalation_reason: "Hardware issue outside agent capability"

  - id: TEST-004
    template_id: password_reset_ambiguous
    short_description: "Something wrong with my login"
    description: >
      hey so i was trying to log into my computer and it kept saying
      wrong password even though im sure its right. i havent changed
      anything. this started after lunch.
    caller: tom.rivera
    urgency: 2
    expected:
      ticket_type: password_reset
      confidence_min: 0.6
      affected_user: tom.rivera
      should_escalate: false
      notes: "Ambiguous — could be expired or lockout. Treat as reset."

  - id: TEST-005
    template_id: group_access_add_vague
    short_description: "need access to some files"
    description: >
      my boss said to email you about getting into some folder.
      i think its the budget stuff? im in accounting
    caller: jane.doe
    urgency: 3
    expected:
      ticket_type: group_access_add
      confidence_min: 0.5
      affected_user: jane.doe
      target_group: null
      should_escalate: true
      escalation_reason: "Insufficient information to determine target"
      notes: "Vague request. Agent should clarify or escalate."

  # ... more tickets with full labels
```

---

## 8. Evaluation Engine

### Purpose

Measure classification accuracy quantitatively so that prompt changes, few-shot
example updates, and model swaps can be evaluated objectively. No more "I think it
got better" — we know exactly what the numbers are.

### Process

```
For each ticket in ground truth dataset:
    1. Submit to classifier (TicketClassifier.classify())
    2. Capture: predicted type, confidence, affected_user, target_group
    3. Compare predicted vs expected
    4. Record: correct/incorrect, confidence delta, field-level accuracy

Aggregate:
    - Overall accuracy
    - Per-type precision and recall
    - Confidence calibration
    - Escalation accuracy
    - Field extraction accuracy
```

### Metrics

| Metric | Description | Target |
|--------|-------------|--------|
| Overall accuracy | % of tickets classified correctly | > 90% |
| Per-type precision | Correct predictions / total predictions of that type | > 85% |
| Per-type recall | Correct predictions / total actual tickets of that type | > 85% |
| Confidence calibration | Stated confidence matches actual accuracy | Within 10% |
| Escalation precision | % of escalated tickets that truly needed escalation | > 80% |
| Escalation recall | % of tickets needing escalation that were escalated | > 95% |
| False positive rate | Tickets agent tried to handle but shouldn't have | < 5% |
| Field extraction | Correct affected_user, target_group, etc. | > 90% |

### Confidence Calibration

If the agent reports 0.95 confidence, it should be correct ~95% of the time. If it's
actually correct only 70% of the time at stated 0.95 confidence, the confidence is
miscalibrated and the escalation threshold needs adjustment.

```
Confidence Bucket | Predictions | Correct | Actual Accuracy | Calibrated?
0.90 - 1.00      | 45          | 43      | 95.6%           | ✅ Yes
0.80 - 0.89      | 30          | 25      | 83.3%           | ✅ Close
0.70 - 0.79      | 15          | 10      | 66.7%           | ⚠️ Overconfident
0.60 - 0.69      | 8           | 4       | 50.0%           | ❌ Overconfident
< 0.60           | 2           | 0       | 0%              | ✅ Correctly uncertain
```

This data directly informs the escalation threshold. If the agent is overconfident
in the 0.70-0.79 range, raise the threshold to 0.80.

### Report Output

```
=== Classification Evaluation Report ===
Date: 2026-03-01
Model: llama3.1:8b
Dataset: batch-001 (50 tickets)
Prompt version: v1.3

OVERALL ACCURACY: 88.0% (44/50)

BY TYPE:
  password_reset:      95.0% precision, 90.0% recall (19/20)
  group_access_add:    83.3% precision, 83.3% recall (10/12)
  group_access_remove: 100%  precision, 83.3% recall (5/6)
  file_permission:     75.0% precision, 75.0% recall (3/4)
  unknown/escalate:    87.5% precision, 87.5% recall (7/8)

CONFIDENCE CALIBRATION:
  Stated > 0.9:  96% actual (well calibrated)
  Stated 0.7-0.9: 78% actual (slightly overconfident)
  Stated < 0.7:  escalated correctly in 90% of cases

FIELD EXTRACTION:
  affected_user correct: 91%
  target_group correct:  78% (needs improvement)
  target_resource correct: 75% (needs improvement)

RECOMMENDATIONS:
  1. Add few-shot examples for group_access_add with vague descriptions
  2. Raise escalation threshold from 0.6 to 0.7
  3. Add template for "transfer between departments"
```

### Comparative Reporting (A/B Testing)

The key feature at scale. You're not just asking "how good is classification?" —
you're asking "did this prompt change make it better or worse?" with statistical
significance.

```bash
praxova-testgen compare \
    --baseline results/run-v1.2.json \
    --candidate results/run-v1.3.json
```

Output:

```
=== A/B Comparison: v1.2 → v1.3 ===

OVERALL: 84.0% → 88.0% (+4.0%) ✅ IMPROVED

BY TYPE CHANGES:
  password_reset:      90.0% → 95.0% (+5.0%) ✅
  group_access_add:    75.0% → 83.3% (+8.3%) ✅
  group_access_remove: 83.3% → 100%  (+16.7%) ✅
  file_permission:     75.0% → 75.0% (no change)
  unknown/escalate:    87.5% → 87.5% (no change)

REGRESSIONS: None

NEW CORRECT (tickets that failed before, pass now):
  TEST-012: group_access_add (was misclassified as file_permission)
  TEST-027: group_access_remove (was unknown, now correctly classified)

CONFIDENCE SHIFT:
  Mean confidence: 0.78 → 0.82 (+0.04)
  Calibration improved in 0.70-0.79 bucket
```

---

## 9. Output Adapters

### Design

Adapters are pluggable. Each takes internal data models and produces
platform-specific output. Adding a new target (Jira, Azure AD, Okta) is a new
adapter class, not a rewrite.

```python
class OutputAdapter(ABC):
    """Base class for all output adapters."""

    @abstractmethod
    def export_users(self, users: list[User], path: Path) -> None:
        """Write users to a file in platform-specific format."""
        ...

    @abstractmethod
    def export_tickets(self, tickets: list[Ticket], path: Path) -> None:
        """Write tickets to a file in platform-specific format."""
        ...

    @abstractmethod
    def submit_users(self, users: list[User], connection: dict) -> None:
        """Push users to a live system via API."""
        ...

    @abstractmethod
    def submit_tickets(self, tickets: list[Ticket], connection: dict) -> None:
        """Push tickets to a live system via API."""
        ...
```

### Active Directory Adapter

Generates PowerShell scripts for AD user/group creation:

```powershell
# Generated by: praxova-testgen users --adapter ad --output create-users.ps1

# Create OUs
New-ADOrganizationalUnit -Name "Users" -Path "DC=montanifarms,DC=com"
New-ADOrganizationalUnit -Name "IT" -Path "OU=Users,DC=montanifarms,DC=com"
New-ADOrganizationalUnit -Name "Accounting" -Path "OU=Users,DC=montanifarms,DC=com"
# ...

# Create Groups
New-ADGroup -Name "VPN-Users" -GroupScope Global `
    -Path "OU=Groups,DC=montanifarms,DC=com"
New-ADGroup -Name "Accounting-Staff" -GroupScope Global `
    -Path "OU=Groups,DC=montanifarms,DC=com"
# ...

# Create Users
New-ADUser -Name "Jane Doe" -SamAccountName "jane.doe" `
    -UserPrincipalName "jane.doe@montanifarms.com" `
    -GivenName "Jane" -Surname "Doe" `
    -Department "Accounting" -Title "Senior Accountant" `
    -Manager "bob.smith" `
    -Path "OU=Accounting,OU=Users,DC=montanifarms,DC=com" `
    -AccountPassword (ConvertTo-SecureString "TempP@ss123!" -AsPlainText -Force) `
    -Enabled $true -ChangePasswordAtLogon $true
# ...

# Add to Groups
Add-ADGroupMember -Identity "Accounting-Staff" -Members "jane.doe"
Add-ADGroupMember -Identity "SharePoint-Finance" -Members "jane.doe"
Add-ADGroupMember -Identity "Printers-HQ" -Members "jane.doe"
# ...
```

### ServiceNow Adapter

REST API or CSV export for ServiceNow user and ticket creation:

```python
class ServiceNowAdapter(OutputAdapter):
    """Submit users and tickets via ServiceNow REST API."""

    def submit_users(self, users, connection):
        """Create sys_user records via REST API."""
        for user in users:
            payload = {
                "user_name": user.username,
                "first_name": user.first_name,
                "last_name": user.last_name,
                "email": user.email,
                "department": user.department,
                "title": user.title,
                "manager": user.manager,  # resolved to sys_id
                "active": "true",
            }
            self.client.post("/api/now/table/sys_user", json=payload)

    def submit_tickets(self, tickets, connection):
        """Create incident records via REST API."""
        for ticket in tickets:
            payload = {
                "caller_id": ticket.caller,  # resolved to sys_id
                "short_description": ticket.short_description,
                "description": ticket.description,
                "urgency": str(ticket.urgency),
                "assignment_group": connection["assignment_group"],
                "state": "1",  # New
                "category": "Inquiry / Help",
            }
            self.client.post("/api/now/table/incident", json=payload)
```

### Ground Truth Adapter

Exports labeled tickets as YAML for the evaluation engine:

```python
class GroundTruthAdapter(OutputAdapter):
    """Export tickets with expected classification labels."""

    def export_tickets(self, tickets, path):
        data = {
            "metadata": {
                "batch_id": self.batch_id,
                "generated_date": datetime.now().isoformat(),
                "total_tickets": len(tickets),
                "distribution": self._count_distribution(tickets),
            },
            "tickets": [t.to_ground_truth_dict() for t in tickets],
        }
        path.write_text(yaml.dump(data, default_flow_style=False))
```

### Cleanup

For repeatable testing, the system can wipe and recreate:

```bash
# Delete all test incidents (tagged with correlation_id)
praxova-testgen cleanup --target servicenow --connection config/snow.yaml

# Delete all test users/groups from AD and recreate
praxova-testgen cleanup --target ad --recreate --profile profiles/dev.yaml

# Both
praxova-testgen cleanup --target all --recreate --profile profiles/dev.yaml
```

---

## 10. Quality Principles for Synthetic Data

### What Makes Synthetic Data Realistic

1. **Inconsistent writing quality**: Real users range from professional emails to
   barely-punctuated stream of consciousness. Distribution should weight toward
   the messy end.

2. **Missing information**: Real tickets frequently omit critical details. "I need
   access to the thing" is more common than "Please add me to
   CN=SharePoint-Engineering,OU=Groups,DC=montanifarms,DC=com."

3. **Wrong information**: Users sometimes provide incorrect usernames, wrong group
   names, or describe their problem inaccurately. The agent must handle gracefully.

4. **Emotional content**: Frustrated, panicked, apologetic users. Shouldn't affect
   classification but often trips up models.

5. **Red herrings**: Irrelevant context mixed in. "I can't log in. Also, the coffee
   machine on the 3rd floor is broken again."

6. **Disguised types**: A ticket that appears to be one type but is actually another.
   "I can't access my email" is usually a password reset, not an email server issue.

7. **Multi-action requests**: "Set up the new hire with a password, VPN access, and
   add them to Engineering." Currently should escalate; eventually the agent
   should decompose them.

8. **Caller ≠ affected user**: Manager submitting on behalf of a team member. Agent
   needs to identify both the caller and the affected user.

### What Makes Synthetic Data Bad

1. **Too clean**: Every ticket perfectly formatted with all required fields.
2. **Too uniform**: All tickets sound like the same person wrote them.
3. **Unrealistic distribution**: 50% of each type instead of 60% password resets.
4. **No edge cases**: Only clear-cut tickets with obvious classifications.
5. **Template artifacts**: Tickets that obviously came from a template
   ("Hello, I am {first_name} and I need a {ticket_type}").

### Realistic Type Distribution

Based on typical IT helpdesk data:

```
password_reset:       40-50%  (most common by far)
group_access_add:     15-20%
group_access_remove:   5-10%
file_permission:       5-10%
unknown/escalate:     15-25%  (hardware, software, network, etc.)
```

---

## 11. Scaling Strategy

### Phase 1: Development (Current Need)

| Item | Count | Method |
|------|-------|--------|
| Users | 30-50 | Hand-crafted in profile + some generated |
| Groups | 15-20 | Hand-crafted in profile |
| Ticket templates | 20-30 | Hand-crafted |
| Generated tickets | 100-200 | Template mode |
| Ground truth labels | 100-200 | Hand-reviewed |

Purpose: Get classification loop working end-to-end, validate tools, record demos.

### Phase 2: Prompt Tuning (Post-Demo)

| Item | Count | Method |
|------|-------|--------|
| Users | 50-100 | Profile-driven with Faker |
| Groups | 30-50 | Profile-driven |
| Ticket templates | 50-100 | LLM-generated with human review |
| Generated tickets | 500-2000 | LLM-assisted mode |
| Ground truth labels | 500-2000 | LLM-labeled with human spot-check |

Purpose: Systematically improve few-shot prompts. A/B test prompt strategies.

### Phase 3: Model Fine-Tuning (Future)

| Item | Count | Method |
|------|-------|--------|
| Users | 500-1000 | Fully programmatic |
| Groups | 100-200 | Fully programmatic |
| Generated tickets | 10000-50000 | LLM bulk generation |
| Ground truth labels | 10000-50000 | LLM-labeled with statistical validation |

Purpose: Fine-tune Llama or other models for IT ticket classification. This is
where synthetic data quality really matters — the model learns whatever patterns
exist, including unrealistic artifacts.

### Phase 4: Load Testing (Future)

| Item | Count | Method |
|------|-------|--------|
| Users | 5000+ | Programmatic (Faker) |
| Generated tickets | 50000+ | Programmatic with temporal distribution |

Purpose: Validate "1 agent handles ~5000 users" business claim.

---

## 12. Relationship to Other Plans

### Supervised Learning Loop (Core Value Proposition)

The test data strategy IS the supervised learning loop in development form:

- Generate tickets → simulates real ticket flow
- Classify with agent → the product doing its job
- Compare to ground truth → human feedback
- Adjust prompts/examples → learning
- Re-evaluate → measure improvement

When deployed at a customer site, the same loop runs with real tickets and real
human feedback instead of synthetic data and pre-labeled ground truth.

### Model Tuning (Previous Discussion)

Large-scale synthetic data (Phase 3) feeds directly into model fine-tuning. The
quality principles in Section 10 are critical — fine-tuning on unrealistic data
produces a model that works on test data but fails on real tickets.

### DevOps Agent (DEV-INFRASTRUCTURE.md)

The DevOps agent's "create test tickets" capability uses this generator. The agent
calls `praxova-testgen tickets --adapter servicenow --submit` to populate the
queue for testing or demos.

### Video Strategy (DEV-INFRASTRUCTURE.md)

Video 3 ("Praxova in Action") uses generated tickets. They need to look realistic
on camera — a viewer should see the ticket and think "yeah, that's what a real
user would write."

### Customer Onboarding (Future)

Customers can use praxova-testgen during implementation to generate test data
matching their environment. "Here's 200 tickets that look like your organization.
Let's see how classification performs before we go live."

---

## 13. MCP Interface

### Purpose

The praxova-testgen system exposes an MCP (Model Context Protocol) server so that
Praxova agents — particularly the DevOps agent — can generate test data, run
evaluations, and manage test environments programmatically as part of automated
development cycles. No human needs to run CLI commands; the agent self-serves.

This follows the same pattern as every other Praxova capability: the agent requests
a capability, the system routes to the appropriate tool, and the operation executes
with full audit trail.

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     DevOps Agent                                 │
│                                                                 │
│  "Generate 50 test tickets and run classification evaluation"   │
│                                                                 │
│  1. Calls MCP tool: testgen_generate_users                      │
│  2. Calls MCP tool: testgen_sync_users (push to AD + ServiceNow)│
│  3. Calls MCP tool: testgen_generate_tickets                    │
│  4. Calls MCP tool: testgen_submit_tickets (push to ServiceNow) │
│  5. Waits for agent to process tickets                          │
│  6. Calls MCP tool: testgen_evaluate                            │
│  7. Calls MCP tool: testgen_compare (against last baseline)     │
│  8. Reports results                                             │
└──────────────────────────┬──────────────────────────────────────┘
                           │ MCP (stdio or SSE)
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                  praxova-testgen MCP Server                      │
│                                                                 │
│  Tools:                                                         │
│  ├── testgen_generate_users      Generate user corpus            │
│  ├── testgen_generate_tickets    Generate labeled tickets        │
│  ├── testgen_sync_users          Push users to AD + ServiceNow   │
│  ├── testgen_submit_tickets      Submit tickets to ServiceNow    │
│  ├── testgen_evaluate            Run classification evaluation   │
│  ├── testgen_compare             A/B compare evaluation runs     │
│  ├── testgen_cleanup             Wipe test data from targets     │
│  ├── testgen_status              Current state of test data      │
│  ├── testgen_list_profiles       Available org profiles          │
│  └── testgen_list_datasets       Available ground truth sets     │
│                                                                 │
│  Resources:                                                     │
│  ├── profiles://dev.yaml         Organization profiles           │
│  ├── ground-truth://batch-001    Labeled datasets                │
│  └── results://run-001           Evaluation results              │
└─────────────────────────────────────────────────────────────────┘
```

### MCP Tools

#### testgen_generate_users

Generate a user corpus from an organization profile.

```json
{
  "name": "testgen_generate_users",
  "description": "Generate test users from an organization profile. Returns user corpus that can be synced to AD and ServiceNow.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "profile": {
        "type": "string",
        "description": "Profile name (e.g., 'dev', 'demo', 'enterprise') or path"
      },
      "count": {
        "type": "integer",
        "description": "Override user count from profile (optional)"
      },
      "seed": {
        "type": "integer",
        "description": "Random seed for reproducible generation"
      }
    },
    "required": ["profile"]
  }
}
```

Response:
```json
{
  "status": "success",
  "users_generated": 50,
  "departments": 6,
  "groups": 18,
  "corpus_path": "generated/users-2026-02-15-001.yaml",
  "summary": {
    "IT": 5,
    "Accounting": 8,
    "Engineering": 12,
    "Marketing": 8,
    "Human Resources": 5,
    "Operations": 12
  }
}
```

#### testgen_generate_tickets

Generate labeled tickets from user corpus and templates.

```json
{
  "name": "testgen_generate_tickets",
  "description": "Generate test tickets with ground truth labels. Uses existing user corpus for realistic caller/context data.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "count": {
        "type": "integer",
        "description": "Number of tickets to generate"
      },
      "corpus": {
        "type": "string",
        "description": "Path to user corpus (from testgen_generate_users)"
      },
      "mode": {
        "type": "string",
        "enum": ["template", "llm-assisted", "llm-bulk"],
        "description": "Generation mode. template=fast/deterministic, llm-assisted=natural language, llm-bulk=high volume"
      },
      "distribution": {
        "type": "string",
        "enum": ["realistic", "uniform", "custom"],
        "description": "Ticket type distribution"
      },
      "difficulty_bias": {
        "type": "string",
        "enum": ["easy", "mixed", "hard", "adversarial"],
        "description": "Bias difficulty distribution (default: mixed)"
      },
      "seed": {
        "type": "integer",
        "description": "Random seed for reproducible generation"
      }
    },
    "required": ["count"]
  }
}
```

Response:
```json
{
  "status": "success",
  "tickets_generated": 200,
  "dataset_path": "ground-truth/batch-003.yaml",
  "distribution": {
    "password_reset": 90,
    "group_access_add": 36,
    "group_access_remove": 14,
    "file_permission": 16,
    "unknown_escalate": 44
  },
  "difficulty": {
    "easy": 100,
    "medium": 60,
    "hard": 30,
    "adversarial": 10
  }
}
```

#### testgen_sync_users

Push generated users to target systems (AD and/or ServiceNow).

```json
{
  "name": "testgen_sync_users",
  "description": "Sync generated user corpus to Active Directory and/or ServiceNow. Creates OUs, groups, users, and group memberships.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "corpus": {
        "type": "string",
        "description": "Path to user corpus"
      },
      "targets": {
        "type": "array",
        "items": {
          "type": "string",
          "enum": ["ad", "servicenow", "both"]
        },
        "description": "Target systems to sync to"
      },
      "ad_connection": {
        "type": "object",
        "description": "AD connection details (or use default from config)",
        "properties": {
          "server": { "type": "string" },
          "credential_ref": { "type": "string" }
        }
      },
      "servicenow_connection": {
        "type": "object",
        "description": "ServiceNow connection details (or use default)",
        "properties": {
          "instance_url": { "type": "string" },
          "credential_ref": { "type": "string" }
        }
      },
      "dry_run": {
        "type": "boolean",
        "description": "Preview what would be created without actually creating"
      }
    },
    "required": ["corpus", "targets"]
  }
}
```

Response:
```json
{
  "status": "success",
  "ad": {
    "ous_created": 8,
    "groups_created": 18,
    "users_created": 50,
    "memberships_set": 127
  },
  "servicenow": {
    "users_created": 50,
    "users_skipped": 0,
    "errors": []
  }
}
```

#### testgen_submit_tickets

Submit generated tickets to ServiceNow for agent processing.

```json
{
  "name": "testgen_submit_tickets",
  "description": "Submit generated tickets to ServiceNow as incidents. Tickets are assigned to the configured helpdesk group for agent pickup.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "dataset": {
        "type": "string",
        "description": "Path to ground truth dataset (from testgen_generate_tickets)"
      },
      "count": {
        "type": "integer",
        "description": "Submit only first N tickets (optional, for partial runs)"
      },
      "pacing": {
        "type": "string",
        "enum": ["immediate", "realistic", "slow"],
        "description": "Submission timing. immediate=all at once, realistic=simulated arrival pattern, slow=one per minute"
      },
      "servicenow_connection": {
        "type": "object",
        "description": "ServiceNow connection (or use default)"
      }
    },
    "required": ["dataset"]
  }
}
```

Response:
```json
{
  "status": "success",
  "tickets_submitted": 50,
  "incident_numbers": ["INC0010001", "INC0010002", "..."],
  "assignment_group": "Helpdesk",
  "pacing": "immediate",
  "estimated_processing_time": "5-10 minutes"
}
```

#### testgen_evaluate

Run classification evaluation against a labeled dataset.

```json
{
  "name": "testgen_evaluate",
  "description": "Run the classifier against a labeled ground truth dataset and compute accuracy metrics. Can evaluate against a live classifier endpoint or replay from stored results.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "dataset": {
        "type": "string",
        "description": "Path to ground truth dataset"
      },
      "classifier_url": {
        "type": "string",
        "description": "Classifier API endpoint (default: from config)"
      },
      "run_id": {
        "type": "string",
        "description": "Custom run ID (default: auto-generated timestamp)"
      },
      "save_results": {
        "type": "boolean",
        "description": "Save detailed results for later comparison (default: true)"
      }
    },
    "required": ["dataset"]
  }
}
```

Response:
```json
{
  "status": "success",
  "run_id": "run-2026-02-15-001",
  "results_path": "results/run-2026-02-15-001.json",
  "summary": {
    "total_tickets": 200,
    "overall_accuracy": 0.88,
    "by_type": {
      "password_reset": { "precision": 0.95, "recall": 0.90 },
      "group_access_add": { "precision": 0.83, "recall": 0.83 },
      "group_access_remove": { "precision": 1.00, "recall": 0.83 },
      "file_permission": { "precision": 0.75, "recall": 0.75 },
      "unknown_escalate": { "precision": 0.88, "recall": 0.88 }
    },
    "escalation_recall": 0.95,
    "false_positive_rate": 0.03,
    "mean_confidence": 0.82,
    "recommendations": [
      "Add few-shot examples for vague group_access_add",
      "Raise escalation threshold from 0.6 to 0.7"
    ]
  }
}
```

#### testgen_compare

Compare two evaluation runs for A/B testing.

```json
{
  "name": "testgen_compare",
  "description": "Compare two evaluation runs to measure the impact of prompt changes, model swaps, or configuration updates.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "baseline": {
        "type": "string",
        "description": "Path to baseline results (the 'before')"
      },
      "candidate": {
        "type": "string",
        "description": "Path to candidate results (the 'after')"
      }
    },
    "required": ["baseline", "candidate"]
  }
}
```

Response:
```json
{
  "status": "success",
  "overall_change": {
    "baseline_accuracy": 0.84,
    "candidate_accuracy": 0.88,
    "delta": 0.04,
    "direction": "improved"
  },
  "type_changes": {
    "password_reset": { "baseline": 0.90, "candidate": 0.95, "delta": 0.05 },
    "group_access_add": { "baseline": 0.75, "candidate": 0.83, "delta": 0.08 }
  },
  "regressions": [],
  "newly_correct": ["TEST-012", "TEST-027"],
  "newly_incorrect": []
}
```

#### testgen_cleanup

Remove test data from target systems for a fresh cycle.

```json
{
  "name": "testgen_cleanup",
  "description": "Remove test data from AD and/or ServiceNow. Enables clean re-runs without stale data.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "targets": {
        "type": "array",
        "items": {
          "type": "string",
          "enum": ["ad", "servicenow", "both"]
        }
      },
      "scope": {
        "type": "string",
        "enum": ["tickets_only", "users_only", "all"],
        "description": "What to clean. tickets_only preserves users for reuse."
      },
      "recreate": {
        "type": "boolean",
        "description": "After cleanup, regenerate and sync from profile"
      },
      "profile": {
        "type": "string",
        "description": "Profile to use for recreation (required if recreate=true)"
      }
    },
    "required": ["targets"]
  }
}
```

#### testgen_status

Report current state of test data across all systems.

```json
{
  "name": "testgen_status",
  "description": "Check the current state of test data: how many users in AD and ServiceNow, open tickets, latest evaluation results.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "verbose": {
        "type": "boolean",
        "description": "Include detailed breakdowns"
      }
    }
  }
}
```

Response:
```json
{
  "status": "success",
  "active_directory": {
    "test_users": 50,
    "test_groups": 18,
    "test_ous": 8,
    "connected": true
  },
  "servicenow": {
    "test_users": 50,
    "open_test_incidents": 12,
    "closed_test_incidents": 38,
    "connected": true
  },
  "local": {
    "profiles_available": ["dev", "demo", "enterprise"],
    "ground_truth_datasets": 3,
    "total_labeled_tickets": 450,
    "evaluation_runs": 5,
    "latest_run": {
      "run_id": "run-2026-02-15-001",
      "accuracy": 0.88,
      "date": "2026-02-15"
    }
  }
}
```

#### testgen_list_profiles / testgen_list_datasets

Discovery tools for the agent to know what's available.

```json
{
  "name": "testgen_list_profiles",
  "description": "List available organization profiles with their parameters."
}

{
  "name": "testgen_list_datasets",
  "description": "List available ground truth datasets with metadata."
}
```

### MCP Server Implementation

The MCP server wraps the same Python library that the CLI uses. Both interfaces
call the same generators, adapters, and evaluation engine.

```
┌───────────────────────────────────────────┐
│           praxova-testgen                  │
│                                           │
│  ┌─────────┐   ┌─────────┐               │
│  │  CLI     │   │  MCP    │               │
│  │ (Click)  │   │ Server  │               │
│  └────┬─────┘   └────┬────┘               │
│       │              │                     │
│       └──────┬───────┘                     │
│              ▼                             │
│  ┌─────────────────────┐                   │
│  │   Core Library      │                   │
│  │                     │                   │
│  │  generators/        │                   │
│  │  adapters/          │                   │
│  │  evaluation/        │                   │
│  │  models/            │                   │
│  └─────────────────────┘                   │
└───────────────────────────────────────────┘
```

The MCP server uses the FastMCP Python framework (or equivalent) with stdio
transport for local agent access:

```python
# src/praxova_testgen/mcp_server.py

from fastmcp import FastMCP

mcp = FastMCP("praxova-testgen")

@mcp.tool()
async def testgen_generate_users(
    profile: str,
    count: int | None = None,
    seed: int | None = None,
) -> dict:
    """Generate test users from an organization profile."""
    org = load_profile(profile)
    if count:
        org.size = count
    users = UserGenerator(seed=seed).generate(org)
    corpus_path = save_corpus(users)
    return {
        "status": "success",
        "users_generated": len(users),
        "departments": len(org.departments),
        "groups": count_groups(users),
        "corpus_path": str(corpus_path),
        "summary": department_summary(users),
    }

@mcp.tool()
async def testgen_generate_tickets(
    count: int,
    corpus: str | None = None,
    mode: str = "template",
    distribution: str = "realistic",
    difficulty_bias: str = "mixed",
    seed: int | None = None,
) -> dict:
    """Generate test tickets with ground truth labels."""
    users = load_corpus(corpus) if corpus else load_latest_corpus()
    templates = load_all_templates()
    generator = TicketGenerator(
        users=users,
        templates=templates,
        mode=mode,
        distribution=distribution,
        difficulty_bias=difficulty_bias,
        seed=seed,
    )
    tickets = generator.generate(count)
    dataset_path = GroundTruthAdapter().export(tickets)
    return {
        "status": "success",
        "tickets_generated": len(tickets),
        "dataset_path": str(dataset_path),
        "distribution": type_distribution(tickets),
        "difficulty": difficulty_distribution(tickets),
    }

@mcp.tool()
async def testgen_evaluate(
    dataset: str,
    classifier_url: str | None = None,
    run_id: str | None = None,
    save_results: bool = True,
) -> dict:
    """Run classification evaluation against labeled dataset."""
    ground_truth = load_dataset(dataset)
    runner = EvaluationRunner(classifier_url or default_classifier_url())
    results = await runner.evaluate(ground_truth)
    
    if save_results:
        results_path = save_evaluation(results, run_id)
    
    return {
        "status": "success",
        "run_id": results.run_id,
        "results_path": str(results_path) if save_results else None,
        "summary": results.to_summary_dict(),
    }

# ... more tool implementations

if __name__ == "__main__":
    mcp.run()
```

### MCP Registry Entry

The testgen MCP server registers in Praxova's MCP registry (per existing
Agora architecture) so agents can discover and use it:

```yaml
# Agora MCP registry entry
registry_id: praxova-testgen
name: "Praxova Test Data Generator"
description: "Generate test users, tickets, and run classification evaluations"
command: python
args:
  - "-m"
  - "praxova_testgen.mcp_server"
transport: stdio
```

### Agent Workflow Example

A complete dev cycle orchestrated by the DevOps agent:

```
DevOps Agent receives: "Run a full test cycle with 100 tickets"

1. testgen_status()
   → Check current state. Any stale data to clean?

2. testgen_cleanup(targets=["servicenow"], scope="tickets_only")
   → Clear old test tickets, keep users

3. testgen_generate_tickets(count=100, mode="template", seed=42)
   → Generate 100 labeled tickets
   → Returns dataset_path: "ground-truth/batch-004.yaml"

4. testgen_submit_tickets(dataset="ground-truth/batch-004.yaml", pacing="immediate")
   → Submit all 100 to ServiceNow
   → Returns incident numbers

5. [Wait for helpdesk agent to process tickets]
   → Poll ServiceNow or use testgen_status() to check progress

6. testgen_evaluate(dataset="ground-truth/batch-004.yaml")
   → Run classifier against labeled data
   → Returns accuracy: 88%, recommendations

7. testgen_compare(baseline="results/run-003.json", candidate="results/run-004.json")
   → Compare to previous run
   → Returns: +4% improvement, no regressions

8. Report results to human operator or log to audit trail
```

### Updated Project Structure

```
test-data/
├── pyproject.toml
├── README.md
│
├── profiles/                         # Organization profiles
├── templates/                        # Ticket templates
├── personas/                         # Writing style definitions
│
├── src/
│   └── praxova_testgen/
│       ├── __init__.py
│       ├── cli.py                    # Click CLI entry point
│       ├── mcp_server.py            # MCP server entry point  ← NEW
│       │
│       ├── models/
│       ├── generators/
│       ├── adapters/
│       ├── evaluation/
│       └── utils/
│
├── ground-truth/
├── results/
└── tests/
```

---

## 14. Open Questions

1. **ServiceNow user creation method**: REST API is most scriptable but requires
   admin credentials. CSV import is simpler but manual. Prefer REST for automation.

2. **Ticket submission timing**: All at once for batch evaluation, or drip-fed to
   simulate realistic arrival patterns for demo videos?

3. **Existing Setup-TestEnvironment.ps1**: Replace entirely with generator output,
   or keep as a quick-start and use generator for comprehensive data?

4. **LLM for generation**: Claude (higher quality, API cost) or local Ollama (free,
   lower quality)? Could use Claude for gold datasets and Ollama for bulk.

5. **Version control**: Ground truth datasets in git (small batches) or separate
   storage (large Phase 3+ datasets)?

6. **Cross-validation**: Split ground truth into train/test sets? Some examples
   used as few-shot prompts, others held out for evaluation?

7. **Persona voice quality**: How much effort on text transformation (persona
   engine) vs LLM rewriting? Template mode with good personas might be sufficient
   for Phase 1-2, with LLM mode reserved for Phase 3+.

8. **Package distribution**: Ship praxova-testgen as part of the main repo or as
   a separate package? Leaning toward subdirectory of main repo for now, separate
   package later if customers use it.
