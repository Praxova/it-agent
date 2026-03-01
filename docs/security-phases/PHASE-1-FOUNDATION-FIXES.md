# Phase 1 — Foundation Fixes

## Context for the Intermediary Chat

This prompt was produced by a security architecture session that designed the to-be
security posture for Praxova IT Agent. It describes WHAT needs to exist and WHY.
Your job is to compare this against the current codebase, identify the delta, and
produce a Claude Code implementation prompt that gets from as-is to to-be.

Do not change anything beyond what is specified here. These are four isolated fixes
with no dependencies on each other. They can be implemented and committed separately.

---

## Fix 1: HTTP Bootstrap Endpoint — Exempt from HTTPS Redirect

### What and Why

The agent container needs to fetch the portal's CA certificate over plain HTTP before
it can trust the portal's HTTPS certificate. This is the trust bootstrap problem —
you can't use HTTPS to fetch the certificate that enables HTTPS.

Currently, the portal's `UseHttpsRedirection()` middleware intercepts the HTTP request
to `/api/pki/trust-bundle` and redirects it to HTTPS, which the agent can't follow
because it doesn't trust the portal's cert yet. The workaround is a shared Docker
volume mount that gives the agent filesystem access to the portal's data directory.

The fix: exempt `/api/pki/trust-bundle` from the HTTPS redirect middleware so the
agent can fetch the CA cert over HTTP, then use HTTPS for everything else.

### Specification

1. The endpoint `GET /api/pki/trust-bundle` MUST be accessible over plain HTTP
   (port 5000) without being redirected to HTTPS.

2. All other endpoints MUST continue to redirect HTTP → HTTPS as they do today.

3. The implementation should use middleware ordering or a conditional check in the
   HTTPS redirect middleware — NOT by disabling HTTPS redirect globally.

4. The preferred pattern in ASP.NET Core is to add a short-circuit middleware
   BEFORE `UseHttpsRedirection()` that checks the request path and skips the
   redirect for the trust bundle endpoint. Example approach:

   ```
   // Before UseHttpsRedirection():
   app.Use(async (context, next) =>
   {
       if (context.Request.Path.StartsWithSegments("/api/pki/trust-bundle"))
       {
           await next();
           return;
       }
       await next();
   });
   app.UseHttpsRedirection();
   ```

   However, the actual implementation may differ based on how the current middleware
   pipeline is structured. The intermediary chat should examine the actual Program.cs
   or Startup.cs to determine the correct insertion point.

5. After this fix is verified, the shared Docker volume workaround in
   `docker-compose.yml` should be removed. The agent container should no longer
   mount the portal's data volume. Instead, the agent's entrypoint script should
   fetch the CA cert via HTTP from the portal.

6. The agent's entrypoint/bootstrap script should be updated to:
   a. Fetch CA cert from `http://admin-portal:5000/api/pki/trust-bundle`
   b. Write it to the trust store location (e.g., `/etc/ssl/certs/praxova-ca.pem`)
   c. Set `SSL_CERT_FILE` or run `update-ca-certificates`
   d. Then start the agent process, which uses HTTPS for all runtime calls

### Acceptance Criteria

- [ ] `curl http://localhost:5000/api/pki/trust-bundle` returns the CA certificate
      (PEM format) with HTTP 200, no redirect
- [ ] `curl http://localhost:5000/api/health/` redirects to HTTPS as before
- [ ] `curl http://localhost:5000/api/agents/` redirects to HTTPS as before
- [ ] The shared volume mount for trust bootstrap is removed from docker-compose.yml
- [ ] Agent container starts successfully using the HTTP bootstrap path
- [ ] Agent communicates with portal over HTTPS after bootstrap
- [ ] Full end-to-end workflow (ticket → classify → execute) works after the change

---

## Fix 2: Clean .env.example

### What and Why

The `.env.example` file references variables from earlier architecture iterations
that no longer apply. This causes confusion for anyone setting up the project —
operators may set variables that do nothing, miss variables that matter, or lose
confidence in the project's documentation quality.

For an open-source security product, the configuration documentation must be precise.
A stale `.env.example` in a public repository signals inattention to detail in a
codebase where details matter.

### Specification

1. The intermediary chat should examine the current `.env.example` and identify
   which variables are actually used by the current codebase (check
   `docker-compose.yml`, the portal's configuration loading, and the agent's
   configuration loading).

2. The new `.env.example` should contain ONLY variables that are currently consumed,
   organized into clear sections with comments explaining:
   - What the variable does
   - Whether it is required or optional
   - What the default is (if any)
   - Security implications (e.g., "Store this file with restrictive permissions")

3. Based on the current architecture, the expected variables are approximately:

   ```bash
   # === Required ===

   # Agent API key for portal authentication.
   # Generate in portal UI → API Keys after first login.
   LUCID_API_KEY=

   # Passphrase used to derive the master encryption key via Argon2id.
   # The portal uses this to unseal the secrets store on startup.
   # SECURITY: This file should be stored outside the project directory
   # with restrictive permissions (chmod 600). See deployment guide.
   PRAXOVA_UNSEAL_PASSPHRASE=

   # === Optional (uncomment if needed) ===

   # ServiceNow instance password (only if not stored in portal's
   # encrypted secrets store via the ServiceAccount UI)
   # SERVICENOW_PASSWORD=
   ```

   The intermediary chat should verify this list against actual code usage. There
   may be additional variables I'm not aware of, or some of these may have been
   renamed.

4. Any variables that existed in the old `.env.example` but are no longer consumed
   by any code should be REMOVED, not commented out. Dead configuration is worse
   than no configuration.

5. Add a header comment to the file:

   ```bash
   # Praxova IT Agent — Environment Configuration
   #
   # Copy this file to .env and fill in the required values.
   # See docs/deployment-guide.md for full configuration reference.
   #
   # SECURITY: The .env file contains sensitive credentials.
   # - Do NOT commit this file to version control (.gitignore should exclude it)
   # - Set restrictive file permissions: chmod 600 .env
   # - For production deployments, consider using a separate secrets file
   #   for the unseal passphrase (see docs/security/unseal-configuration.md)
   ```

### Acceptance Criteria

- [ ] Every variable in `.env.example` is consumed by at least one component
      (verified by code search)
- [ ] No variables from previous architecture iterations remain
- [ ] Each variable has a comment explaining its purpose and whether it's required
- [ ] Security guidance is included in the file header
- [ ] `docker compose up` works correctly with only the documented variables set
- [ ] The `.gitignore` includes `.env` (verify this is already the case)

---

## Fix 3: Unseal Passphrase Isolation

### What and Why

The unseal passphrase (`PRAXOVA_UNSEAL_PASSPHRASE`) is currently stored in the
`.env` file in the project directory alongside `docker-compose.yml`. This means
the master key material — the passphrase from which the entire encryption hierarchy
derives — sits in the same directory as the deployment configuration. Anyone who
copies the project directory (for backup, for sharing, for version control) gets
the passphrase.

The fix: separate the unseal passphrase into its own file outside the project
directory, with restrictive permissions, and configure Docker Compose to read from
that location.

This is a documentation and deployment configuration change, not a code change.
The portal's code doesn't need to change — it reads the passphrase from the
environment variable regardless of how that variable gets into the environment.

### Specification

1. Create a documented convention for the unseal passphrase location:
   - Recommended path: `/etc/praxova/unseal.env`
   - File contains only: `PRAXOVA_UNSEAL_PASSPHRASE=<value>`
   - Permissions: `chmod 600`, owned by root or the Docker service user
   - This file is NOT in the project directory and NOT in version control

2. Update `docker-compose.yml` to use Docker Compose's `env_file` directive
   pointing to the separate secrets file. The structure should be:

   ```yaml
   services:
     admin-portal:
       env_file:
         - .env                        # General configuration
         - /etc/praxova/unseal.env     # Unseal passphrase (restricted)
   ```

   Alternatively, if the project already uses a different mechanism for env
   injection, adapt this to match. The intermediary chat should examine the
   current docker-compose.yml structure and choose the cleanest approach.

3. The `.env` file should NO LONGER contain `PRAXOVA_UNSEAL_PASSPHRASE`. It
   should have a comment pointing to the separate file:

   ```bash
   # The unseal passphrase is stored separately for security.
   # See /etc/praxova/unseal.env (or your configured secrets file location).
   # See docs/security/unseal-configuration.md for setup instructions.
   ```

4. Create a documentation file at `docs/security/unseal-configuration.md` that
   explains:
   - What the unseal passphrase does (derives the master key for all encryption)
   - Why it's stored separately (blast radius reduction)
   - How to set it up (create file, set permissions, configure docker-compose)
   - What happens if it's lost (need recovery key — see Phase 2)
   - For development/testing: it's acceptable to put the passphrase in `.env`
     for convenience, but this should never be done in production

5. Create a setup helper script (`scripts/setup-unseal.sh`) that:
   - Creates `/etc/praxova/` directory if it doesn't exist
   - Prompts the operator for the unseal passphrase (or generates a random one)
   - Writes it to `/etc/praxova/unseal.env`
   - Sets permissions to 600
   - Prints confirmation and next steps

6. For development environments, the docker-compose.yml should fall back gracefully
   if `/etc/praxova/unseal.env` doesn't exist. Docker Compose supports optional
   env files with the `required: false` syntax (Compose v2.24+):

   ```yaml
   env_file:
     - .env
     - path: /etc/praxova/unseal.env
       required: false
   ```

   This means developers can still use `.env` for everything during development
   without creating the separate file.

### Acceptance Criteria

- [ ] `/etc/praxova/unseal.env` is the documented production location for the
      unseal passphrase
- [ ] `docker-compose.yml` references the separate file via `env_file`
- [ ] `.env.example` does not contain `PRAXOVA_UNSEAL_PASSPHRASE`
- [ ] `docs/security/unseal-configuration.md` exists and is complete
- [ ] `scripts/setup-unseal.sh` works on a fresh system
- [ ] `docker compose up` works in both configurations:
  - Production: passphrase in `/etc/praxova/unseal.env`
  - Development: passphrase in `.env` (fallback)
- [ ] Portal starts and unseals correctly in both configurations

---

## Fix 4: Failed Workflows Route to Escalation

### What and Why

When a workflow step fails (tool server unreachable, AD operation returns an error,
classification confidence below threshold for an unknown type), the workflow execution
must route to an escalation path — not silently complete or hang in an ambiguous state.

This is a security concern, not just an operational one. A failed password reset that
silently resolves the ticket means the user believes their password was reset when it
wasn't. A failed operation with no audit trail means there's no record of what went
wrong. And a workflow that fails without escalation creates a gap in the
human-in-the-loop supervision model that is core to the product.

### Specification

1. The dispatcher workflow must have explicit transitions for `outcome == 'failed'`
   on every step that can fail. This includes:
   - `Classify` step: classification failure (LLM error, timeout) → Escalate
   - `ToolCall` step: tool server error, capability routing failure → Escalate
   - `SubWorkflow` step: child workflow failure → Escalate
   - `Condition` step: no matching branch (unrecognized ticket type) → Escalate

2. The `Escalate` terminal step must:
   - Set the `WorkflowExecution` status to `Escalated`
   - Create an audit event with the failure reason
   - Add a work note to the ServiceNow ticket explaining that automation could not
     complete and the ticket has been escalated to a human
   - Assign the ticket to the original assignment group (or a configured escalation
     group) in ServiceNow

3. The workflow engine should treat "no matching transition" as a distinct condition
   from "step completed successfully with no outbound transition" (which is a
   terminal state). Specifically:
   - A step that completes with an outcome that has no matching transition should
     log a WARNING (not silently succeed) and route to escalation
   - A step that reaches an `End` node should log as INFO (successful completion)
   - These should be distinguishable in the audit log

4. The intermediary chat should examine the current dispatcher workflow definition
   (likely in `steps_json` of the dispatcher `WorkflowDefinition`) and the
   `WorkflowEngine` transition logic to understand how outcomes and transitions
   currently work. The fix may be:
   - Adding `failed` transitions to the workflow definition JSON
   - Adding default escalation behavior in the `WorkflowEngine` for unmatched
     outcomes
   - Or both

5. A catch-all: if the workflow engine encounters an unhandled exception during any
   step execution, it should catch the exception, log it, set the execution status
   to `Failed`, create an audit event, and attempt to escalate. The workflow should
   NEVER fail silently.

### Acceptance Criteria

- [ ] A ticket that fails classification (simulate by making LLM unreachable)
      routes to escalation, not silent completion
- [ ] A ticket that fails tool execution (simulate by making tool server unreachable)
      routes to escalation with the failure reason in the audit log
- [ ] A ticket with an unrecognized type (no matching sub-workflow) routes to
      escalation, not silent completion
- [ ] The ServiceNow ticket receives a work note on escalation explaining what
      happened
- [ ] The audit log contains the failure reason for every escalated workflow
- [ ] "No matching transition" produces a WARNING-level log entry distinct from
      successful completion
- [ ] An unhandled exception in any step executor results in escalation, not a
      crash or silent failure
- [ ] Existing successful workflows (password reset, group add) still work correctly
      (regression test)

---

## Implementation Notes for the Intermediary Chat

### File locations to examine (approximate — verify against actual project structure)

- Portal middleware pipeline: look for `UseHttpsRedirection()` in the portal's
  startup configuration (likely `Program.cs` or a similar entry point in
  `admin/dotnet/src/LucidAdmin.Web/`)
- Trust bundle endpoint: search for `trust-bundle` or `pki` in the portal's
  API controllers or minimal API route definitions
- Docker Compose: `docker-compose.yml` in the project root
- Environment configuration: `.env`, `.env.example` in the project root
- Agent entrypoint: likely a shell script (`entrypoint.sh` or similar) in the
  agent's Docker directory, or referenced in the agent's Dockerfile
- Workflow engine: `agent/src/agent/pipeline/engine.py`
- Step executors: `agent/src/agent/pipeline/executors/`
- Dispatcher workflow: may be in the database or in a seed/fixture file

### Git commit guidance

Each fix should be a separate commit with a descriptive message:

1. `fix(portal): exempt /api/pki/trust-bundle from HTTPS redirect`
2. `chore: clean .env.example to match current architecture`
3. `security: isolate unseal passphrase to separate env file`
4. `fix(agent): route failed workflows to escalation`

### What NOT to change

- Do not modify the PKI certificate generation logic
- Do not modify the secrets encryption/decryption logic
- Do not modify the mTLS authentication on the tool server
- Do not modify the ServiceNow connector
- Do not modify the LLM classification logic (only the failure handling)
- Do not add any new NuGet or pip dependencies unless absolutely necessary
