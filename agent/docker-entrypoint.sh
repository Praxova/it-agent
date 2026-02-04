#!/bin/bash
set -e

export PYTHONPATH="${PYTHONPATH:-/app/src}"

echo "Lucid IT Agent starting..."
echo "  Admin Portal: ${ADMIN_PORTAL_URL}"
echo "  Agent Name:   ${AGENT_NAME}"
echo "  Poll Interval: ${POLL_INTERVAL}s"
echo "  Heartbeat:    ${HEARTBEAT_INTERVAL}s"
echo "  Log Level:    ${LOG_LEVEL}"

exec python -m agent.runtime.cli \
    --admin-url "${ADMIN_PORTAL_URL}" \
    --agent-name "${AGENT_NAME}" \
    --poll-interval "${POLL_INTERVAL}" \
    --heartbeat-interval "${HEARTBEAT_INTERVAL}" \
    --log-level "${LOG_LEVEL}"
