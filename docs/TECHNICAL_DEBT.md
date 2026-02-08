# Lucid IT Agent — Technical Debt Tracker

> Items that are working but need proper long-term solutions.
> Prioritized by impact and risk.

---

## TD-001: Classification Prompt Uses Hardcoded Categories (HIGH)

**Location:** `agent/src/agent/runtime/executors/classify.py` (~line 119, 143)
**Introduced:** ADR-011 Phase C3 E2E testing (2026-02-06)
**Workaround:** `_TYPE_MAP` normalization dict in `_parse_classification_response()`

**Problem:**
The classify executor has hardcoded prompt text with underscore-style category names (`password_reset`, `group_access_add`, `file_permission`) and inline few-shot examples. The dispatcher workflow transition conditions use hyphen-style names (`password-reset`, `group-membership`, `file-permissions`). A bandaid mapping dict translates between them at parse time.

Additionally, the classify executor ignores the dynamic example sets stored in the portal (e.g., `it-dispatch-classification`), even though the seeder creates them and the export includes them in `context.export.example_sets`. There's a comment on line ~142 acknowledging this: *"Note: Example sets would come from context.export.example_sets / For now, we'll use inline examples"*.

**Proper Fix:**
1. Wire `_build_classification_prompt()` to pull categories and examples dynamically from the workflow's configured example set via `context.export.example_sets`.
2. Remove the hardcoded prompt categories and inline examples.
3. Remove the `_TYPE_MAP` normalization — the LLM will naturally output whatever format the dynamic examples teach it, which will match the dispatcher conditions by design.
4. This also enables different workflows to use different classification taxonomies without code changes.

**Risk if Deferred:**
- Any new ticket type added via the portal won't route correctly until the mapping dict is manually updated in Python code.
- Two sources of truth for classification categories (portal examples vs. Python code) will drift over time.

---

## TD-002: Pydantic Model Null Handling for C# Serialization (LOW)

**Location:** `agent/src/agent/runtime/models.py` — `AgentBasicInfo.service_account_bindings`
**Introduced:** ADR-011 Phase 5 E2E testing (2026-02-06)
**Workaround:** `@field_validator("service_account_bindings", mode="before")` coerces `None` → `[]`

**Problem:**
The C# Admin Portal serializes empty/null collections as `null` in JSON. Pydantic's `default_factory=list` only handles the case where the key is entirely absent from JSON — when the key is present with value `null`, validation fails with `Input should be a valid list`.

**Proper Fix:**
Either:
- (C# side) Ensure the export serializer always emits `[]` instead of `null` for collection properties — add `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]` or initialize all `List<T>` properties.
- (Python side) Add a reusable base model mixin or custom type that coerces `null` → `[]` for all list fields, rather than per-field validators.

**Risk if Deferred:**
Low — only affects agent startup when no service account bindings exist. The field_validator fix is stable. But any new list field added to the export models could hit the same issue.

---

---

## TD-003: No Validation Between Capability Names in Workflows vs Capability Mappings (MEDIUM)

**Location:** Seeder `WorkflowSeeder.cs` (step ConfigurationJson), Capability Mappings (portal UI), `execute.py` (endpoint_map)
**Introduced:** ADR-011 Phase 5 E2E testing (2026-02-06)
**Workaround:** Manual alignment — fixed seeder values and patched live DB

**Problem:**
Capability names appear in three independent locations with no cross-validation:
1. Workflow step `ConfigurationJson` (e.g., `"capability": "ad-group-add"`)
2. Capability Mappings in the Admin Portal (e.g., capability name `ad-group-add`)
3. Python `endpoint_map` in `execute.py` (maps capability → tool server API path)

The group-membership-sub and file-permissions-sub workflows shipped with wrong capability names (`ad-group-membership` and `file-permissions` instead of `ad-group-add` and `ntfs-permission-grant`). The execute step silently failed because the Admin Portal returned 404 for the unknown capability, and the workflow reported success despite doing nothing.

**Proper Fix:**
- Add a startup validation step in the agent that checks all referenced capabilities in the active workflow export against the registered capability mappings.
- Consider adding a foreign key or lookup validation in the Admin Portal when saving workflow step configurations.
- The Python `endpoint_map` should be replaced with dynamic endpoint discovery from the tool server's capability metadata.

**Risk if Deferred:**
Any new workflow with a typo in a capability name will silently fail. The lack of validation makes this class of bug invisible without careful log inspection.

---

## TD-004: Workflow Engine Treats "No Matching Transition" as Success (MEDIUM)

**Location:** `agent/src/agent/runtime/workflow_engine.py` (~line 192), `sub_workflow.py` (~line 155)
**Introduced:** ADR-011 Phase 5 E2E testing (2026-02-06)
**Workaround:** None — design gap, not yet causing user-facing issues with correct capability names

**Problem:**
When no transition condition matches after a step, the workflow engine defaults to "completing" the workflow. Combined with the sub-workflow executor mapping FAILED status to `outcome: "failed"` (which has no dispatcher transition), this creates a silent success path for actual failures.

The chain: Execute step fails → sub-workflow sets FAILED status → sub_workflow executor maps to `result.complete({"outcome": "failed"})` → dispatcher checks `outcome == 'completed'` (no), `outcome == 'escalated'` (no) → no match → "no outgoing transition, completing" → ticket marked resolved.

**Proper Fix:**
Two complementary changes:
1. Add `outcome == 'failed'` transitions in the dispatcher workflow for each sub-workflow step (route to escalation).
2. Consider making "no matching transition" configurable — default to failure rather than success, or at least log it as a warning that's distinct from intentional completion.

**Risk if Deferred:**
Any sub-workflow failure that doesn't match existing transition conditions will silently resolve the ticket as successful.

---

## TD-005: Multi-Task Ticket Decomposition (FUTURE — ENHANCEMENT)

**Location:** Classification / dispatcher routing layer
**Observed:** ADR-011 Phase 5 E2E testing (2026-02-06)
**Workaround:** None — single-task processing only

**Problem:**
When a ticket contains multiple requests (e.g., "add obi-wan.kenobi to LucidTest-VPNUsers AND Department1_FileShare_Group"), the classifier picks up only the first group and the workflow executes a single action. The second group is silently ignored.

This is common in real-world IT tickets — users frequently bundle related requests into one ticket.

**Proper Fix:**
Add a ticket decomposition step (or enhance the classify step) that can detect multi-task tickets and either:
1. Loop the sub-workflow for each identified sub-task within the same ticket, or
2. Split the ticket into child tickets, one per sub-task, each processed independently.

Option 1 is simpler; option 2 is more auditable but requires ServiceNow child ticket creation.

**Risk if Deferred:**
Incomplete ticket resolution — users will need to re-submit for missed items. Acceptable for MVP but impacts customer satisfaction in production.

*Last updated: 2026-02-06*

---

## TD-006: Software Catalog Local Caching (MEDIUM)

**Location:** `agent/src/agent/runtime/executors/classify.py`, `agent/src/agent/config_loader.py`
**Introduced:** Software install workflow implementation (2026-02-08)
**Workaround:** Agent requires admin portal connectivity at startup and during catalog resolution.

**Problem:**
The approved software catalog is currently fetched from the admin portal as an example set during agent startup. If the admin portal is temporarily unavailable, the agent cannot resolve catalog entries for software install requests.

**Proper Fix:**
Implement local caching (SQLite or YAML) so the agent can operate with a stale catalog when the admin portal is temporarily unavailable. Cache should refresh periodically (e.g., every 5 minutes) when the portal is reachable, and fall back to the last cached version when it isn't.

**Risk if Deferred:**
Agent cannot process software install tickets if the admin portal is down. Acceptable for dev/demo environments but a reliability concern in production.

*Last updated: 2026-02-08*
