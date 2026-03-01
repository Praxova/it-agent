# Claude Code Prompt: Fix LLM Container Certificate Bootstrap

## Context

The Praxova stack has three containers that need TLS certificates from the portal's internal PKI:
- **admin-portal** — self-issues its own cert at startup
- **agent** — fetches cert via `GET /api/pki/certificates/agent/{agentName}` using an Agent-role API key → **works**
- **llm** (llama.cpp server) — tries `POST /api/pki/certificates/issue` using the same Agent-role API key → **returns 403**

The `/api/pki/certificates/issue` endpoint uses `.RequireAuthorization(AuthorizationPolicies.RequireAdmin)`,
but the shared `LUCID_API_KEY` in docker-compose is an Agent-role key. The LLM container restart-loops
because `set -e` + `exit 1` causes a restart on the 403.

## Problem

Infrastructure containers (LLM, and potentially future internal services) need a cert bootstrap path
that works with Agent-role API keys. We should NOT require a separate admin key for internal cert provisioning.

## Key File

All PKI endpoints are in a single file:
**`admin/dotnet/src/LucidAdmin.Web/Endpoints/PkiEndpoints.cs`**

The patterns you need are already in this file:
- **Auth policy:** `.RequireAuthorization(AuthorizationPolicies.RequireAgent)` — used by the agent cert endpoint
- **Cert issuance:** `pkiService.IssueCertificateAsync(name, commonName, sanDnsNames, sanIpAddresses, lifetimeDays)` — used by the admin issue endpoint
- **Response shape:** `IssueCertificateResponse` record (already defined in the file)
- **CA PEM getter:** `pkiService.GetCaCertificatePem()`

## Design Decision: Don't Copy the Agent Identity Pattern

The agent cert endpoint (`/certificates/agent/{agentName}`) does strict identity validation —
it extracts `agent_id` from the API key claims and verifies the requesting agent matches the
requested cert. This makes sense for agents (each agent should only get its own cert).

For infrastructure services, this pattern does NOT apply because:
- The LLM container is not an `Agent` entity in the database
- It shares the same API key as the agent (one key per deployment)
- We just need to verify the caller has a valid Agent-role key AND the requested service name is allowed

## Requirements

### 1. Add a new endpoint in PkiEndpoints.cs

Add `GET /api/pki/certificates/service/{serviceName}` in the `MapPkiEndpoints` method:

- **Auth:** `.RequireAuthorization(AuthorizationPolicies.RequireAgent)` (same as agent cert endpoint)
- **No identity validation** — don't check `agent_id` claims. Any valid Agent-role key can request infrastructure certs.
- **Allow-list validation** — only these service names are permitted:

```csharp
var allowedServices = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    ["llm"] = new[] { "llm", "praxova-llm", "localhost" },
    ["praxova-llm"] = new[] { "llm", "praxova-llm", "localhost" },
    // Future: add more infrastructure services here
};
```

- If `serviceName` is not in the allow-list, return 404 with `{ "error": "unknown_service" }`
- Call `pkiService.IssueCertificateAsync()` with:
  - `name`: `$"service-tls-{serviceName}"` (e.g., `"service-tls-llm"`)
  - `commonName`: the serviceName
  - `sanDnsNames`: from the allow-list lookup
  - `sanIpAddresses`: null
  - `lifetimeDays`: 90
- Return `IssueCertificateResponse` (already defined in the file) with the cert, key, and CA PEM
- Add `.WithName("GetServiceCert")` and `.WithDescription(...)`

### 2. Update the LLM container entrypoint

File: **`llm/docker-entrypoint.sh`**

Replace the cert provisioning section (Step 4, the `POST /api/pki/certificates/issue` call) with:

```bash
SERVICE_CERT_URL="${PORTAL_URL}/api/pki/certificates/service/praxova-llm"

RESPONSE=$(curl -sf "${SERVICE_CERT_URL}" \
    -H "X-API-Key: ${API_KEY}" \
    2>&1)
```

Remove the `ISSUE_BODY` JSON construction. Keep everything else (cert-exists check, PEM parsing, file writes).

### 3. Do NOT change

- The existing `/api/pki/certificates/issue` endpoint — stays Admin-only
- The existing `/api/pki/certificates/agent/{agentName}` endpoint — unchanged
- The agent's entrypoint — unchanged
- API key roles, `AuthorizationPolicies`, or the auth middleware — unchanged
- Any record types — reuse `IssueCertificateResponse` as-is

## Testing

After implementation:

```bash
# 1. Rebuild portal
cd /home/alton/Documents/lucid-it-agent
docker compose build admin-portal

# 2. Delete existing LLM certs to force re-provisioning
docker compose stop llm
docker volume rm praxova-llm-certs 2>/dev/null; true

# 3. Start the stack
docker compose up -d

# 4. Verify LLM gets its cert
docker compose logs --tail 20 llm
# Should see: "TLS certificate provisioned successfully."
# Should NOT see: restart loop

# 5. Test endpoint with Agent-role API key
curl -sk https://localhost:5001/api/pki/certificates/service/praxova-llm \
  -H "X-API-Key: $LUCID_API_KEY" \
  -w '\nHTTP: %{http_code}\n'
# Expected: 200 with JSON cert response

# 6. Test without auth
curl -sk https://localhost:5001/api/pki/certificates/service/praxova-llm \
  -w '\nHTTP: %{http_code}\n'
# Expected: 401

# 7. Test unknown service name
curl -sk https://localhost:5001/api/pki/certificates/service/unknown-thing \
  -H "X-API-Key: $LUCID_API_KEY" \
  -w '\nHTTP: %{http_code}\n'
# Expected: 404

# 8. Verify LLM healthcheck passes
curl -sk https://localhost:8443/health
# Expected: llama.cpp health response (or connection if model not loaded)
```

## Commit Message

```
fix(pki): add service cert endpoint for infrastructure containers

The /api/pki/certificates/issue endpoint requires Admin role, but
infrastructure containers (LLM server) share the Agent-role API key.

Add GET /api/pki/certificates/service/{serviceName} that:
- Accepts Agent-role API keys (same policy as agent cert endpoint)
- Issues server TLS certs for allowed infrastructure service names
- Uses predefined SAN lists per service (allow-list pattern)
- Returns existing IssueCertificateResponse shape

Update LLM entrypoint to use the new endpoint instead of the
admin-only issue endpoint.

Fixes: LLM container restart loop due to 403 on cert provisioning
```
