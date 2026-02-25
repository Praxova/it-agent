# Claude Code Prompt: Agent Trust Bootstrap — Fetch Portal CA at Startup

## Step 0: Git Branch Setup

```bash
cd /home/alton/Documents/lucid-it-agent
git checkout master
git checkout -b feature/agent-trust-bootstrap
```

Commit with messages prefixed `trust:`.

## Context

Read these files first:
- `agent/Dockerfile` (current build-time cert trust)
- `agent/docker-entrypoint.sh` (current startup flow)
- `docker-compose.yml` (agent service definition)

### Current State

The admin portal now auto-generates its own TLS certificates via an internal CA (ADR-014). The CA trust bundle is available at `GET /api/pki/trust-bundle` (HTTP, no auth required).

The agent container currently:
1. Copies static cert files from `agent/certs/ca-trust/` at **build time**
2. Runs `update-ca-certificates` at **build time**
3. Talks to the portal over **HTTP** (`http://admin-portal:5000`)
4. Runs as non-root user `agent` (can't run `update-ca-certificates` at runtime)

### Goal

At container startup, the agent fetches the portal's CA trust bundle over HTTP and installs it so that:
- Python's `httpx` and `requests` libraries trust the portal's auto-generated HTTPS cert
- The agent can communicate with the portal over HTTPS
- No Docker rebuild needed when the CA changes (cert rotation, new install)
- The build-time cert mechanism remains as a fallback for additional certs (tool server, DC, etc.)

---

## Implementation

### 1. Modify `agent/docker-entrypoint.sh`

Add trust bootstrap between the portal health check and the agent start. The key insight: since the agent runs as non-root, we can't call `update-ca-certificates`. Instead, we create a combined CA bundle and set `SSL_CERT_FILE` so Python's SSL stack uses it.

```bash
#!/bin/bash
set -e

export PYTHONPATH="${PYTHONPATH:-/app/src}"

echo "Praxova IT Agent starting..."
echo "  Admin Portal: ${ADMIN_PORTAL_URL}"
echo "  Agent Name:   ${AGENT_NAME}"
echo "  Poll Interval: ${POLL_INTERVAL}s"
echo "  Heartbeat:    ${HEARTBEAT_INTERVAL}s"
echo "  Log Level:    ${LOG_LEVEL}"

# ── Wait for Admin Portal ────────────────────────────────────────
# Use the HTTP bootstrap URL to check health (before we have TLS trust)
BOOTSTRAP_URL="${ADMIN_PORTAL_BOOTSTRAP_URL:-${ADMIN_PORTAL_URL}}"
echo "Waiting for Admin Portal at ${BOOTSTRAP_URL}..."
MAX_RETRIES=30
RETRY_COUNT=0
until curl -sf "${BOOTSTRAP_URL}/api/health/" > /dev/null 2>&1; do
    RETRY_COUNT=$((RETRY_COUNT + 1))
    if [ $RETRY_COUNT -ge $MAX_RETRIES ]; then
        echo "ERROR: Admin Portal not available after ${MAX_RETRIES} attempts. Exiting."
        exit 1
    fi
    echo "  Attempt ${RETRY_COUNT}/${MAX_RETRIES} — portal not ready, waiting 5s..."
    sleep 5
done
echo "Admin Portal is healthy."

# ── Trust Bootstrap ──────────────────────────────────────────────
# Fetch the portal's internal CA certificate and add it to our trust bundle.
# This allows HTTPS communication with the portal without Docker rebuilds.
# Uses HTTP for the fetch (bootstrap problem: can't trust HTTPS before we have the CA).
echo "Fetching portal CA trust bundle..."
TRUST_BUNDLE_URL="${BOOTSTRAP_URL}/api/pki/trust-bundle"
PRAXOVA_CA_FILE="/tmp/praxova-ca.pem"
COMBINED_BUNDLE="/tmp/combined-ca-bundle.pem"

if curl -sf "${TRUST_BUNDLE_URL}" -o "${PRAXOVA_CA_FILE}" 2>/dev/null; then
    if [ -s "${PRAXOVA_CA_FILE}" ]; then
        # Validate it looks like a PEM certificate
        if grep -q "BEGIN CERTIFICATE" "${PRAXOVA_CA_FILE}"; then
            # Combine system CAs + portal CA into a single bundle
            SYSTEM_BUNDLE="/etc/ssl/certs/ca-certificates.crt"
            if [ -f "${SYSTEM_BUNDLE}" ]; then
                cat "${SYSTEM_BUNDLE}" "${PRAXOVA_CA_FILE}" > "${COMBINED_BUNDLE}"
            else
                cp "${PRAXOVA_CA_FILE}" "${COMBINED_BUNDLE}"
            fi
            # Set env vars so Python SSL trusts our combined bundle
            export SSL_CERT_FILE="${COMBINED_BUNDLE}"
            export REQUESTS_CA_BUNDLE="${COMBINED_BUNDLE}"
            echo "  Portal CA trusted (combined bundle at ${COMBINED_BUNDLE})"
        else
            echo "  WARNING: Trust bundle response is not a PEM certificate. Skipping."
        fi
    else
        echo "  WARNING: Empty trust bundle response. Skipping."
    fi
else
    echo "  WARNING: Could not fetch trust bundle from ${TRUST_BUNDLE_URL}. Continuing without portal CA trust."
    echo "  (HTTPS connections to the portal may fail if using auto-generated certificates)"
fi

# ── Start Agent ──────────────────────────────────────────────────
echo "Starting agent..."
exec python -m agent.runtime.cli \
    --admin-url "${ADMIN_PORTAL_URL}" \
    --agent-name "${AGENT_NAME}" \
    --poll-interval "${POLL_INTERVAL}" \
    --heartbeat-interval "${HEARTBEAT_INTERVAL}" \
    --log-level "${LOG_LEVEL}"
```

**Key design decisions:**
- `ADMIN_PORTAL_BOOTSTRAP_URL` — Separate env var for the HTTP URL used only for health check and trust fetch. Defaults to `ADMIN_PORTAL_URL` if not set (backward compatible).
- `ADMIN_PORTAL_URL` — Can now be set to HTTPS. The agent passes this to `--admin-url` for all runtime communication.
- Trust fetch failure is a **warning, not a fatal error**. The agent should still start — it may work fine over HTTP, or the certs might already be trusted via build-time mechanism.
- `SSL_CERT_FILE` and `REQUESTS_CA_BUNDLE` — Both set because `httpx` uses `SSL_CERT_FILE` (via Python's `ssl` module) and `requests` uses `REQUESTS_CA_BUNDLE`. Setting both covers all Python HTTP libraries.

### 2. Update `docker-compose.yml`

```yaml
agent-helpdesk-01:
    environment:
      # Bootstrap URL — HTTP, used only for health check and trust bundle fetch
      ADMIN_PORTAL_BOOTSTRAP_URL: http://admin-portal:5000
      # Runtime URL — HTTPS, used for all agent↔portal communication
      ADMIN_PORTAL_URL: https://admin-portal:5001
      AGENT_NAME: test-agent
      # ... rest unchanged
```

This is the key change: `ADMIN_PORTAL_URL` switches to HTTPS. The bootstrap URL stays HTTP for the initial trust fetch.

### 3. Clean Up Build-Time Certs (Optional)

The `agent/certs/ca-trust/` directory currently has:
- `dc-ldaps.crt` — DC LDAPS cert (agent doesn't talk to DC directly — this can be removed)
- `toolserver.crt` — Tool server cert (may still be needed if tool server uses its own cert)
- `.gitkeep`

For now, **leave the build-time mechanism in place**. It's a useful fallback for certs that can't be fetched dynamically (e.g., tool server TLS if it's not using the portal's CA). Just note in a comment in the Dockerfile that runtime trust bootstrap is preferred:

```dockerfile
# Static CA certificates — fallback for certs not managed by the portal's internal PKI.
# The portal's CA is fetched dynamically at runtime via docker-entrypoint.sh.
COPY certs/ca-trust/ /usr/local/share/ca-certificates/custom/
RUN update-ca-certificates 2>/dev/null || true
```

---

## Files to Modify
- `agent/docker-entrypoint.sh` — Add trust bootstrap section
- `docker-compose.yml` — Add `ADMIN_PORTAL_BOOTSTRAP_URL`, change `ADMIN_PORTAL_URL` to HTTPS
- `agent/Dockerfile` — Update comment on ca-trust COPY (optional, cosmetic)

## Files NOT to Modify
- Agent Python code — `httpx` automatically picks up `SSL_CERT_FILE`
- Portal code — Trust bundle endpoint already exists and works
- SealManager / EncryptionService — Unrelated

---

## Testing

1. Rebuild: `docker compose up -d --build`
2. Watch agent logs: `docker compose logs -f agent-helpdesk-01`
3. Verify startup output shows:
   ```
   Waiting for Admin Portal at http://admin-portal:5000...
   Admin Portal is healthy.
   Fetching portal CA trust bundle...
     Portal CA trusted (combined bundle at /tmp/combined-ca-bundle.pem)
   Starting agent...
   ```
4. Verify the agent successfully communicates with the portal over HTTPS (check agent logs for successful configuration fetch / heartbeat — no SSL errors)
5. If you have a running ServiceNow instance, verify end-to-end ticket processing still works

### Failure mode test
1. Stop the portal: `docker compose stop admin-portal`
2. Start agent: `docker compose start agent-helpdesk-01`
3. Verify: Agent retries health check, eventually fails after 30 attempts (150 seconds)
4. Start portal again, restart agent — should work normally
