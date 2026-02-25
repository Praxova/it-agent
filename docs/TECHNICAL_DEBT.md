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

## TD-003: No Validation Between Capability Names in Workflows vs Capability Mappings (MEDIUM) — PARTIALLY RESOLVED

**Location:** Seeder `WorkflowSeeder.cs` (step ConfigurationJson), Capability Providers, `execute.py` (endpoint_map)
**Introduced:** ADR-011 Phase 5 E2E testing (2026-02-06)
**Partially resolved:** 2026-02-10

**Original Problem:**
Capability names appeared in three independent locations with no cross-validation:
1. Workflow step `ConfigurationJson` (e.g., `"capability": "ad-group-add"`)
2. Capability Provider IDs in the Admin Portal (e.g., `ad-group-mgmt`)
3. Python `endpoint_map` in `execute.py` (maps capability → tool server API path)

The group-membership-sub and file-permissions-sub workflows had mismatched capability names vs the registered capability providers, causing silent 404 failures at runtime.

**What was fixed:**
1. Capability Provider IDs aligned to match workflow step names and endpoint_map:
   - `ad-group-mgmt` → split into `ad-group-add` and `ad-group-remove`
   - `fs-permissions` → split into `ntfs-permission-grant` and `ntfs-permission-revoke`
2. Agent startup validation warns when workflow Execute steps reference unregistered capabilities
3. Obsolete capability records cleaned up by seeder

**Remaining (post-launch):**
- The Python `endpoint_map` in `execute.py` is still hardcoded. Future enhancement: replace with dynamic endpoint discovery from tool server capability metadata.
- No foreign key or lookup validation in the Admin Portal UI when saving workflow step configurations. Users could still type a wrong capability name in the workflow designer.

**Risk if Deferred (remaining items):**
Low — the startup validation catches mismatches early. The hardcoded endpoint_map covers all current capabilities and only needs updating when new capabilities are added.

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

---

## TD-007: Admin Portal Authentication — AD Integration and Local Account Hardening (HIGH — MVP BLOCKER)

**Location:** `admin/dotnet/src/LucidAdmin.Web/` (auth middleware, login flow), `LucidAdmin.Infrastructure/Services/`
**Introduced:** Initial portal scaffold — hardcoded `admin/admin` credentials
**Workaround:** Static username/password with no password change capability

**Problem:**
The Admin Portal currently uses a hardcoded `admin/admin` local account with no password change mechanism, no password policy enforcement, and plaintext credential comparison. This is unsuitable for any deployment beyond local development.

Two authentication capabilities are needed:

**1. Local Break-Glass Account (hardening)**
The built-in local administrator account must remain permanently available — it cannot be disabled or removed. This account is the only recovery path when the portal is disconnected from Active Directory due to network segmentation, routing failures, DNS issues, or domain trust problems. However, it needs proper hardening:
- Force password change on first login (the training video intro should demonstrate this)
- Store the password hash using the existing Argon2 hasher (`Argon2PasswordHasher.cs`)
- Enforce a reasonable password policy (length, complexity)
- Support credential storage in a vault (HashiCorp Vault, Azure Key Vault) rather than the application database for production deployments
- Audit all local account authentications

**2. Active Directory Integration (new capability)**
Operators and administrators should authenticate against the domain (montanifarms.com) using their existing AD credentials:
- LDAP bind authentication against the domain controller
- AD group-to-portal-role mapping (e.g., `LucidAdmin-Admins` → Admin role, `LucidAdmin-Operators` → Operator role, `LucidAdmin-Viewers` → Viewer role)
- Leverage the existing `UserRole` enum (Admin, Operator, Viewer)
- Kerberos/NTLM pass-through for domain-joined browsers (nice-to-have for MVP, not required)
- Graceful fallback to local account when AD is unreachable

**Proper Fix:**
- Add a first-login setup flow that forces the default admin password to be changed before any other portal operations
- Implement LDAP authentication provider using the existing ServiceAccount infrastructure (a `windows-ad` service account already exists for the tool server — the portal can use the same pattern or a dedicated one)
- Add AD group membership lookup at login time to resolve portal roles
- Add an authentication provider abstraction so local and AD auth coexist cleanly
- The local account should always authenticate against the local store, never against AD — keeping the two paths independent ensures the break-glass account works regardless of AD connectivity

**Risk if Deferred:**
Cannot ship MVP. The first thing shown in training videos must be changing the default password. Shipping with `admin/admin` and no AD integration would undermine credibility with any enterprise audience. The break-glass account requirement also means this can't be solved by "just use AD" — both paths must work.

*Last updated: 2026-02-08*

---

## TD-008: Ollama Does Not Support TLS — LLM Inference Traffic Unencrypted (MEDIUM)

**Location:** Docker Compose `ollama` service, Admin Portal LLM service account configuration
**Discovered:** 2026-02-15 during SSL migration for demo prep
**Workaround:** Running Ollama on isolated Docker bridge network (`praxova-network`) with no external port exposure in production. Acceptable for demo; not for enterprise deployment.

**Problem:**
Ollama has no native TLS support — it only serves plain HTTP on port 11434. While the traffic is LLM prompts and completions (not credentials or PII in most cases), enterprise security policies universally require all inter-service traffic to be encrypted. Security teams will not accept "it's only inference traffic" as justification. This was identified when the full stack was moved to SSL and Ollama was the only component that could not participate.

**Options for Proper Fix (in order of complexity):**
1. **TLS-terminating reverse proxy** (Quick) — Add an Nginx/Caddy sidecar container that terminates TLS and proxies to `ollama:11434`. Minimal disruption, works with existing Ollama images. Agent/portal connect to the proxy via HTTPS.
2. **Switch to llama.cpp server** (Medium) — llama.cpp's built-in HTTP server supports `--ssl-cert-file` and `--ssl-key-file` natively. Requires manual model file management (no `ollama pull`), but gives full TLS control. The existing LLM driver factory (ADR-006) makes the backend swap transparent to agent code.
3. **Switch to vLLM or TGI** (Production-grade) — Both support TLS, production batching, token streaming, and multi-model serving. Higher operational complexity but purpose-built for enterprise LLM serving at scale.

All options are transparent to the agent thanks to the pluggable driver factory pattern in ADR-006. No agent code changes required regardless of which path is chosen.

**Risk if Deferred:**
- Enterprise security audits will flag unencrypted traffic between services, even on internal networks.
- Blocks deployment in environments with strict network encryption policies (financial, healthcare, government).
- Demo/MVP acceptable with the Docker network isolation mitigation, but must be resolved before first customer deployment.

*Last updated: 2026-02-15*
