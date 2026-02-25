# ADR-012: Dynamic Classification Training via Portal

**Status**: Accepted — Implemented  
**Date**: 2026-02-06  
**Decision Makers**: Alton  
**Depends On**: ADR-011 (Composable Workflows — Complete)  
**Resolves**: TD-001 (Hardcoded Classification Categories)

---

## 1. Problem Statement

The classification engine is the brain of every Lucid workflow, but customers can't
teach it anything. Today, the `ClassifyExecutor` has a hardcoded prompt with hardcoded
categories (`password_reset`, `group_access_add`, etc.) and three inline few-shot
examples baked into Python code. The Admin Portal already has Example Set and Ruleset
editors, and the agent export already carries example sets in its JSON payload — but
the classify executor ignores all of it.

This creates three concrete problems:

1. **No customization** — A customer who needs to classify "VPN access requests" or
   "mailbox provisioning" must modify Python code to add the category.

2. **Naming collisions** — The hardcoded prompt teaches the LLM underscore-style
   categories (`password_reset`) while dispatcher transitions use hyphen-style
   (`password-reset`), requiring a brittle `_TYPE_MAP` translation dict. This caused
   a silent routing failure during ADR-011 E2E testing.

3. **No iterative improvement** — When the LLM misclassifies a ticket, there's no
   way for an admin to add a corrective example and improve accuracy over time. The
   classification quality is frozen at whatever the developer hardcoded.

The goal: **everything a customer needs to work with the classification engine —
adding examples, changing categories, tuning prompts, configuring thresholds — must
be doable entirely through the Admin Portal with zero code changes.**

---

## 2. What Exists Today (Inventory)

### Portal Side (C# / Blazor)

| Component | Status | Notes |
|-----------|--------|-------|
| `ExampleSet` entity | ✅ Exists | Has Name, DisplayName, Description, TargetTicketType, IsActive |
| `Example` entity | ✅ Exists | Has TicketShortDescription, TicketDescription, ExpectedTicketType, ExpectedConfidence, ExpectedAffectedUser, ExpectedTargetGroup, etc. |
| `Examples/Index.razor` | ✅ Exists | Lists example sets |
| `Examples/Edit.razor` | ✅ Exists | Edits individual example set and its examples |
| `ExampleSetSeeder` | ✅ Exists | Seeds `password-reset-examples` and `it-dispatch-classification` sets |
| `WorkflowDefinition.ExampleSetId` | ✅ Exists | Links a workflow to its example set |
| Export API includes `example_sets` | ✅ Exists | Serialized in agent export JSON |

### Agent Side (Python)

| Component | Status | Notes |
|-----------|--------|-------|
| `ExampleSetExportInfo` model | ✅ Exists | Parses example sets from export |
| `ExampleExportInfo` model | ✅ Exists | Has `input_text`, `expected_output_json` |
| `ClassifyExecutor._build_classification_prompt()` | ❌ Hardcoded | Ignores export example sets entirely |
| `_TYPE_MAP` normalization dict | ❌ Bandaid | Translates LLM output to match transition conditions |
| `context._agent_export.example_sets` | ✅ Available | Data is there, just unused |

### Key Insight

The infrastructure is ~80% built. The portal can manage examples, the export carries
them, and the Python models parse them. The only gap is the last mile: the classify
executor reading and using the example data instead of its hardcoded prompt.

---

## 3. Design: Examples Define the Taxonomy

The central design principle is: **the example data itself defines the classification
taxonomy.** There is no separate "category configuration" screen. If your examples
demonstrate outputs with categories `password-reset`, `group-membership`, and
`vpn-access`, those become the valid categories. The LLM learns the taxonomy from
the examples, and the dispatcher transition conditions simply match those same strings.

This means:
- Adding a new ticket type = adding examples of that type
- Changing a category name = updating the examples (and matching transitions)
- Removing a category = removing/disabling those examples
- One source of truth for classification behavior

### Prompt Construction Algorithm

```
1. Read workflow's linked ExampleSet from export
2. Extract unique category names from examples' expected outputs
3. Build system instruction:
   "You are an IT helpdesk ticket classifier. Classify the ticket into
    one of these categories: {categories_from_examples}.
    If none fit, use 'unknown'."
4. For each active example, format as few-shot:
   "Ticket: {example.short_description} — {example.description}
    Classification: {example.expected_output_json}"
5. Append the actual ticket data
6. Parse LLM response — no normalization needed because the examples
   taught the LLM the exact strings the transitions expect
```

### Example Output JSON Format

The `expected_output_json` field on each Example stores what the LLM should output
for that example. This is the same JSON structure the classify step produces:

```json
{
  "ticket_type": "password-reset",
  "confidence": 0.95,
  "affected_user": "jsmith",
  "target_group": null,
  "target_resource": null,
  "reasoning": "Clear password reset request with username identified"
}
```

Because the examples use `password-reset` (matching the transition conditions),
and the LLM mimics the format it sees in examples, the output naturally matches
the dispatcher conditions. No `_TYPE_MAP` translation needed.

---

## 4. Implementation Phases

### Phase D1: Wire ClassifyExecutor to Dynamic Examples

**Goal**: Replace hardcoded prompt with example-set-driven prompt.

**Python Changes** (`classify.py`):

1. Modify `_build_classification_prompt()`:
   - Accept the workflow's example set from `context._agent_export.example_sets`
   - The step configuration includes `use_example_set` — use this to look up the
     correct example set by name
   - Extract categories dynamically from examples' `expected_output_json`
   - Format each example as a few-shot pair
   - If no example set is linked, fall back to a minimal generic prompt with
     `unknown` as the only category (forces escalation — safe default)

2. Remove `_TYPE_MAP` normalization dict entirely. The examples now teach the LLM
   the correct output format.

3. Remove the hardcoded category list from the system instruction. Categories come
   from examples.

4. Preserve the JSON parsing and validation logic (`_parse_classification_response`)
   — it's good code, just remove the mapping step.

**Step Configuration Schema**:
```json
{
  "use_example_set": "it-dispatch-classification",
  "extract_fields": ["ticket_type", "affected_user", "confidence"],
  "temperature": 0.0,
  "max_retries": 2
}
```

**Test**: Re-run E2E with same test tickets. Classification should produce
`password-reset` (not `password_reset`) because the `it-dispatch-classification`
example set uses hyphen-style names. No `_TYPE_MAP` involved.

**Estimated effort**: 2-3 hours

---

### Phase D2: Verify and Enhance Export Serialization

**Goal**: Ensure the export API serializes examples correctly for prompt building.

**Check / Fix** (`AgentExportService.cs`):

1. Verify `ExampleExportInfo` includes all fields needed for prompt construction:
   - `inputText` → should contain the ticket text (short_description + description)
   - `expectedOutputJson` → the full JSON the LLM should produce
   - `notes` → optional context for the admin, not sent to LLM

2. If the current export maps `Example` entity fields to `ExampleExportInfo`
   incorrectly (e.g., `inputText` is empty because the entity stores
   `TicketShortDescription` and `TicketDescription` separately), add a
   computed mapping that concatenates them:
   ```
   inputText = $"{example.TicketShortDescription} — {example.TicketDescription}"
   ```

3. If the entity stores structured fields (ExpectedTicketType as enum,
   ExpectedConfidence as decimal) rather than raw JSON, add a computed
   `ExpectedOutputJson` that serializes the structured fields into the
   JSON string the agent needs. Alternatively, add an `ExpectedOutputJson`
   column to the entity that the editor populates.

4. Ensure the workflow's linked `ExampleSetId` is included in the export
   so the agent knows which example set to use.

5. Ensure disabled/inactive examples are excluded from the export.

**Estimated effort**: 1-2 hours

---

### Phase D3: Enhance Example Editor UI

**Goal**: Make the Example editor a first-class tool for classification training.

**Current state**: `Examples/Index.razor` and `Examples/Edit.razor` exist.
Need evaluation for customer-readiness.

**Required capabilities**:

1. **Example Set list page** (`Examples/Index.razor`):
   - Show all example sets with name, description, example count, active status
   - Indicate which workflow(s) reference each set
   - Create / Clone / Delete actions

2. **Example Set editor** (`Examples/Edit.razor`):
   - Edit set metadata: name, description, active status
   - Show which workflow(s) reference this set
   - Add/edit/delete/reorder individual examples
   - Toggle individual examples active/inactive

3. **Individual example editor**:
   - **Input section**: Ticket short description and full description
     (what the LLM sees as the "ticket to classify")
   - **Expected output section**: Either a structured form (ticket_type dropdown,
     confidence slider, affected_user text, etc.) that generates the JSON, or
     a raw JSON editor for power users. Ideally both with a toggle.
   - **Preview**: Show the formatted prompt example as it will appear to the LLM
   - **Notes**: Free text explaining why this example exists

4. **Validation**:
   - Warn if an example set has fewer than 2 examples per category
   - Warn if expected output JSON is malformed
   - Warn if category names don't match any dispatcher transition conditions

5. **Test Classification** (nice-to-have for MVP):
   - "Test" button: Enter a ticket description, send it through the LLM with
     the current example set, show what the classifier would produce
   - Helps admins verify their examples teach the right behavior

**Estimated effort**: 4-6 hours

---

### Phase D4: Classify Step Properties in Workflow Designer

**Goal**: Let workflow designers configure classification from the step properties panel.

When a Classify node is selected in the workflow designer, the properties
panel should show:

1. **Example Set selector**: Dropdown of available example sets. Shows the
   linked set with an "Edit Examples →" link.

2. **Read-only summary**: "Using N examples across M categories"

3. **Model/Temperature override** (optional): If not set, uses agent's
   default LLM provider. Classification typically uses temperature 0.0.

4. **Retry configuration**: Max retries on parse failure, fallback behavior.

**Estimated effort**: 3-4 hours

---

## 5. Data Flow (After Implementation)

### Admin Creates Classification Examples
```
Portal → Examples/Edit.razor
  → Add example: "User jsmith forgot his password"
     Expected output: {"ticket_type": "password-reset", "confidence": 0.95, ...}
  → Save → Database (ExampleSet + Example rows)
```

### Agent Loads Configuration
```
Agent startup → GET /api/agents/by-name/test-agent/export
  → JSON includes example_sets with full example data
  → Classify step config says: use_example_set = "it-dispatch-classification"
```

### Classification at Runtime
```
ClassifyExecutor._build_classification_prompt():
  1. Look up "it-dispatch-classification" from export
  2. Scan examples → unique categories: ["password-reset", "group-membership",
     "file-permissions"]
  3. Build prompt with dynamic category list + few-shot examples
  4. LLM returns: {"ticket_type": "password-reset", ...}
  5. Matches transition "ticket_type == 'password-reset'" → pw-reset-sub
```

### Admin Adds a New Category (No Code Changes)
```
LLM encounters VPN request, classifies as "unknown" →
Admin adds 3-4 VPN examples with expected type "vpn-access" →
Admin adds "vpn-access" transition in dispatcher workflow →
Admin creates vpn-access sub-workflow →
Restart agent → VPN requests now classify correctly
```

---

## 6. Migration: Seeded Example Data

The seeded `it-dispatch-classification` example set uses the `Example` entity's
typed fields (`ExpectedTicketType` as an enum, `ExpectedConfidence` as a decimal).
These must be mapped to the `expectedOutputJson` format during export.

**Approach**: The export service generates `expectedOutputJson` from the typed
fields at serialization time. The category names in the generated JSON must use
the same format as dispatcher transition conditions (hyphen-style: `password-reset`,
not `password_reset`).

The existing `TicketType` enum may need to be mapped to string values matching
transition conditions. If the enum uses `PasswordReset` (PascalCase) internally,
the export mapping converts it to `password-reset` (kebab-case).

---

## 7. Testing Strategy

| Test | Validates |
|------|-----------|
| Unit: Dynamic prompt builds correct categories from examples | D1 |
| Unit: Empty example set produces safe fallback prompt | D1 |
| Unit: Disabled examples excluded from prompt | D1 |
| Integration: Export API includes examples with correct fields | D2 |
| E2E: Password reset ticket classifies via dynamic examples | D1+D2 |
| E2E: Add new example, restart agent, verify new category works | Full pipeline |
| Visual: Example editor CRUD operations | D3 |
| Visual: Classify step properties shows example set selector | D4 |

---

## 8. Effort Summary

| Phase | Description | Effort | Priority |
|-------|-------------|--------|----------|
| D1 | Wire ClassifyExecutor to dynamic examples | 2-3 hours | Must-have |
| D2 | Verify/enhance export serialization | 1-2 hours | Must-have |
| D3 | Enhance Example editor UI | 4-6 hours | Must-have |
| D4 | Classify step properties in designer | 3-4 hours | Should-have |
| **Total** | | **10-15 hours** | |

Phases D1+D2 are the minimum for dynamic classification. D3 makes it
customer-usable. D4 is designer polish.

---

## 9. References

- TD-001 in `docs/TECHNICAL_DEBT.md`
- `agent/src/agent/runtime/executors/classify.py` — Current hardcoded executor
- `admin/dotnet/src/LucidAdmin.Infrastructure/Data/Seeding/ExampleSetSeeder.cs`
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Examples/`
- ADR-010 — Visual Workflow Designer (established Example editor)
- ADR-011 — Composable Workflows (dispatcher depends on correct classification)
