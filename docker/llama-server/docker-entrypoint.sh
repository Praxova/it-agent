#!/bin/bash
# ─────────────────────────────────────────────────────────────────
# Praxova llama.cpp Server — Docker Entrypoint
# ─────────────────────────────────────────────────────────────────
# Bootstraps TLS trust and provisions a certificate from the portal
# PKI before starting llama-server with native TLS.
#
# Required environment variables:
#   ADMIN_PORTAL_BOOTSTRAP_URL — HTTP URL for health check and CA fetch
#                                (e.g., http://admin-portal:5000)
#   ADMIN_PORTAL_URL           — HTTPS URL for PKI API calls
#                                (e.g., https://admin-portal:5001)
#   LUCID_API_KEY              — API key for PKI certificate issuance
# ─────────────────────────────────────────────────────────────────
set -e

echo "Praxova LLM Server (llama.cpp) starting..."
echo "  Admin Portal (bootstrap): ${ADMIN_PORTAL_BOOTSTRAP_URL}"
echo "  Admin Portal (HTTPS):     ${ADMIN_PORTAL_URL}"

# ── Step 1: Wait for Admin Portal ────────────────────────────────
BOOTSTRAP_URL="${ADMIN_PORTAL_BOOTSTRAP_URL:-http://admin-portal:5000}"
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

# ── Step 2: Fetch and install CA trust bundle ────────────────────
echo "Fetching portal CA trust bundle..."
TRUST_BUNDLE_URL="${BOOTSTRAP_URL}/api/pki/trust-bundle"
PRAXOVA_CA_FILE="/tmp/praxova-ca.pem"
COMBINED_BUNDLE="/tmp/combined-ca-bundle.pem"

if curl -sf "${TRUST_BUNDLE_URL}" -o "${PRAXOVA_CA_FILE}" 2>/dev/null; then
    if [ -s "${PRAXOVA_CA_FILE}" ] && grep -q "BEGIN CERTIFICATE" "${PRAXOVA_CA_FILE}"; then
        # Install into OS trust store
        cp "${PRAXOVA_CA_FILE}" /usr/local/share/ca-certificates/praxova-internal-ca.crt
        update-ca-certificates 2>/dev/null || true

        # Also build a combined bundle for SSL_CERT_FILE
        SYSTEM_BUNDLE="/etc/ssl/certs/ca-certificates.crt"
        if [ -f "${SYSTEM_BUNDLE}" ]; then
            cat "${SYSTEM_BUNDLE}" "${PRAXOVA_CA_FILE}" > "${COMBINED_BUNDLE}"
        else
            cp "${PRAXOVA_CA_FILE}" "${COMBINED_BUNDLE}"
        fi
        export SSL_CERT_FILE="${COMBINED_BUNDLE}"
        echo "  Portal CA trusted."
    else
        echo "  WARNING: Trust bundle response is not a valid PEM certificate. Skipping."
    fi
else
    echo "  WARNING: Could not fetch trust bundle. Continuing without portal CA trust."
fi

# ── Step 3: Check for existing TLS certs ─────────────────────────
CERT_FILE="/certs/llama-server.crt"
KEY_FILE="/certs/llama-server.key"

if [ -f "${CERT_FILE}" ] && [ -f "${KEY_FILE}" ]; then
    echo "TLS certificate already exists at ${CERT_FILE}. Skipping provisioning."
else
    # ── Step 4: Provision TLS certificate from portal PKI ────────
    echo "Provisioning TLS certificate from portal PKI..."

    PORTAL_URL="${ADMIN_PORTAL_URL:-https://admin-portal:5001}"
    API_KEY="${LUCID_API_KEY:-}"

    if [ -z "${API_KEY}" ]; then
        echo "ERROR: LUCID_API_KEY is not set. Cannot provision TLS certificate."
        exit 1
    fi

    SERVICE_CERT_URL="${PORTAL_URL}/api/pki/certificates/service/praxova-llm"

    RESPONSE=$(curl -sf "${SERVICE_CERT_URL}" \
        -H "X-API-Key: ${API_KEY}" \
        2>&1)

    if [ $? -ne 0 ]; then
        echo "ERROR: Certificate issuance failed. Response:"
        echo "${RESPONSE}"
        exit 1
    fi

    # Parse JSON response with grep/sed (no jq dependency)
    # Extract certificatePem
    CERT_PEM=$(echo "${RESPONSE}" | sed -n 's/.*"certificatePem"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | sed 's/\\n/\n/g')
    KEY_PEM=$(echo "${RESPONSE}" | sed -n 's/.*"privateKeyPem"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | sed 's/\\n/\n/g')
    CA_PEM=$(echo "${RESPONSE}" | sed -n 's/.*"caCertificatePem"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | sed 's/\\n/\n/g')

    if [ -z "${CERT_PEM}" ] || [ -z "${KEY_PEM}" ]; then
        echo "ERROR: Failed to parse certificate response. Raw response:"
        echo "${RESPONSE}" | head -c 500
        exit 1
    fi

    echo "${CERT_PEM}" > "${CERT_FILE}"
    echo "${KEY_PEM}" > "${KEY_FILE}"
    chmod 600 "${KEY_FILE}"

    if [ -n "${CA_PEM}" ]; then
        echo "${CA_PEM}" > /certs/ca.pem
    fi

    echo "  TLS certificate provisioned successfully."
fi

# ── Step 5: Start llama-server with TLS ──────────────────────────
echo "Starting llama-server..."
exec llama-server \
    --host 0.0.0.0 \
    --port 8443 \
    --model /models/model.gguf \
    --ssl-cert-file "${CERT_FILE}" \
    --ssl-key-file "${KEY_FILE}" \
    --n-gpu-layers -1 \
    --ctx-size 8192 \
    --parallel 2
