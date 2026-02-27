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

# ── Agent Client Certificate Bootstrap ──────────────────────────
# Fetch the agent's mTLS client certificate from the portal.
# This cert is presented to the tool server on every operation call.
# The portal issues it on first call and auto-renews when within 30 days of expiry.
AGENT_CERT_DIR="/tmp/praxova"
AGENT_CERT_FILE="${AGENT_CERT_DIR}/agent-client.crt"
AGENT_CERT_KEY_FILE="${AGENT_CERT_DIR}/agent-client.key"

mkdir -p "${AGENT_CERT_DIR}"

if [ -n "${LUCID_API_KEY}" ] && [ -n "${AGENT_NAME}" ]; then
    CERT_URL="${ADMIN_PORTAL_URL}/api/pki/certificates/agent/${AGENT_NAME}"
    echo "Fetching agent client certificate for mTLS..."

    CERT_RESPONSE=$(curl -sf \
        -H "X-API-Key: ${LUCID_API_KEY}" \
        --cacert "${COMBINED_BUNDLE}" \
        "${CERT_URL}" 2>/dev/null)

    if [ $? -eq 0 ] && [ -n "${CERT_RESPONSE}" ]; then
        # Parse JSON response and extract cert/key PEM fields
        echo "${CERT_RESPONSE}" | python3 -c "
import sys, json, os
data = json.load(sys.stdin)
cert_file = '${AGENT_CERT_FILE}'
key_file = '${AGENT_CERT_KEY_FILE}'
with open(cert_file, 'w') as f:
    f.write(data['certificatePem'])
with open(key_file, 'w') as f:
    f.write(data['privateKeyPem'])
os.chmod(key_file, 0o600)
print(f'  Agent client cert written to {cert_file}')
print(f'  Expires: {data.get(\"expiresAt\", \"unknown\")}')
"
        export AGENT_CLIENT_CERT="${AGENT_CERT_FILE}"
        export AGENT_CLIENT_KEY="${AGENT_CERT_KEY_FILE}"
        echo "  mTLS client cert bootstrap complete"
    else
        echo "  WARNING: Could not fetch agent client cert from ${CERT_URL}"
        echo "  Tool server calls will proceed without mTLS client cert (may fail if tool server requires it)"
    fi
else
    echo "  Skipping agent client cert bootstrap (LUCID_API_KEY or AGENT_NAME not set)"
fi

# ── Start Agent ──────────────────────────────────────────────────
echo "Starting agent..."
exec python -m agent.runtime.cli \
    --admin-url "${ADMIN_PORTAL_URL}" \
    --agent-name "${AGENT_NAME}" \
    --poll-interval "${POLL_INTERVAL}" \
    --heartbeat-interval "${HEARTBEAT_INTERVAL}" \
    --log-level "${LOG_LEVEL}"
