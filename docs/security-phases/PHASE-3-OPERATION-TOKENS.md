# Phase 3 — Operation Authorization Tokens

## Context for the Intermediary Chat

This prompt was produced by a security architecture session that designed the to-be
security posture for Praxova IT Agent. It describes WHAT needs to exist and WHY.
Your job is to compare this against the current codebase, identify the delta, and
produce a Claude Code implementation prompt that gets from as-is to to-be.

**This is the most security-critical feature in the entire v1.0 release.** It closes
the authorization gap between "who is calling" (mTLS certificate) and "what are they
authorized to do right now" (operation-scoped token). Without this, anyone with a
valid agent client certificate can call any tool server endpoint and execute any AD
operation — bypassing workflows, approval gates, and audit logging entirely.

**Dependency:** Phase 2 must be complete. Phase 3 uses the portal's JWT signing
infrastructure and writes audit events using the hash chain from Phase 2.

**Critical context from Phase 1 feedback:** The agent does NOT have a composable
WorkflowEngine with step-graph executors. It uses a procedural `TicketExecutor`.
The composable workflow engine described in ARCHITECTURE.md is the portal-side
visual designer — the agent-side executor that runs those step graphs is not yet
implemented. This phase must integrate with the ACTUAL agent code (the procedural
TicketExecutor), not the aspirational architecture. The intermediary chat must
examine where the agent currently makes tool server HTTP calls and insert the
token request at that point.

**Three components are modified:**
1. Admin Portal — new token issuance endpoint + supporting infrastructure
2. Python Agent — requests tokens before tool server calls
3. Tool Server — validates tokens before executing operations

Each component section below can be implemented and tested somewhat independently,
but the integration test at the end validates the complete chain.

---

## Component 1: Admin Portal — Token Issuance

### What and Why

The portal is the authorization authority. When the agent needs to execute an
operation against a tool server (e.g., reset a password, modify a group), it first
requests an operation token from the portal. The portal validates that the operation
is legitimate (there's an active workflow, the capability matches, any required
approval was granted) and issues a short-lived, operation-scoped JWT.

The tool server then validates this token before executing. Without a valid token,
the tool server refuses the operation — even if the caller has a valid mTLS
certificate.

### Specification

**1. New API Endpoint: POST /api/authz/operation-token**

Request (from agent, authenticated via API key):

```json
{
  "agent_name": "helpdesk-01",
  "capability": "ad-password-reset",
  "target": "jsmith",
  "target_type": "user",
  "tool_server_url": "https://tool01.montanifarms.com:8443",
  "workflow_context": {
    "ticket_number": "INC0012345",
    "workflow_execution_id": "wfe-abc-123",
    "approval_id": "apr-789"
  }
}
```

The `workflow_context` fields provide traceability. Some may be null depending on
the workflow configuration — for example, `approval_id` is null if the workflow
doesn't have an approval step. The portal should NOT reject requests where
`approval_id` is null — the decision about whether approval is required is made
by the workflow definition, not by the token issuance endpoint. The token endpoint
validates that the request is plausible, not that every possible gate was passed.

However, the intermediary chat should examine what workflow/execution tracking
currently exists in the portal. If `WorkflowExecution` records exist and are
populated by the agent, the token endpoint should validate against them. If the
agent doesn't currently create WorkflowExecution records (because the composable
engine isn't implemented yet), the token endpoint should still accept the request
but with lighter validation — essentially verifying that:
- The requesting agent is registered and active
- The capability exists and is mapped to a tool server
- The tool server URL matches a registered tool server
- The target is provided (non-empty)
- Rate limits are not exceeded (see below)

This lighter validation is the v1.0 posture. When the composable workflow engine
is implemented in the future, the validation can be tightened to require a valid
WorkflowExecution record in the correct state.

Response (success):

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6InByeC0yMDI2MDIifQ...",
  "expires_in": 300,
  "expires_at": "2026-02-27T10:35:00Z"
}
```

Response (failure):

```json
{
  "error": "capability_not_found",
  "message": "No active capability mapping found for 'ad-password-reset'"
}
```

Error codes:
- `agent_not_found` — agent name doesn't match a registered agent
- `agent_inactive` — agent is registered but not in active status
- `capability_not_found` — capability name doesn't exist or has no active mapping
- `tool_server_mismatch` — the requested tool server URL doesn't match any
  registered tool server for this capability
- `rate_limited` — agent has exceeded the token request rate limit
- `internal_error` — portal error during token generation

**2. Token Structure (JWT)**

Header:
```json
{
  "alg": "HS256",
  "typ": "JWT",
  "kid": "prx-202602"
}
```

Payload:
```json
{
  "jti": "550e8400-e29b-41d4-a716-446655440000",
  "iat": 1740000000,
  "exp": 1740000300,
  "iss": "praxova-portal",
  "sub": "helpdesk-01",
  "cap": "ad-password-reset",
  "target": "jsmith",
  "target_type": "user",
  "ts_url": "https://tool01.montanifarms.com:8443",
  "ticket": "INC0012345",
  "wfe": "wfe-abc-123",
  "apr": "apr-789"
}
```

Claim descriptions:
- `jti` — Unique token ID (UUID). Used for replay prevention.
- `iat` — Issued at (Unix timestamp)
- `exp` — Expiration (Unix timestamp). 300 seconds (5 minutes) after `iat`.
- `iss` — Always "praxova-portal"
- `sub` — The agent name that requested the token
- `cap` — The capability name this token authorizes
- `target` — The target entity (username, group name, path — depends on capability)
- `target_type` — Type of target: "user", "group", "path"
- `ts_url` — The specific tool server URL this token is valid for
- `ticket` — ServiceNow ticket number (for audit trail linkage)
- `wfe` — Workflow execution ID (null if not tracked yet)
- `apr` — Approval ID (null if no approval step)

**Signing key decision:**

The intermediary chat should examine the portal's existing JWT signing infrastructure
(used for portal authentication tokens / session management from Phase 2). There are
two options:

Option A: **Reuse the existing JWT signing key** for operation tokens. Simpler, less
key material to manage. The `kid` header and/or a custom claim (e.g., `"purpose":
"operation"`) distinguishes operation tokens from session tokens.

Option B: **Dedicated signing key for operation tokens.** More isolation — compromise
of the session signing key doesn't enable forging operation tokens. But more key
material to manage and distribute.

For v1.0, I recommend **Option A** (reuse) unless the intermediary chat finds a
compelling reason in the codebase to separate them. The signing key is already
protected by the secrets store. Adding a second key doubles the key management
surface without a proportional security benefit at this stage. Option B can be
adopted later when JWT key rotation (JWKS) is implemented in v1.1.

**Algorithm choice:**

If the portal and tool server share a symmetric key (HMAC): use **HS256**.
If the portal has an asymmetric key pair and the tool server gets only the public
key: use **RS256** or **ES256**.

HS256 is simpler for v1.0 because both the portal and tool server already share a
trust relationship (the tool server trusts the portal's CA). The shared secret
(the signing key) needs to be provisioned to the tool server — this can be done
alongside the TLS cert provisioning (the `provision-toolserver-certs.ps1` script).

RS256/ES256 is more principled because the tool server only needs the public key
(can validate but not forge). But it requires asymmetric key generation and
distribution.

The intermediary chat should choose based on what the existing JWT infrastructure
uses. If the portal already has an asymmetric key pair, use it. If it uses HMAC,
stay with HMAC for v1.0.

**3. Rate Limiting**

To prevent a compromised agent from requesting unlimited tokens (enabling a burst
of unauthorized operations):

- **Per-agent limit:** 60 token requests per minute (1 per second average).
  This is generous for normal operation (a busy agent might process a few tickets
  per minute) but constrains an attacker trying to use a compromised agent for
  mass operations.

- **Per-capability limit:** 30 token requests per minute per capability. Prevents
  a compromised agent from requesting 60 password resets per minute while staying
  under the per-agent limit.

- **Global limit:** 120 token requests per minute across all agents. Provides a
  ceiling regardless of how many agents are registered.

Rate limit state should be in-memory (not persisted). A portal restart resets the
counters, which is acceptable — the window is 60 seconds.

When rate limited, return HTTP 429 with a `Retry-After` header.

**4. Nonce Tracking**

The `jti` claim is a UUID nonce. The portal tracks issued nonces for the token
lifetime (5 minutes) plus a grace period (1 minute = 6 minutes total). This
tracking is used by the tool server to prevent replay attacks — but the portal
also tracks them for audit purposes.

Nonce storage should be in-memory (a `ConcurrentDictionary<string, DateTime>` with
a background cleanup task that removes expired entries). This is ephemeral — a
portal restart clears the nonce cache, which is acceptable because outstanding
tokens will also expire within 5 minutes.

**5. Audit Events**

Every token issuance and every token denial must be recorded as an audit event
(using the hash chain from Phase 2):

- `EventType`: `OperationTokenIssued` or `OperationTokenDenied`
- `AgentId`: The requesting agent
- `Capability`: The requested capability
- `DetailJson`: The full token request (for issuance) or the denial reason
  (for denial)
- Tool server URL, target, ticket number — captured in detail

**6. Public Key Distribution Endpoint**

The tool server needs the portal's signing key (public key for RS256/ES256, or
shared secret for HS256) to validate tokens. For v1.0, use **static provisioning**:

- The signing key (or public key) is exported during tool server cert provisioning
  and deployed to the tool server alongside its TLS cert
- The tool server reads it from a config file at startup

Add an endpoint for future use (v1.1 JWKS-based rotation):

```
GET /api/authz/keys
```

Returns the current signing key's public component (for RS256/ES256) or a key
identifier (for HS256, where the actual key is not exposed via API). This endpoint
is authenticated (API key or mTLS).

For v1.0, the tool server does NOT call this endpoint at runtime — it uses the
statically provisioned key. The endpoint exists for forward compatibility.

---

## Component 2: Python Agent — Token Request

### What and Why

The agent currently calls tool server endpoints directly after resolving the tool
server URL through capability routing. The change: before every tool server HTTP
call, the agent first requests an operation token from the portal, then includes
that token in the Authorization header of the tool server request.

### Specification

**1. Find the Actual Tool Server Call Site**

The intermediary chat MUST examine the actual agent codebase to find where tool
server HTTP calls are made. Based on Phase 1 feedback, this is in the procedural
`TicketExecutor`, NOT in a composable WorkflowEngine with step executors.

Look for:
- HTTP calls (requests, httpx, aiohttp, urllib) to tool server URLs
- References to capability routing (asking the portal for tool server URLs)
- The password reset flow, group membership flow, account unlock flow —
  wherever these construct and send HTTP requests to the tool server

The token request must be inserted BETWEEN the capability resolution (getting the
tool server URL) and the actual HTTP call to the tool server.

**2. Token Request Function**

Create a function (or method on an appropriate class) that requests an operation
token from the portal:

```python
def request_operation_token(
    portal_url: str,
    api_key: str,
    agent_name: str,
    capability: str,
    target: str,
    target_type: str,
    tool_server_url: str,
    ticket_number: str | None = None,
    workflow_execution_id: str | None = None,
    approval_id: str | None = None,
) -> str:
    """
    Request an operation authorization token from the portal.

    Returns the JWT token string.
    Raises OperationTokenError on failure.
    """
```

The function should:
- POST to `{portal_url}/api/authz/operation-token`
- Include the agent's API key in the Authorization header (same auth mechanism
  used for other portal API calls)
- Parse the response and return the token string
- On failure, raise a clear exception that includes the error code and message
  from the portal response

**3. Integration into Tool Server Calls**

Wherever the agent currently calls the tool server, the flow becomes:

```python
# 1. Resolve tool server URL (existing capability routing)
tool_server_url = resolve_capability(capability_name)

# 2. NEW: Request operation token
try:
    operation_token = request_operation_token(
        portal_url=self.portal_url,
        api_key=self.api_key,
        agent_name=self.agent_name,
        capability=capability_name,
        target=target_username,  # or group name, or path
        target_type="user",     # or "group", or "path"
        tool_server_url=tool_server_url,
        ticket_number=ticket.number,
    )
except OperationTokenError as e:
    # Token request failed — cannot proceed with tool server call
    # Log the error, escalate the ticket
    logger.error(f"Operation token request failed: {e}")
    escalate_ticket(ticket, reason=f"Authorization failed: {e}")
    return

# 3. Call tool server with token (modified from existing call)
response = requests.post(
    f"{tool_server_url}/api/v1/tools/password/reset",
    json={"username": target_username, "new_password": new_password},
    headers={
        "Authorization": f"Bearer {operation_token}",
        # ... existing headers (mTLS is handled by the requests session/cert config)
    },
    cert=(client_cert_path, client_key_path),  # existing mTLS
    verify=ca_cert_path,  # existing CA trust
)
```

**4. Error Handling**

If the token request fails:
- Log the failure with the portal's error response
- Do NOT proceed to call the tool server
- Escalate the ticket (or fail the current operation, depending on the existing
  error handling pattern)
- The failure should be visible in the agent's log and in the portal's audit log
  (the portal logs token denials)

If the token request times out:
- Retry once with a short timeout (5 seconds)
- If the retry fails, treat it as a token request failure (escalate)

If the tool server returns 403 (token validation failed):
- Log the specific error from the tool server response
- Do NOT retry with the same token (it's either expired, wrong capability, or
  replayed — retrying won't help)
- Escalate the ticket

**5. Token Caching**

Do NOT cache operation tokens. Each tool server call gets a fresh token. The
tokens are short-lived (5 minutes) and operation-scoped (specific capability +
target), so caching provides negligible benefit and introduces the risk of
using a stale or wrong-scoped token.

The one exception: if a single workflow step makes multiple tool server calls
for the same operation (e.g., a retry), the same token CAN be reused within
its validity window. But do not cache across different operations or different
targets.

---

## Component 3: Tool Server — Token Validation

### What and Why

The tool server currently authenticates callers via mTLS only — it checks that the
caller's client certificate was signed by the Praxova CA. This proves identity
("you are a Praxova agent") but not authorization ("you are allowed to reset this
specific user's password right now").

The operation token adds the authorization layer. Every API call to a tool endpoint
must include a valid operation token. The tool server validates the token before
executing any operation.

### Specification

**1. JWT Validation Middleware**

Add JWT Bearer authentication to the tool server's ASP.NET Core pipeline. This
runs ALONGSIDE the existing mTLS validation, not instead of it. Both must pass.

```csharp
// In Program.cs or auth configuration:
builder.Services.AddAuthentication()
    .AddCertificate(options => { /* existing mTLS config */ })
    .AddJwtBearer("OperationToken", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "praxova-portal",

            ValidateAudience = false, // We use custom claim validation instead

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30), // Allow 30s clock skew

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = loadedSigningKey, // Loaded from config file at startup

            RequireExpirationTime = true,
            RequireSignedTokens = true,
        };
    });
```

The intermediary chat should examine the tool server's existing authentication
pipeline to determine the correct integration approach. The tool server may
already have an authentication scheme for mTLS — the JWT validation needs to
work alongside it, not replace it.

**2. Custom Authorization Policy**

Beyond standard JWT validation (signature, expiration, issuer), the tool server
must validate operation-specific claims. Create a custom authorization policy
or requirement handler:

```csharp
public class OperationTokenRequirement : IAuthorizationRequirement { }

public class OperationTokenHandler : AuthorizationHandler<OperationTokenRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationTokenRequirement requirement)
    {
        // 1. Extract claims from the validated JWT
        var capClaim = context.User.FindFirst("cap")?.Value;
        var targetClaim = context.User.FindFirst("target")?.Value;
        var targetTypeClaim = context.User.FindFirst("target_type")?.Value;
        var tsUrlClaim = context.User.FindFirst("ts_url")?.Value;
        var jtiClaim = context.User.FindFirst("jti")?.Value;

        // 2. Get the current HTTP context to compare claims against the request
        var httpContext = /* get from DI or context accessor */;
        var requestPath = httpContext.Request.Path.Value;
        var requestBody = /* deserialize request body */;

        // 3. Validate: capability matches the endpoint
        //    Map endpoints to capabilities:
        //    /api/v1/tools/password/reset → "ad-password-reset"
        //    /api/v1/tools/groups/add-member → "ad-group-add"
        //    /api/v1/tools/groups/remove-member → "ad-group-remove"
        //    etc.
        if (!CapabilityMatchesEndpoint(capClaim, requestPath))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // 4. Validate: target matches the request body
        //    The "target" claim should match the username/group/path in the
        //    request body. The exact field name depends on the endpoint.
        if (!TargetMatchesRequest(targetClaim, targetTypeClaim, requestBody))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // 5. Validate: tool server URL matches this server's own URL
        //    The tool server knows its own URL from configuration.
        if (!ToolServerUrlMatchesSelf(tsUrlClaim))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // 6. Validate: nonce not replayed
        if (!NonceTracker.TryConsume(jtiClaim))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

**3. Capability-to-Endpoint Mapping**

The tool server needs a mapping from capability names to API endpoints, so it can
verify that a token issued for "ad-password-reset" is being used to call
`/api/v1/tools/password/reset` and not `/api/v1/tools/groups/add-member`.

This mapping should be configured, not hardcoded:

```json
{
  "CapabilityEndpointMap": {
    "ad-password-reset": ["/api/v1/tools/password/reset"],
    "ad-group-add": ["/api/v1/tools/groups/add-member"],
    "ad-group-remove": ["/api/v1/tools/groups/remove-member"],
    "ad-account-unlock": ["/api/v1/tools/account/unlock"],
    "ntfs-permission-grant": ["/api/v1/tools/permissions/grant"],
    "ntfs-permission-revoke": ["/api/v1/tools/permissions/revoke"]
  }
}
```

Place this in the tool server's `appsettings.json` or a dedicated config file.

A capability can map to multiple endpoints if needed (e.g., a "group-management"
capability might authorize both add-member and remove-member). But a token for
one capability should NOT authorize endpoints mapped to a different capability.

**4. Target Matching**

The tool server must verify that the `target` claim in the token matches the
actual target in the request body. This prevents a token issued for resetting
jsmith's password from being used to reset jdoe's password.

The matching logic depends on the endpoint:

| Endpoint | Request Body Field | Token `target_type` |
|----------|-------------------|---------------------|
| /api/v1/tools/password/reset | `username` | `user` |
| /api/v1/tools/groups/add-member | `username` (user being added) | `user` |
| /api/v1/tools/groups/remove-member | `username` (user being removed) | `user` |
| /api/v1/tools/account/unlock | `username` | `user` |
| /api/v1/tools/permissions/grant | `path` | `path` |
| /api/v1/tools/permissions/revoke | `path` | `path` |

The intermediary chat should examine the actual request body schemas of each
tool server endpoint to determine the correct field names. The table above is
approximate — use whatever the actual code uses.

Target matching should be **case-insensitive** for usernames (AD usernames are
case-insensitive) and **case-sensitive** for paths (NTFS paths are case-insensitive
on Windows, but case-sensitive matching in the token is safer — the token should
contain the path in the same case as the request).

**5. Nonce Tracking (Replay Prevention)**

The tool server maintains an in-memory set of consumed nonces (`jti` values).
When a token is presented:

1. Check if the `jti` is in the consumed set
2. If yes → reject (replay attack)
3. If no → add to consumed set, proceed with validation

The consumed set should be cleaned up periodically — remove entries older than
the token lifetime (5 minutes) plus clock skew (30 seconds) plus a buffer
(30 seconds) = 6 minutes. A background timer that runs every minute is sufficient.

Use a `ConcurrentDictionary<string, DateTime>` or similar thread-safe structure.
The DateTime value is the token's `exp` claim — entries are removed when
`DateTime.UtcNow > exp + buffer`.

On tool server restart, the nonce cache is empty. This means a token issued before
the restart could theoretically be replayed once after the restart. This is an
acceptable risk for v1.0 because:
- The token is still validated for signature, expiration, capability, and target
- The replay window is at most 5 minutes (token lifetime)
- Tool server restarts during active operation processing are rare

For v1.1, consider persisting nonces to a lightweight store (SQLite, file) if
this risk is deemed unacceptable.

**6. Health Endpoint Exemption**

The health check endpoint (`GET /api/health` or `/api/v1/health`) must NOT require
an operation token. It should continue to work with mTLS only (or unauthenticated,
depending on current configuration). Health checks are used by the portal for
tool server status monitoring and should not require a per-operation token.

Similarly, any endpoint that returns metadata (capabilities list, version info)
should be exempt from operation token requirements. Only endpoints that EXECUTE
OPERATIONS against AD or the filesystem require tokens.

The intermediary chat should examine the tool server's endpoint list and
categorize each as "operation" (requires token) or "metadata" (exempt).

**7. Error Responses**

When token validation fails, the tool server should return HTTP 403 with a
specific error body:

```json
{
  "error": "token_invalid",
  "detail": "Token capability 'ad-group-add' does not match endpoint capability 'ad-password-reset'"
}
```

Error codes:
- `token_missing` — No Authorization header or no Bearer token
- `token_expired` — Token's `exp` claim is in the past
- `token_signature_invalid` — Signature verification failed
- `token_issuer_invalid` — `iss` claim is not "praxova-portal"
- `token_capability_mismatch` — `cap` claim doesn't match the endpoint
- `token_target_mismatch` — `target` claim doesn't match the request body
- `token_server_mismatch` — `ts_url` claim doesn't match this tool server
- `token_replayed` — `jti` has already been consumed
- `token_invalid` — Generic validation failure

These errors should be logged at WARNING level on the tool server. They are
security-relevant events — a pattern of token validation failures may indicate
an attack.

**8. Signing Key Provisioning**

The tool server needs the portal's signing key to validate tokens. For v1.0,
this is statically provisioned:

- Extend the `provision-toolserver-certs.ps1` script to also deploy the
  signing key (or public key) to the tool server
- The key is stored in the same certificate directory on the tool server
  (e.g., `C:\Program Files\Praxova\ToolServer\certs\token-signing-key.json`
  or `.pem`, depending on format)
- The tool server reads this key at startup

The intermediary chat should examine how the tool server currently loads its
TLS certificates to determine the best pattern for loading the signing key.
Follow the same pattern (config file path, certificate store, etc.).

---

## Integration Testing

After all three components are modified, the following end-to-end tests validate
the complete authorization chain. These should be documented as test procedures
and ideally automated where possible.

### Positive Tests

**Test 1: Normal operation — full chain**
1. Create a test ticket in ServiceNow (or trigger a manual workflow)
2. Agent picks up the ticket, classifies it, resolves the tool server URL
3. Agent requests an operation token from the portal
4. Agent calls the tool server with the token + mTLS cert
5. Tool server validates both, executes the AD operation
6. Verify: operation succeeds, audit trail shows token issuance and execution

**Test 2: Operation without approval step**
1. Configure a workflow without an approval step
2. Run a ticket through it
3. Verify: token is issued with `apr: null`, tool server accepts it
4. The absence of an approval step is a workflow design choice, not a security
   violation — the token is still required

### Negative Tests (Security Validation)

**Test 3: mTLS cert without token → 403**
1. Using curl or a test script with the agent's client cert (mTLS)
2. Call the tool server directly WITHOUT an Authorization header
3. Expected: HTTP 403, error code `token_missing`
4. This proves that mTLS alone is no longer sufficient

```bash
# This should FAIL with 403 (previously it would have succeeded)
curl -sk --cert agent-client.crt --key agent-client.key \
  https://tool01:8443/api/v1/tools/password/reset \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}'
```

**Test 4: Expired token → 403**
1. Request a token from the portal
2. Wait 6 minutes (token expires after 5, plus clock skew)
3. Present the expired token to the tool server
4. Expected: HTTP 403, error code `token_expired`

**Test 5: Token for wrong capability → 403**
1. Request a token for capability "ad-group-add"
2. Use it to call `/api/v1/tools/password/reset`
3. Expected: HTTP 403, error code `token_capability_mismatch`

**Test 6: Token for wrong target → 403**
1. Request a token targeting user "jsmith"
2. Use it to call password reset for user "jdoe"
3. Expected: HTTP 403, error code `token_target_mismatch`

**Test 7: Replayed token → 403**
1. Request a token and use it successfully (operation executes)
2. Immediately reuse the same token for the same operation
3. Expected: HTTP 403, error code `token_replayed`

**Test 8: Token for wrong tool server → 403**
1. Request a token scoped to "https://tool01:8443"
2. Present it to a different tool server (if available) or modify the tool
   server's self-URL configuration to simulate a mismatch
3. Expected: HTTP 403, error code `token_server_mismatch`

**Test 9: Forged token → 403**
1. Create a JWT manually with the correct claims but signed with a different key
2. Present it to the tool server
3. Expected: HTTP 403, error code `token_signature_invalid`

**Test 10: Portal unreachable during token request → escalation**
1. Stop the portal container
2. Agent attempts to process a ticket that requires a tool server call
3. Agent attempts to request a token, fails (connection refused)
4. Expected: Agent escalates the ticket with a clear reason ("Authorization
   service unavailable"), does NOT call the tool server

### Regression Tests

**Test 11: Existing workflows still work**
1. Run a password reset ticket end-to-end
2. Run a group membership change ticket end-to-end
3. Run an account unlock ticket end-to-end
4. Verify all succeed with the new token requirement in place

**Test 12: Tool server health check still works**
1. Call `GET /api/v1/health` (or equivalent) without a token
2. Expected: HTTP 200 (health checks are exempt)
3. Portal health monitoring of tool server still works

---

## Implementation Notes for the Intermediary Chat

### Critical: Examine the actual agent code

The agent-side integration is the most uncertain part of this spec because the
Phase 1 feedback revealed the agent architecture differs from the documentation.
Before writing the Claude Code prompt, the intermediary chat MUST:

1. Find where tool server HTTP calls are made (the procedural TicketExecutor)
2. Find how capability routing currently works (how the agent gets tool server URLs)
3. Find how mTLS is configured on the agent's HTTP client
4. Find how the agent currently handles tool server call failures
5. Determine the agent's HTTP library (requests, httpx, etc.)

The token request function should follow the same patterns as existing portal API
calls in the agent codebase.

### Critical: Examine the tool server's existing auth pipeline

The tool server already has mTLS. Adding JWT validation must not break it. The
intermediary chat should:

1. Find how mTLS is currently configured (Kestrel options? Middleware? Custom handler?)
2. Find how endpoints are currently protected (is there an [Authorize] attribute?
   A middleware? Nothing?)
3. Determine whether the tool server uses controller-based routing or minimal APIs
4. Find the existing health check endpoint and its current auth requirements

### Signing key format

The portal's JWT signing key format determines the tool server's validation
configuration. The intermediary chat should check:

- Is the portal's signing key stored as a JWK, PEM, or raw bytes?
- Is it symmetric (HMAC) or asymmetric (RSA/ECDSA)?
- Where is it stored and how is it loaded at startup?

Then configure the tool server's `TokenValidationParameters.IssuerSigningKey`
accordingly.

### File locations to examine (approximate)

Portal:
- JWT signing configuration: look for `AddJwtBearer`, `JwtSecurityToken`,
  `SigningCredentials`, or `TokenValidationParameters` in the Web layer
- Existing API key authentication middleware
- `AuditEvent` creation helper (from Phase 2)

Agent:
- Main executor: look for `TicketExecutor`, `pipeline`, `executor` in
  `agent/src/agent/`
- Capability routing: look for `capability`, `routing`, `resolve` in
  `agent/src/agent/`
- HTTP client configuration: look for `requests.`, `httpx.`, `Session` in
  the tool server call code
- mTLS cert configuration: look for `cert=`, `verify=`, `ssl` in HTTP calls

Tool Server:
- Program.cs or Startup.cs for the auth pipeline
- Controller classes for endpoint definitions
- Existing mTLS configuration
- appsettings.json for current configuration structure

### Git commit guidance

```
feat(portal): add operation token issuance endpoint with rate limiting
feat(portal): add nonce tracking for operation tokens
feat(portal): add signing key distribution endpoint (forward compat)
feat(portal): audit events for operation token issuance and denial
feat(agent): request operation token before tool server calls
feat(agent): handle token request failures with escalation
feat(toolserver): add JWT validation middleware for operation tokens
feat(toolserver): custom authorization policy for claim validation
feat(toolserver): nonce replay prevention for operation tokens
feat(toolserver): capability-to-endpoint mapping configuration
test: end-to-end operation token authorization chain
test: negative security tests for token validation
```

### What NOT to change

- Do not modify the PKI certificate generation logic
- Do not modify the secrets encryption/decryption implementation
- Do not modify the ServiceNow connector
- Do not modify the LLM classification logic
- Do not modify the existing mTLS authentication — ADD JWT validation alongside it
- Do not modify the capability routing logic in the portal — the token endpoint
  uses it for validation but does not change how it works
- Do not implement JWKS-based key rotation — that's v1.1; use static provisioning
- Do not implement the composable workflow engine — integrate with the actual
  procedural TicketExecutor
