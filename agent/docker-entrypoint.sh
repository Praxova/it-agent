#!/bin/bash
set -e

export PYTHONPATH="${PYTHONPATH:-/app/src}"

echo "Praxova IT Agent starting..."
echo "  Admin Portal: ${ADMIN_PORTAL_URL}"
echo "  Agent Name:   ${AGENT_NAME}"
echo "  Poll Interval: ${POLL_INTERVAL}s"
echo "  Heartbeat:    ${HEARTBEAT_INTERVAL}s"
echo "  Log Level:    ${LOG_LEVEL}"

# Wait for Admin Portal to be healthy
echo "Waiting for Admin Portal at ${ADMIN_PORTAL_URL}..."
MAX_RETRIES=30
RETRY_COUNT=0
until curl -sf "${ADMIN_PORTAL_URL}/api/health/" > /dev/null 2>&1; do
    RETRY_COUNT=$((RETRY_COUNT + 1))
    if [ $RETRY_COUNT -ge $MAX_RETRIES ]; then
        echo "ERROR: Admin Portal not available after ${MAX_RETRIES} attempts. Exiting."
        exit 1
    fi
    echo "  Attempt ${RETRY_COUNT}/${MAX_RETRIES} — portal not ready, waiting 5s..."
    sleep 5
done
echo "Admin Portal is healthy. Starting agent..."

exec python -m agent.runtime.cli \
    --admin-url "${ADMIN_PORTAL_URL}" \
    --agent-name "${AGENT_NAME}" \
    --poll-interval "${POLL_INTERVAL}" \
    --heartbeat-interval "${HEARTBEAT_INTERVAL}" \
    --log-level "${LOG_LEVEL}"
