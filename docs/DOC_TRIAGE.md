# Documentation Triage Manifest
**Created**: 2026-02-25  
**Purpose**: Drive the Phase B/C/D documentation cleanup sessions.  
**Status**: Phase A complete — triage only, no files changed yet.

---

## Legend

| Code | Action |
|------|--------|
| ✅ KEEP+UPDATE | Living reference doc — keep and bring current in Phase B |
| 📦 ARCHIVE | Historical value, move to `docs/archive/` |
| 🗑️ DELETE | No ongoing value, delete in Phase C |
| 🔀 MOVE | Lives in wrong location, relocate |
| 🆕 CREATE | Missing documentation identified in Phase A — write in Phase D |

---

## Root-Level Files

| File | Action | Notes |
|------|--------|-------|
| `CLAUDE.md` | ✅ KEEP+UPDATE | Claude Code instructions — correct purpose, but still says "Lucid IT Agent" in title. Missing ADRs 010-015, missing composable workflow and approval patterns, missing gMSA/cert/secrets info. Major update needed. |
| `CLAUDE_CONTEXT.md` | 🗑️ DELETE | Completely superseded by `CLAUDE.md`. This is the old version with the same content — ADR list stops at 009, still references old sprint focus and stale directory structure. The two files have drifted into duplication. **Decision: CLAUDE.md is Claude Code file, this one served the same purpose and is outdated.** |
| `CLAUDE_PROJECT_PROMPT.md` | 🗑️ DELETE | Very early session-0 build prompt from when the project was first created. Not a reference document. |
| `README.md` | ✅ KEEP+UPDATE | Still says "Lucid IT Agent" throughout. Roadmap section is almost entirely stale. Architecture diagram is outdated (missing Admin Portal as central hub). Needs a full pass. |
| `Makefile` | ✅ KEEP | Build/dev automation — review commands for accuracy in Phase B. |
| `Makefile.bak` | 🗑️ DELETE | Backup file with no value. |
| `dc01-cert.json` | 🔀 MOVE | Certificate artifact at repo root. Move to `env-setup/dc/certs/` and add to `.gitignore`. Security concern. |
| `dc01-cert.pem` | 🔀 MOVE | Same as above. |
| `dc01-key.pem` | 🔀 MOVE | **Private key at repo root.** Priority move. |
| `dc01-ldaps.pfx` | 🔀 MOVE | Same as above. |
| `praxova-ca.pem` | 🔀 MOVE | CA cert at repo root — move and gitignore. |

---

## docs/ Root-Level Files

### Living Reference — Keep & Update

| File | Action | Notes |
|------|--------|-------|
| `ARCHITECTURE.md` | ✅ KEEP+UPDATE | Largest single documentation gap. Was written before ADRs 010-015. Missing: composable workflow/dispatcher pattern (ADR-011), human-in-the-loop approval (ADR-013), internal PKI (ADR-014), secrets infrastructure (ADR-015), and the software install workflow. The entire "Ticket Processing Flow" section needs to reflect the new dispatcher + sub-workflow architecture. Also references "Lucid" branding throughout. **This is Phase B Session 1 priority.** |
| `TECHNICAL_DEBT.md` | ✅ KEEP+UPDATE | Actually the best-maintained doc in the repo. TD-001 through TD-008 with accurate descriptions and dates. Minor updates only: confirm TD-003 status (shows "partially resolved"), add any new debt items discovered since last update. |
| `DEV-QUICKREF.md` | ✅ KEEP+UPDATE | Excellent doc, clearly maintained. Last updated 2026-02-21. Minor gap: tool server cert provisioning script (`provision-toolserver-certs.ps1`) is documented but may not exist yet — verify. Port map needs HTTPS verification. Otherwise solid. |
| `WORKFLOW_DESIGNER_ENTITIES.md` | ✅ KEEP+UPDATE | Still valuable as a reference for entity shapes, but the code exists now — verify the entities match what was actually implemented. May need updates to reflect any changes made during implementation. Rename to `ADMIN_PORTAL_ENTITIES.md` to be more accurate. |
| `TEST-DATA-STRATEGY.md` | ✅ KEEP+UPDATE | Keep — relevant for ongoing development and demo prep. Review for accuracy. |
| `WINDOWS_CONTAINERS_PRIMER.md` | ✅ KEEP | Still accurate background reference for Windows container work. Low priority update. |
| `PROXMOX-LAB-LAYOUT.md` | ✅ KEEP+UPDATE | Keep — infrastructure reference. Update to reflect actual current state vs. plan (DEV-INFRASTRUCTURE.md says "Planning" as of Feb 15, but Proxmox migration was already underway per memory context). |
| `PROXMOX-SETUP-RUNBOOK.md` | ✅ KEEP+UPDATE | Keep — operational runbook. Verify steps are current. |

### Review Needed (read before deciding in Phase B)

| File | Action | Notes |
|------|--------|-------|
| `AGENT_CONFIG_INTEGRATION.md` | ❓ REVIEW | Not read in triage — review in Phase B to determine if content has been superseded by the current agent config loading code. |
| `MCP-INFRASTRUCTURE-ANALYSIS.md` | ❓ REVIEW | Not read in triage — could be a planning doc that's now outdated, or could have useful background. Review in Phase B. |

### Archive Candidates (historical value, not reference)

| File | Action | Notes |
|------|--------|-------|
| `DEV-INFRASTRUCTURE.md` | 📦 ARCHIVE | This was a planning document for the ESXi → Proxmox migration as of Feb 15 with detailed video and migration plan. That migration has since proceeded. The video strategy section is still useful context, but it was a planning artifact not a reference doc. Archive it. The actual current infrastructure state should live in `PROXMOX-LAB-LAYOUT.md`. |
| `SPRINT_BACKLOG.md` | 📦 ARCHIVE | Wildly outdated. Describes "Current Sprint: Service Account & Multi-Account Support" and future sprints like "Admin Portal Foundation" that are both long-complete. Has value as a historical record of how the project evolved. Archive it. **Replacement needed** — see Phase D. |

### Delete Candidates (one-time artifacts, build logs, session handoffs)

| File | Action | Notes |
|------|--------|-------|
| `ADMIN_PROJECT_STRUCTURE.md` | 🗑️ DELETE | Early scaffold structure doc. Superseded by actual code and `WORKFLOW_DESIGNER_ENTITIES.md`. |
| `AGENT_EXPORT_TRANSITIONS_FIX.md` | 🗑️ DELETE | Fix record for a resolved issue. |
| `ANONYMOUS_API_ACCESS_VERIFICATION.md` | 🗑️ DELETE | One-time verification record. |
| `CLAUDE_CODE_PROMPT_ADMIN_SCAFFOLD.md` | 🗑️ DELETE | Build prompt, feature shipped. |
| `CLAUDE_CODE_PROMPT_DOTNET_SCAFFOLD.md` | 🗑️ DELETE | Build prompt, feature shipped. |
| `CLAUDE_CODE_PROMPT_transition_editing.md` | 🗑️ DELETE | Build prompt, feature shipped. |
| `infra-handoff-ssl-trust-bootstrap.md` | 🗑️ DELETE | Session handoff doc with no permanent value. |
| `continuation-prompt-docker-deploy.md` | 🗑️ DELETE | Session handoff doc. |
| `PHASE_1_RULESETS_PROMPT.md` | 🗑️ DELETE | Build prompt, feature shipped. |
| `PHASE_4B_PYTHON_RUNTIME.md` | 🗑️ DELETE | Build log for completed phase. |
| `PHASE_4B1_RUNTIME_INFRASTRUCTURE.md` | 🗑️ DELETE | Build log for completed phase. |
| `PHASE_4B2_STEP_EXECUTORS.md` | 🗑️ DELETE | Build log for completed phase. |
| `PHASE_4B3_INTEGRATION_LAYER.md` | 🗑️ DELETE | Build log for completed phase. |
| `PHASE_4B4_TESTING_COMPLETE.md` | 🗑️ DELETE | Build log for completed phase. |
| `PORTAL_AGENT_BUILDER_Phase 4A.md` | 🗑️ DELETE | Build log for completed phase. |
| `PORTAL_AGENT_BUILDER_Phase 4B-1.md` | 🗑️ DELETE | Build log for completed phase. |
| `PORTAL_AGENT_BUILDER_Phase 4B-2.md` | 🗑️ DELETE | Build log for completed phase. |
| `PORTAL_AGENT_BUILDER_Phase 4B-3.md` | 🗑️ DELETE | Build log for completed phase. |
| `PORTAL_AGENT_BUILDER_Phase 4B-4.md` | 🗑️ DELETE | Build log for completed phase. |
| `SPRINT3_COMPLETE.md` | 🗑️ DELETE | Sprint completion record. |
| `SPRINT3_STATUS.md` | 🗑️ DELETE | Sprint status record. |
| `TEST_PLAN_DELEGATED_HEALTH_CHECK.md` | 🗑️ DELETE | One-time test plan for a specific fix. |
| `WORKFLOW_DESIGNER_FIXES_VERIFICATION.md` | 🗑️ DELETE | Verification record for shipped fixes. |
| `WORKFLOW_SEEDER_VERIFICATION.md` | 🗑️ DELETE | Verification record for completed work. |
| `WORKFLOW_SELECTOR_IMPLEMENTATION.md` | 🗑️ DELETE | Implementation notes for shipped feature. |

---

## docs/adr/

ADRs are the most important architectural documents and should be treated carefully. The main issue is that statuses are frozen at "Draft" or "Proposed" even when implementation is complete.

| File | Action | Notes |
|------|--------|-------|
| `ADR-005-windows-native-tool-server.md` | ✅ KEEP | Update status to "Accepted/Implemented." Verify the implementation notes match current code. |
| `ADR-006-admin-portal-config-service.md` | ✅ KEEP | Update status. Note the rename to Praxova. |
| `ADR-009-llm-reasons-tools-execute.md` | ✅ KEEP | Update status if implementation complete. |
| `ADR-010-visual-workflow-designer.md` | ✅ KEEP | Update status. |
| `ADR-011-composable-workflows-pluggable-triggers.md` | ✅ KEEP+UPDATE | Status is "Draft" but this is fully validated as of 2026-02-06. Update status to "Accepted/Implemented." Add implementation summary section noting which phases completed (all) and which remain open (T4 Email, T5 Jira triggers). The "Questions for Alton" section can be removed or converted to a "Decisions Made" section. |
| `ADR-012-dynamic-classification-training.md` | ✅ KEEP | Update status. Note TD-001 relationship. |
| `ADR-013-human-in-the-loop-approval.md` | ✅ KEEP+UPDATE | Status is "Proposed" but all phases (A1-A5) show ✅ Complete as of 2026-02-06. Update to "Accepted/Implemented." |
| `ADR-014-certificate-management-agent.md` | ✅ KEEP+UPDATE | Status is "Proposed." Phase 0 (LDAPS trust for portal) appears to have been implemented per memory context and DEV-QUICKREF. Update status to reflect what's done vs. what's future roadmap. |
| `ADR-015-secrets-management-credentials.md` | ✅ KEEP+UPDATE | Not read in triage — review status in Phase B. Per memory context, Phase 0 (secrets foundation) and Phase 1 (envelope encryption) are implemented. Update accordingly. |

**Missing ADRs** — decisions were made but never documented as ADRs:
- ADR-007 and ADR-008 are referenced in CLAUDE_CONTEXT.md but no ADR files exist for them. They should either be written or documented as inline in ARCHITECTURE.md.

---

## docs/brand/

| File | Action | Notes |
|------|--------|-------|
| `brand/COLOR_PALETTE.md` | ✅ KEEP | Useful for UI consistency. Keep as-is. |

---

## docs/infra/

| File | Action | Notes |
|------|--------|-------|
| `infra/PRAXOVA-DEV-ENVIRONMENT-RUNBOOK.md` | ✅ KEEP+UPDATE | Operational runbook — keep and verify accuracy. |
| `infra/PRAXOVA-WINDOWS-INFRA-RUNBOOK.md` | ✅ KEEP+UPDATE | Operational runbook — keep and verify accuracy. |

---

## docs/prompts/

This directory is entirely build prompts for Claude Code. Most are for shipped features.

### Delete (feature shipped)

| File | Notes |
|------|-------|
| `ADR-012-claude-code-prompt.md` | Dynamic classification — ADR-012 implemented. |
| `ADR-013_Phase_A2_ApprovalExecutor_prompt.md` | ADR-013 fully implemented. |
| `CC-PROMPT-dynamic-ticket-categories-01.md` | Shipped. |
| `CC-PROMPT-dynamic-ticket-categories-02.md` | Shipped. |
| `CC-PROMPT-example-crud-ui-03.md` | Shipped. |
| `PROMPT_BLAZOR_DIAGRAMS_MIGRATION.md` | Shipped. |
| `PROMPT_C2_subworkflow_execution.md` | ADR-011 Phase C2 — complete. |
| `PROMPT_C3_dispatcher_workflow.md` | ADR-011 Phase C3 — complete. |
| `PROMPT_CONTAINERIZE_ADMIN_PORTAL.md` | Shipped. |
| `PROMPT_Clarification_Infrastructure.md` | One-time clarification. |
| `PROMPT_DELEGATED_HEALTH_CHECK.md` | Shipped. |
| `PROMPT_EXAMPLES_EDITOR.md` | Shipped. |
| `PROMPT_FIX_DESIGNER_LIFECYCLE.md` | Shipped fix. |
| `PROMPT_RULES_EDITOR.md` | Shipped. |
| `PROMPT_T2_manual_trigger.md` | ADR-011 Phase T2 — complete. |
| `PROMPT_T3_dynamic-agent_config.md` | ADR-011 Phase T3 — complete. |
| `PROMPT_WORKFLOW_DESIGNER.md` | Shipped. |
| `PROMPT_add_Azure_Provider.md` | Shipped. |
| `PROMPT_add_workflow_selector_to_agent.md` | Shipped. |
| `PROMPT_software_2B.md` | Shipped. |
| `PROMPT_software_A2.md` | Shipped. |
| `PROMPT_software_A2_fix.md` | Shipped. |
| `PROMPT_software_install_tool.md` | Shipped. |
| `PROMPT_3_testing_and_integration.md` | Shipped. |
| `fix-ssl-trust-complete.md` | Shipped fix. |
| `phase-C1-subworkflow-step-type.md` | ADR-011 Phase C1 — complete. |
| `phase-T1-trigger-abstraction.md` | ADR-011 Phase T1 — complete. |
| `task-admin-portal-branding.md` | Shipped. |
| `task-ssl-certificate-management.md` | Shipped. |
| `task-tool-server-https.md` | Shipped. |
| `task-tool-server-installer.md` | Shipped. |
| `task-tool-server-windows-service.md` | Shipped. |
| `TD-003-fix-capability-alignment.md` | TD-003 partially resolved — prompt done. |

### Keep (feature not yet fully implemented)

| File | Action | Notes |
|------|--------|-------|
| `PROMPT-ADR014-PHASE0-LDAPS-TRUST.md` | ✅ KEEP | Phase 0 of ADR-014 — check if complete. If yes, delete. |
| `PROMPT-ADR014-PHASE01-INTERNAL-PKI.md` | ✅ KEEP | Phase 1 of ADR-014 — likely future work. |
| `PROMPT-ADR015-PHASE0-SECRETS-FOUNDATION.md` | ✅ KEEP | Phase 0 of ADR-015 — check if complete. |
| `PROMPT-ADR015-PHASE1-ENVELOPE-ENCRYPTION.md` | ✅ KEEP | Phase 1 of ADR-015 — check if complete. |
| `PROMPT-AGENT-TRUST-BOOTSTRAP.md` | ✅ KEEP | Check if implemented or still pending. |
| `TD-007A_local_account_hardening.md` | ✅ KEEP | TD-007 is a current HIGH priority. |
| `TD-007B_ad_ldap_authentication.md` | ✅ KEEP | TD-007 is a current HIGH priority. |
| `TD-007C_ad_settings_ui_and_dc_setup.md` | ✅ KEEP | TD-007 is a current HIGH priority. |
| `TD-007_fix1_password_contains.md` | ❓ REVIEW | May be shipped — verify. |
| `TD-007_fix2_audit_log_page.md` | ❓ REVIEW | May be shipped — verify. |
| `TD-007_fix3_rbac_enforcement.md` | ❓ REVIEW | May be shipped — verify. |

---

## docs/test-scenarios/ and docs/testing/

| File | Action | Notes |
|------|--------|-------|
| `test-scenarios/software-install-demo.md` | ✅ KEEP | Demo scenario — keep for demo prep. |
| `testing/Create-cert-tool-server.md` | ✅ KEEP | Operational testing procedure. |
| `testing/TD-007_test_plan.md` | ✅ KEEP | Active test plan for current high-priority TD-007. |

---

## Phase D: Missing Documentation to Create

These are gaps identified during triage — documentation that should exist but doesn't.

| Doc to Create | Priority | Notes |
|---------------|----------|-------|
| **ROADMAP.md** | HIGH | Replace SPRINT_BACKLOG.md. A forward-looking roadmap: v1 release scope, post-v1 near-term features (TD-007 auth, ADR-014/015), and longer-term vision (cert agent, email trigger, etc.). |
| **ARCHITECTURE.md major revision** | HIGH | The current doc pre-dates ADRs 010-015. The entire workflow execution section needs to be rewritten to reflect the composable workflow/dispatcher pattern. See Phase B Session 1. |
| **DEPLOYMENT.md** | HIGH | No deployment guide exists. DEV-QUICKREF covers dev but not production. Should cover: Docker Compose, environment variables, initial setup sequence, first-login, service account configuration, tool server deployment and cert provisioning. |
| **ADR-007-capability-routing.md** | MEDIUM | Referenced in CLAUDE_CONTEXT.md but no ADR file exists. Write a proper ADR or fold into ARCHITECTURE.md. |
| **ADR-008-agent-config-from-portal.md** | MEDIUM | Same — referenced but no file. |
| **CONTRIBUTING.md** | MEDIUM | Needed for GitHub public release. Git workflow, branch naming, commit format, how to run tests, PR process. |
| **SECURITY.md** | MEDIUM | Security policy, how to report vulnerabilities. Standard for open-source projects. |
| **GITHUB_SETUP.md** or `.github/` config | MEDIUM | Issue templates, PR template, CODEOWNERS, actions workflows for CI. |

---

## Summary Counts

| Action | Count |
|--------|-------|
| Keep & Update | 23 |
| Archive | 3 |
| Delete | 38 |
| Move | 5 |
| Review before deciding | 6 |
| Create (new) | 8 |

**Net result**: ~40 files removed, docs directory becomes significantly cleaner. The remaining docs are all either actionable reference material or active ADRs.

---

## Recommended Phase Order

| Phase | Session | What to Tackle | Priority |
|-------|---------|---------------|----------|
| **B** | B1 | ARCHITECTURE.md full revision (biggest single gap) | 🔴 HIGH |
| **B** | B2 | CLAUDE.md update + README.md update | 🔴 HIGH |
| **B** | B3 | ADR status updates (ADR-011, 013, 014, 015) | 🟡 MEDIUM |
| **B** | B4 | DEV-QUICKREF.md verify + infra runbooks | 🟡 MEDIUM |
| **C** | C1 | Execute all 🗑️ DELETEs in docs/ root | 🟢 QUICK WIN |
| **C** | C2 | Execute all 🗑️ DELETEs in docs/prompts/ | 🟢 QUICK WIN |
| **C** | C3 | Move cert files at repo root, update .gitignore | 🔴 SECURITY |
| **D** | D1 | Write DEPLOYMENT.md | 🔴 HIGH |
| **D** | D2 | Write ROADMAP.md (replace SPRINT_BACKLOG.md) | 🟡 MEDIUM |
| **D** | D3 | Write CONTRIBUTING.md + SECURITY.md + GitHub templates | 🟡 MEDIUM (pre-launch) |
