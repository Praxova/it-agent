# Phase 5a — Dynamic Classification from Portal Example Sets

## Context for the Intermediary Chat

This prompt was produced by a security architecture session. It describes WHAT needs
to exist and WHY. Your job is to compare against the current codebase and produce a
Claude Code implementation prompt.

**Background:** This is TD-001 from the technical debt tracker. The agent's ticket
classifier currently has hardcoded prompt categories and a `_TYPE_MAP` normalization
dictionary that maps LLM output strings to internal ticket types. Adding a new ticket
type requires a code change and redeployment. The goal is to move ALL of this to the
portal — categories, example sets, and the mapping — so that new ticket types can be
added entirely through the portal UI with no code changes to the agent.

This is a product capability feature, not a security feature, but it was prioritized
alongside the security work because it directly affects the classification improvement
loop, which is Praxova's core value proposition.

**Dependency:** None strictly, but the portal must have the example set management
UI in place. The intermediary chat should check whether this UI already exists (the
ROADMAP indicates "Example sets in portal" is marked ✅ shipped). The question is
whether the portal's example set API is wired into the agent's classifier.

---

## Specification

### 1. Portal API for Classification Configuration

The portal should expose an API endpoint that the agent calls at startup (and
periodically refreshes) to get the current classification configuration:

```
GET /api/classification/config
```

Response:

```json
{
  "categories": [
    {
      "name": "password_reset",
      "display_name": "Password Reset",
      "description": "User needs their password reset or changed",
      "workflow_name": "password-reset-workflow",
      "enabled": true
    },
    {
      "name": "group_access",
      "display_name": "Group / Access Request",
      "description": "User needs to be added to or removed from an AD group",
      "workflow_name": "group-access-workflow",
      "enabled": true
    },
    {
      "name": "account_unlock",
      "display_name": "Account Unlock",
      "description": "User account is locked out and needs to be unlocked",
      "workflow_name": "account-unlock-workflow",
      "enabled": true
    }
  ],
  "examples": [
    {
      "category": "password_reset",
      "ticket_text": "I forgot my password and can't log in to my computer",
      "classification_notes": "Clear password reset request"
    },
    {
      "category": "password_reset",
      "ticket_text": "Need to reset pw for jsmith in accounting",
      "classification_notes": "Manager requesting reset for a report"
    },
    {
      "category": "group_access",
      "ticket_text": "Please add me to the VPN-Users group so I can work from home",
      "classification_notes": "Explicit group name mentioned"
    }
  ],
  "confidence_threshold": 0.7,
  "escalation_category": "escalation",
  "version": "2026-02-27T10:00:00Z"
}
```

**Key design points:**

- `categories` defines ALL valid classification outputs. The classifier prompt
  tells the LLM: "Classify this ticket into one of these categories." If a
  category is disabled, it's excluded from the prompt.
- `examples` are few-shot examples included in the classifier prompt. These are
  the organization-specific training data that make the classifier better over time.
- `confidence_threshold` is the minimum confidence score below which tickets are
  automatically escalated to human review.
- `escalation_category` is the category name used when the classifier can't
  determine the ticket type or confidence is below threshold.
- `version` is a timestamp used by the agent to detect when the configuration
  has changed (so it can refresh its cached prompt).

### 2. Agent Classifier Changes

The agent's `TicketClassifier` currently builds its prompt from hardcoded data.
It needs to be refactored to:

1. **Fetch classification config from portal** at startup
2. **Build the prompt dynamically** from the fetched categories and examples
3. **Cache the config** and refresh periodically (e.g., every 5 minutes, or
   when the version changes)
4. **Remove hardcoded categories and `_TYPE_MAP`** — all classification outputs
   are validated against the fetched category list

The intermediary chat MUST examine:

- The current `TicketClassifier` class — where the prompt is built, where
  categories are defined, where `_TYPE_MAP` lives
- How the classifier output is parsed and mapped to workflow routing
- How the confidence threshold is currently configured
- Whether there's an existing mechanism for the agent to fetch configuration
  from the portal at runtime (the agent startup sequence already calls
  `GET /api/agents/{name}/configuration`)

**Prompt construction from dynamic config:**

```python
def build_classification_prompt(config: ClassificationConfig) -> str:
    categories_section = "\n".join(
        f"- {cat.name}: {cat.description}"
        for cat in config.categories
        if cat.enabled
    )
    
    examples_section = "\n".join(
        f"Ticket: {ex.ticket_text}\nClassification: {ex.category}\n"
        for ex in config.examples
    )
    
    return f"""Classify the following IT support ticket into exactly one of these categories:

{categories_section}

If the ticket does not clearly match any category, or if you are uncertain, classify it as "{config.escalation_category}".

Here are examples of correctly classified tickets:

{examples_section}

Respond with a JSON object containing:
- "ticket_type": the category name (exactly as listed above)
- "confidence": a number between 0.0 and 1.0 indicating your confidence
- "affected_user": the username of the affected user (if mentioned)
- "target_resource": the target resource (group name, path, etc., if mentioned)
- "reasoning": a brief explanation of your classification

Ticket to classify:
{{ticket_text}}
"""
```

The exact prompt format should match what the current classifier uses — the
intermediary chat should examine the existing prompt and preserve its effective
patterns while replacing hardcoded content with dynamic content.

### 3. Category Validation

When the classifier returns a result, the agent should validate that the returned
`ticket_type` matches one of the enabled categories from the portal config.

```python
def validate_classification(result: dict, config: ClassificationConfig) -> dict:
    valid_categories = {cat.name for cat in config.categories if cat.enabled}
    valid_categories.add(config.escalation_category)
    
    if result["ticket_type"] not in valid_categories:
        # LLM returned a category that doesn't exist — treat as escalation
        logger.warning(
            f"Classifier returned unknown category '{result['ticket_type']}'. "
            f"Valid categories: {valid_categories}. Escalating."
        )
        result["ticket_type"] = config.escalation_category
        result["confidence"] = 0.0
    
    if result["confidence"] < config.confidence_threshold:
        # Below threshold — escalate for human review
        result["ticket_type"] = config.escalation_category
    
    return result
```

This replaces the `_TYPE_MAP` normalization. Instead of mapping LLM output strings
to canonical names, the prompt gives the LLM the exact category names and the
validation checks that the output matches. If the LLM hallucinates a category name,
it's caught and escalated rather than silently misrouted.

### 4. Portal UI for Example Management

The portal should have a UI for managing classification examples. The intermediary
chat should check whether this already exists (the ROADMAP says it does). If it
exists, verify it exposes the data through the API endpoint described in section 1.
If it doesn't exist, it needs to be built.

The UI should allow:
- Adding new categories with name, description, and linked workflow
- Enabling/disabling categories (disabled = excluded from prompt)
- Adding few-shot examples with ticket text and category assignment
- Editing and deleting examples
- Setting the confidence threshold
- Previewing the generated prompt (helpful for operators tuning the classifier)

### 5. Configuration Refresh

The agent should refresh its classification config periodically without requiring
a restart. Two approaches:

**Option A: Poll-based refresh.** The agent checks the portal's config endpoint
every N minutes (e.g., 5 minutes) and compares the `version` field. If the version
has changed, rebuild the prompt.

**Option B: Event-driven refresh.** The portal notifies the agent (via webhook or
the heartbeat response) when the config has changed. The agent then fetches the
new config.

For v1.0, **Option A** (polling) is simpler and consistent with the agent's
existing polling patterns (ServiceNow ticket polling, approval polling). The
5-minute interval means a new example added in the portal takes at most 5 minutes
to be used by the classifier. This is acceptable — example set changes are
infrequent operational adjustments, not real-time requirements.

---

## Testing

**Test 1: Dynamic categories in prompt**
1. Configure 3 categories in the portal
2. Start the agent
3. Verify the classifier prompt includes all 3 categories
4. Add a 4th category in the portal
5. Wait for refresh (or restart agent)
6. Verify the classifier prompt now includes 4 categories

**Test 2: Few-shot examples in prompt**
1. Add 3 examples for "password_reset" in the portal
2. Verify the classifier prompt includes all 3 examples
3. Submit a test ticket similar to one of the examples
4. Verify it classifies correctly with high confidence

**Test 3: Unknown category handling**
1. Submit a ticket that the LLM classifies into a non-existent category
   (this can be triggered by using a vague ticket description)
2. Verify: the agent catches the invalid category and escalates

**Test 4: Confidence threshold**
1. Set threshold to 0.9 in the portal
2. Submit a deliberately ambiguous ticket
3. Verify: classification confidence is below 0.9, ticket is escalated

**Test 5: Disabled category**
1. Disable the "account_unlock" category in the portal
2. Submit a ticket that would normally classify as account unlock
3. Verify: the classifier doesn't offer "account_unlock" as an option,
   ticket routes elsewhere or escalates

---

## Git Commit Guidance

```
feat(portal): classification config API endpoint
feat(agent): dynamic classification prompt from portal config
feat(agent): remove hardcoded categories and _TYPE_MAP
feat(agent): classification config caching and periodic refresh
feat(agent): validate classifier output against portal categories
test: dynamic classification integration tests
docs: update ARCHITECTURE classification section
```

### What NOT to Change

- Do not modify the LLM driver or inference configuration
- Do not modify the ServiceNow connector
- Do not modify the operation token system
- Do not modify how the agent routes classified tickets to workflows (the
  `workflow_name` field in the category config handles the routing — the
  dispatcher logic should already use this)
- Do not modify the portal's example set storage if it already exists —
  only add the API endpoint if it's missing
