# Security Phases — End-to-End Functional Test Plan

## Pre-Test: Nuke and Re-Seed

All security phases include first-startup initialization logic (recovery key generation,
audit genesis record, PKI initialization, signing key generation). A fresh database
is required to validate the complete first-run experience.

```bash
cd /home/alton/Documents/lucid-it-agent

# 1. Stop everything
docker compose down

# 2. Wipe portal data (DB, certs, seal keys)
docker volume rm praxova-admin-data

# 3. Rebuild all containers (picks up all code changes)
docker compose up -d --build

# 4. Watch portal initialize
docker compose logs -f admin-portal
```

**Expected first-startup output (verify ALL of these appear):**
```
Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE
Generated new JWT signing key and stored encrypted in database
Internal PKI initialized — CA generated and stored encrypted
Admin portal TLS certificate issued
Default admin user created — password change required on first login
Recovery key generated — SAVE THIS KEY (displayed only once)
Audit log initialized — genesis record created
Operation token signing key generated
```

**IMPORTANT:** Copy the recovery key from the logs. You'll need it for Test 2.7.

After initialization is complete, stop watching logs (Ctrl+C) and proceed.

---

## Phase 1 Tests — Foundation Fixes

### Test 1.1: HTTP Bootstrap Endpoint

The CA trust bundle should be available over plain HTTP (no redirect to HTTPS).

```bash
# Should return PEM certificate, NOT a redirect
curl -v http://localhost:5000/api/pki/trust-bundle 2>&1 | head -20

# Verify it's a valid PEM
curl -s http://localhost:5000/api/pki/trust-bundle | openssl x509 -noout -subject -issuer
```

**Expected:** HTTP 200 with `-----BEGIN CERTIFICATE-----` content.
**Fail if:** HTTP 301/302 redirect to HTTPS.

### Test 1.2: HTTPS Works with Fetched CA

```bash
# Fetch CA cert
curl -s http://localhost:5000/api/pki/trust-bundle > /tmp/praxova-ca.pem

# Use it to verify HTTPS
curl --cacert /tmp/praxova-ca.pem https://localhost:5001/api/health/
```

**Expected:** HTTP 200, valid JSON health response, no SSL errors.

---

## Phase 2 Tests — Portal Authentication & Audit

### Test 2.1: Default Admin — Forced Password Change

**UI Test (manual or Chrome MCP):**
1. Open https://localhost:5001 in browser
2. Log in with default credentials (admin/admin or whatever the default is)
3. **Expected:** Immediately redirected to password change page
4. **Expected:** Cannot navigate away from password change page
5. Enter new password meeting policy (12+ chars, 3 of 4 complexity types)
6. **Expected:** Password changed successfully, redirected to dashboard

```bash
# API equivalent: login should succeed but indicate password change required
curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq
```

**Expected:** Response indicates `must_change_password: true` (check actual field name).

### Test 2.2: Password Policy Enforcement

**After changing to a known password, test policy violations:**

```bash
# Too short (< 12 chars)
curl -sk https://localhost:5001/api/auth/change-password \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"current_password":"YourNewPassword1!","new_password":"Short1!"}' | jq
# Expected: 400, password policy violation

# No complexity (all lowercase)
curl -sk https://localhost:5001/api/auth/change-password \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"current_password":"YourNewPassword1!","new_password":"alllowercaseletters"}' | jq
# Expected: 400, password policy violation

# Common password (if top-1000 check implemented)
curl -sk https://localhost:5001/api/auth/change-password \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"current_password":"YourNewPassword1!","new_password":"Password1234!"}' | jq
# Expected: 400, common password rejected
```

### Test 2.3: Account Lockout

```bash
# Attempt 5 failed logins
for i in {1..5}; do
  echo "Attempt $i:"
  curl -sk https://localhost:5001/api/auth/login \
    -H 'Content-Type: application/json' \
    -d '{"username":"admin","password":"wrong-password"}' | jq -r '.error // .message'
done

# 6th attempt with CORRECT password — should be locked
curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"YourActualPassword1!"}' | jq
```

**Expected:** First 5 return "invalid credentials". 6th returns "account locked" even with correct password.

**After 15 minutes (or adjust lockout duration for testing):**
```bash
# Should succeed again
curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"YourActualPassword1!"}' | jq
```

### Test 2.4: Audit Log Hash Chain

```bash
# Get auth token first
TOKEN=$(curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"YourActualPassword1!"}' | jq -r .token)

# Verify hash chain integrity
curl -sk https://localhost:5001/api/audit/verify \
  -H "Authorization: Bearer $TOKEN" | jq
```

**Expected:** Verification passes — no gaps, no hash mismatches, genesis record present.

### Test 2.5: Audit Events from Login Tests

```bash
# Check audit log for the login events we just generated
curl -sk https://localhost:5001/api/audit?eventType=PortalLoginFailed \
  -H "Authorization: Bearer $TOKEN" | jq
```

**Expected:** 5+ failed login events from Test 2.3, each with timestamp and source IP.

### Test 2.6: Recovery Key Display

**UI Test:** During the first-startup flow (after password change), the UI should
display the recovery key with a confirmation checkbox. Verify:
1. Recovery key is displayed clearly
2. A confirmation checkbox exists ("I have saved this key")
3. After confirmation, the key is never shown again
4. Navigating to the recovery key page again does NOT display the key

### Test 2.7: Recovery Key Functionality

This is a destructive test — only run if you want to verify recovery key works:

1. Note the current recovery key (from first startup)
2. Stop the portal
3. Change the `PRAXOVA_UNSEAL_PASSPHRASE` environment variable to a wrong value
4. Start the portal — it should fail to unseal
5. Use the recovery key to unseal (check the actual mechanism — API endpoint or startup param)
6. Verify the portal unseals and operates normally

---

## Phase 3 Tests — Operation Authorization Tokens

### Test 3.0: Setup Prerequisites

Before testing operation tokens, you need:
- An agent registered in the portal with an API key
- A tool server registered and online
- A capability mapping (e.g., "ad-password-reset" mapped to the tool server)
- A ServiceNow service account configured (or manual trigger)

```bash
# Create API key via portal UI, then:
export API_KEY="prx_your_key_here"
export PORTAL="https://localhost:5001"

# Verify agent is registered
curl -sk $PORTAL/api/agents \
  -H "Authorization: Bearer $TOKEN" | jq
```

### Test 3.1: Token Issuance — Happy Path

```bash
# Request an operation token
curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "ad-password-reset",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://tool01.montanifarms.com:8443",
    "workflow_context": {
      "ticket_number": "INC0012345"
    }
  }' | jq
```

**Expected:** JSON with `token`, `expires_in: 300`, `expires_at` ~5 minutes from now.

```bash
# Decode the token (don't validate, just inspect)
TOKEN_JWT=$(curl -sk ... | jq -r .token)  # repeat above command, capture token
echo $TOKEN_JWT | cut -d. -f2 | base64 -d 2>/dev/null | jq
```

**Expected claims:** `cap: "ad-password-reset"`, `target: "testuser"`, `sub: "helpdesk-01"`, etc.

### Test 3.2: Token Issuance — Invalid Capability

```bash
curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "nonexistent-capability",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://tool01.montanifarms.com:8443"
  }' | jq
```

**Expected:** 400/404 with `error: "capability_not_found"`.

### Test 3.3: Token Issuance — Wrong Tool Server URL

```bash
curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "ad-password-reset",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://wrong-server:8443"
  }' | jq
```

**Expected:** 400 with `error: "tool_server_mismatch"`.

### Test 3.4: Rate Limiting

```bash
# Fire 65 requests rapidly (limit is 60/min per agent)
for i in $(seq 1 65); do
  STATUS=$(curl -sk -o /dev/null -w '%{http_code}' $PORTAL/api/authz/operation-token \
    -H "X-Api-Key: $API_KEY" \
    -H 'Content-Type: application/json' \
    -d '{
      "agent_name": "helpdesk-01",
      "capability": "ad-password-reset",
      "target": "testuser'$i'",
      "target_type": "user",
      "tool_server_url": "https://tool01.montanifarms.com:8443"
    }')
  echo "Request $i: HTTP $STATUS"
done
```

**Expected:** First ~60 return 200, remaining return 429 with `Retry-After` header.

### Test 3.5: Tool Server — Token Required

```bash
# Call tool server WITHOUT token (should fail)
curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}' \
  -w '\nHTTP Status: %{http_code}\n'
```

**Expected:** HTTP 403, `error: "token_missing"`.

### Test 3.6: Tool Server — Expired Token

```bash
# Get a token, wait for it to expire, then use it
OPTOKEN=$(curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "ad-password-reset",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://tool01.montanifarms.com:8443"
  }' | jq -r .token)

echo "Token obtained. Waiting 330 seconds for expiry (5 min + 30s clock skew)..."
sleep 330

curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}' | jq
```

**Expected:** HTTP 403, `error: "token_expired"`.

### Test 3.7: Tool Server — Capability Mismatch

```bash
# Get token for group-add, use it for password-reset
OPTOKEN=$(curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "ad-group-add",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://tool01.montanifarms.com:8443"
  }' | jq -r .token)

curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}' | jq
```

**Expected:** HTTP 403, `error: "token_capability_mismatch"`.

### Test 3.8: Tool Server — Target Mismatch

```bash
# Get token for testuser, use it for differentuser
OPTOKEN=$(curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "ad-password-reset",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://tool01.montanifarms.com:8443"
  }' | jq -r .token)

curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "differentuser", "new_password": "Test1234!"}' | jq
```

**Expected:** HTTP 403, `error: "token_target_mismatch"`.

### Test 3.9: Tool Server — Replay Prevention

```bash
# Get a token and use it twice
OPTOKEN=$(curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "agent_name": "helpdesk-01",
    "capability": "ad-password-reset",
    "target": "testuser",
    "target_type": "user",
    "tool_server_url": "https://tool01.montanifarms.com:8443"
  }' | jq -r .token)

# First use — should succeed (if AD is reachable) or at least pass token validation
curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}' -w '\nHTTP: %{http_code}\n'

# Second use — same token — should be rejected
curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}' | jq
```

**Expected:** First call succeeds (or fails for AD reasons, but NOT token reasons). Second call returns 403, `error: "token_replayed"`.

### Test 3.10: Tool Server — Forged Token

```bash
# Create a fake token with the right claims but wrong signature
HEADER=$(echo -n '{"alg":"HS256","typ":"JWT"}' | base64 -w0 | tr '/+' '_-' | tr -d '=')
PAYLOAD=$(echo -n '{"jti":"fake-id","iat":1740000000,"exp":9999999999,"iss":"praxova-portal","sub":"helpdesk-01","cap":"ad-password-reset","target":"testuser"}' | base64 -w0 | tr '/+' '_-' | tr -d '=')
FAKE_TOKEN="${HEADER}.${PAYLOAD}.fake-signature-here"

curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $FAKE_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}' | jq
```

**Expected:** HTTP 403, `error: "token_signature_invalid"`.

### Test 3.11: Tool Server — Health Check Still Works

```bash
# Health endpoint should NOT require a token
curl -sk https://tool01.montanifarms.com:8443/api/v1/health | jq
```

**Expected:** HTTP 200, health status response.

### Test 3.12: Audit Trail for Token Operations

```bash
# Check that token issuance and denials are in the audit log
curl -sk $PORTAL/api/audit?eventType=OperationTokenIssued \
  -H "Authorization: Bearer $TOKEN" | jq

curl -sk $PORTAL/api/audit?eventType=OperationTokenDenied \
  -H "Authorization: Bearer $TOKEN" | jq
```

**Expected:** Events from all the token tests above are logged with correct details.

---

## Phase 4a Tests — AD Authentication

### Test 4a.1: AD Login

**Prerequisites:** AD service account configured in portal, AD auth enabled, user
exists in AD and is a member of `Praxova-Admins` (or configured group).

**UI Test:**
1. Log out of portal
2. Log in with AD credentials (domain username + password)
3. **Expected:** Login succeeds
4. **Expected:** Header shows username and role (e.g., "jsmith (Admin, AD)")
5. **Expected:** Full admin access available

```bash
# API test
curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"jsmith","password":"AD-Password-Here"}' | jq
```

**Expected:** JWT token returned with `auth_method: "ad"` and correct role.

### Test 4a.2: AD User Without Group Membership

Log in with an AD user who is NOT in any Praxova group.

**Expected:** Login rejected with "Access denied — not a member of any Praxova access group."

### Test 4a.3: AD Unreachable — Fallback to Local

```bash
# Simulate AD being unreachable (e.g., block port 636 from portal container)
# Then log in with the local admin account
curl -sk https://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"YourLocalPassword1!"}' | jq
```

**Expected:** Login succeeds via local auth fallback. Audit log shows `PortalLoginFallback` event.

### Test 4a.4: AD Login Audit Events

```bash
curl -sk $PORTAL/api/audit?eventType=PortalLoginSuccess \
  -H "Authorization: Bearer $TOKEN" | jq
```

**Expected:** AD login events show `auth_method: "ad"`, local logins show `auth_method: "local"`.

---

## Phase 4b Tests — llama.cpp Server

### Test 4b.1: LLM Server Health

```bash
# HTTPS health check (should work with Praxova CA)
curl --cacert /tmp/praxova-ca.pem https://localhost:8443/health
```

**Expected:** HTTP 200, server is healthy.

### Test 4b.2: LLM Inference

```bash
curl --cacert /tmp/praxova-ca.pem https://localhost:8443/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "local",
    "messages": [{"role": "user", "content": "Say hello in exactly 3 words."}],
    "temperature": 0.1,
    "max_tokens": 20
  }' | jq '.choices[0].message.content'
```

**Expected:** A coherent response, proving inference works over HTTPS.

### Test 4b.3: Agent Uses LLM Over HTTPS

```bash
# Check agent logs for LLM communication
docker compose logs agent-helpdesk-01 2>&1 | grep -i "llm\|classification\|prompt"
```

**Expected:** Agent connects to LLM server via HTTPS (no plain HTTP calls to port 11434).

### Test 4b.4: No Plain HTTP on Old Port

```bash
# Old Ollama port should not be listening
curl -s http://localhost:11434/api/tags 2>&1
```

**Expected:** Connection refused (port 11434 not in use).

---

## Phase 4c Tests — mTLS

### Test 4c.1: Agent Has Client Cert

```bash
# Check agent logs for client cert acquisition
docker compose logs agent-helpdesk-01 2>&1 | grep -i "client cert\|mtls\|certificate"
```

**Expected:** Log entries showing successful client cert fetch from portal.

### Test 4c.2: Tool Server Call With mTLS + Token

This is the full chain — verified by processing an actual ticket or calling the
tool server directly with both credentials:

```bash
# Get the agent's client cert (check where the agent stores it)
# Then call tool server with both cert and token
OPTOKEN=$(curl -sk $PORTAL/api/authz/operation-token \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{...}' | jq -r .token)

curl -sk --cert /path/to/agent-client.crt --key /path/to/agent-client.key \
  https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}'
```

**Expected:** Operation succeeds (200, assuming AD user exists).

### Test 4c.3: No Client Cert — Token Only → 403

```bash
# Call with valid token but NO client cert
curl -sk https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}'
```

**Expected:** HTTP 403, client certificate required.

### Test 4c.4: Wrong CA Client Cert → 403

```bash
# Generate a self-signed client cert (not from Praxova CA)
openssl req -x509 -newkey rsa:2048 -keyout /tmp/fake-key.pem -out /tmp/fake-cert.pem \
  -days 1 -nodes -subj "/CN=fake-agent"

curl -sk --cert /tmp/fake-cert.pem --key /tmp/fake-key.pem \
  https://tool01.montanifarms.com:8443/api/v1/tools/password/reset \
  -H "Authorization: Bearer $OPTOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}'
```

**Expected:** HTTP 403 or TLS handshake failure.

### Test 4c.5: Health Check Without Client Cert

```bash
curl -sk https://tool01.montanifarms.com:8443/api/v1/health
```

**Expected:** HTTP 200 (health exempt from mTLS requirement).

---

## Phase 5a Tests — Dynamic Classification

### Test 5a.1: Classification Config in Export

```bash
curl -sk $PORTAL/api/classification/export \
  -H "Authorization: Bearer $TOKEN" | jq '.categories'
```

**Expected:** Array of categories with names, descriptions, workflow mappings.

### Test 5a.2: Add Category via Portal, Agent Picks It Up

**UI Test:**
1. Add a new category "vpn_access" in the portal
2. Add 2-3 few-shot examples for it
3. Wait 5 minutes (or restart agent)
4. Create a test ticket: "I need VPN access to work remotely"
5. **Expected:** Agent classifies as "vpn_access"

### Test 5a.3: Disable Category

**UI Test:**
1. Disable the "vpn_access" category in portal
2. Wait for agent refresh
3. Create a ticket that would match vpn_access
4. **Expected:** Agent does NOT classify as vpn_access (escalates or picks next best)

### Test 5a.4: No Hardcoded Categories

```bash
# Verify no _TYPE_MAP or hardcoded categories in agent code
docker compose exec agent-helpdesk-01 grep -r "TYPE_MAP\|password_reset\|group_access" \
  /app/agent/src/agent/classifier/ || echo "No hardcoded categories found (PASS)"
```

**Expected:** No matches (all categories come from portal).

---

## Phase 5b Tests — Certificate Auto-Renewal

### Test 5b.1: Certificate Health Dashboard

**UI Test:**
1. Navigate to certificate health page in portal
2. **Expected:** Shows all component certs with expiry dates and status indicators
3. **Expected:** All certs show green/OK status (freshly issued)

### Test 5b.2: Portal Self-Renewal (Simulated)

This requires issuing a short-lived portal cert for testing:

```bash
# Check portal cert expiry
echo | openssl s_client -connect localhost:5001 2>/dev/null | \
  openssl x509 -noout -dates
```

For actual renewal testing, you'd need to either:
- Issue a cert with a very short lifetime (e.g., 2 days)
- Or verify the background task logic via unit tests
- Or fast-forward the system clock (risky in a containerized environment)

**Pragmatic approach:** Verify the background task exists and runs, check logs for
cert expiry checking, and rely on unit tests for the renewal logic itself.

### Test 5b.3: Agent Cert Renewal Endpoint

```bash
# Call the renewal endpoint (should reject if cert not near expiry)
curl -sk $PORTAL/api/pki/certificates/renew \
  -H "X-Api-Key: $API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{"current_cert_serial": "XX:XX:XX", "agent_name": "helpdesk-01"}' | jq
```

**Expected:** Rejected with "Certificate not within renewal window" (cert was just issued).

---

## Full End-to-End Integration Test

### Test E2E.1: Complete Ticket Lifecycle

This is the golden path test that validates all phases working together:

1. **Setup:** Portal configured, agent running, tool server online, AD reachable
2. **Create ticket** in ServiceNow (or manual trigger): "Please reset password for testuser"
3. **Watch agent logs:** `docker compose logs -f agent-helpdesk-01`
4. **Expected sequence:**
   - Agent picks up ticket from ServiceNow
   - Agent sends ticket to LLM (llama.cpp) over HTTPS ← Phase 4b
   - LLM classifies as "password_reset" using dynamic categories ← Phase 5a
   - Agent requests operation token from portal ← Phase 3
   - Agent calls tool server with token + mTLS client cert ← Phase 3 + 4c
   - Tool server validates token (signature, capability, target, nonce)
   - Tool server validates client cert (Praxova CA)
   - Tool server executes password reset via LDAPS to DC
   - Agent updates ServiceNow ticket with result
   - Audit trail records every step ← Phase 2 hash chain

5. **Verify audit trail:**
```bash
curl -sk $PORTAL/api/audit \
  -H "Authorization: Bearer $TOKEN" | jq '.[0:10]'
```

**Expected:** Complete chain of events from classification through execution.

6. **Verify audit integrity:**
```bash
curl -sk $PORTAL/api/audit/verify \
  -H "Authorization: Bearer $TOKEN" | jq
```

**Expected:** Hash chain intact after all operations.

---

## Test Results Tracking

| Test ID | Phase | Description | Result | Notes |
|---------|-------|-------------|--------|-------|
| 1.1 | 1 | HTTP bootstrap endpoint | | |
| 1.2 | 1 | HTTPS with fetched CA | | |
| 2.1 | 2 | Forced password change | | |
| 2.2 | 2 | Password policy enforcement | | |
| 2.3 | 2 | Account lockout | | |
| 2.4 | 2 | Audit hash chain verification | | |
| 2.5 | 2 | Login audit events | | |
| 2.6 | 2 | Recovery key display | | |
| 2.7 | 2 | Recovery key functionality | | |
| 3.1 | 3 | Token issuance happy path | | |
| 3.2 | 3 | Token — invalid capability | | |
| 3.3 | 3 | Token — wrong tool server | | |
| 3.4 | 3 | Rate limiting | | |
| 3.5 | 3 | Tool server — no token | | |
| 3.6 | 3 | Tool server — expired token | | |
| 3.7 | 3 | Tool server — capability mismatch | | |
| 3.8 | 3 | Tool server — target mismatch | | |
| 3.9 | 3 | Tool server — replay prevention | | |
| 3.10 | 3 | Tool server — forged token | | |
| 3.11 | 3 | Tool server — health exempt | | |
| 3.12 | 3 | Token audit trail | | |
| 4a.1 | 4a | AD login | | |
| 4a.2 | 4a | AD user without group | | |
| 4a.3 | 4a | AD fallback to local | | |
| 4a.4 | 4a | AD login audit events | | |
| 4b.1 | 4b | LLM server health (HTTPS) | | |
| 4b.2 | 4b | LLM inference over HTTPS | | |
| 4b.3 | 4b | Agent uses HTTPS for LLM | | |
| 4b.4 | 4b | Old Ollama port closed | | |
| 4c.1 | 4c | Agent has client cert | | |
| 4c.2 | 4c | Full mTLS + token call | | |
| 4c.3 | 4c | No client cert → 403 | | |
| 4c.4 | 4c | Wrong CA cert → 403 | | |
| 4c.5 | 4c | Health exempt from mTLS | | |
| 5a.1 | 5a | Classification config export | | |
| 5a.2 | 5a | Dynamic category addition | | |
| 5a.3 | 5a | Disabled category | | |
| 5a.4 | 5a | No hardcoded categories | | |
| 5b.1 | 5b | Cert health dashboard | | |
| 5b.2 | 5b | Portal self-renewal | | |
| 5b.3 | 5b | Agent cert renewal endpoint | | |
| E2E.1 | All | Complete ticket lifecycle | | |
