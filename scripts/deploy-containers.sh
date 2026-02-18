#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Praxova — Deploy Containers to Remote Docker Host
# Transfers image tarballs and starts the stack via SSH.
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ARTIFACTS_DIR="${PROJECT_ROOT}/build/artifacts"
REMOTE_DIR="/opt/praxova"

# Defaults
SKIP_OLLAMA=false

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS] DOCKER_HOST RELEASE_TAG [ENV_FILE]

Deploy Praxova containers to a remote Docker host.

Arguments:
  DOCKER_HOST    SSH target (e.g., deploy@192.168.1.100)
  RELEASE_TAG    Release tag to deploy
  ENV_FILE       Path to .env file (default: .env in project root)

Options:
  --skip-ollama  Skip deploying ollama image tarball
  -h, --help     Show this help message

Prerequisites:
  - SSH key-based auth configured for the deploy user
  - Docker and docker compose installed on remote host
  - NVIDIA Container Toolkit installed for GPU support
EOF
    exit 0
}

# Parse options
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-ollama) SKIP_OLLAMA=true; shift ;;
        -h|--help) usage ;;
        -*) echo "ERROR: Unknown option: $1" >&2; exit 1 ;;
        *) break ;;
    esac
done

DOCKER_HOST="${1:?ERROR: Missing DOCKER_HOST argument. Run with --help for usage.}"
RELEASE_TAG="${2:?ERROR: Missing RELEASE_TAG argument. Run with --help for usage.}"
ENV_FILE="${3:-${PROJECT_ROOT}/.env}"

# ── Validate local artifacts ─────────────────────────────────────
ADMIN_TAR="${ARTIFACTS_DIR}/praxova-admin-${RELEASE_TAG}.tar"
AGENT_TAR="${ARTIFACTS_DIR}/praxova-agent-${RELEASE_TAG}.tar"
OLLAMA_TAR="${ARTIFACTS_DIR}/ollama-latest.tar"

for f in "$ADMIN_TAR" "$AGENT_TAR"; do
    if [[ ! -f "$f" ]]; then
        echo "ERROR: Missing artifact: $f" >&2
        echo "Run: scripts/build-containers.sh ${RELEASE_TAG}" >&2
        exit 1
    fi
done

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: .env file not found: ${ENV_FILE}" >&2
    exit 1
fi

echo "═══════════════════════════════════════════════════════════════"
echo "  Praxova Container Deploy"
echo "  Host:    ${DOCKER_HOST}"
echo "  Tag:     ${RELEASE_TAG}"
echo "  Env:     ${ENV_FILE}"
echo "  Remote:  ${REMOTE_DIR}"
echo "═══════════════════════════════════════════════════════════════"

# ── Prepare remote host ──────────────────────────────────────────
echo ""
echo "── Preparing remote host ──────────────────────────────────────"
ssh "$DOCKER_HOST" "sudo mkdir -p ${REMOTE_DIR} && sudo chown \$(whoami): ${REMOTE_DIR}"

# ── Transfer artifacts ───────────────────────────────────────────
echo ""
echo "── Transferring artifacts ─────────────────────────────────────"

echo "  Syncing admin image..."
rsync -ahP "$ADMIN_TAR" "${DOCKER_HOST}:${REMOTE_DIR}/"

echo "  Syncing agent image..."
rsync -ahP "$AGENT_TAR" "${DOCKER_HOST}:${REMOTE_DIR}/"

if [[ "$SKIP_OLLAMA" != "true" ]] && [[ -f "$OLLAMA_TAR" ]]; then
    echo "  Syncing ollama image (large — rsync provides resume support)..."
    rsync -ahP "$OLLAMA_TAR" "${DOCKER_HOST}:${REMOTE_DIR}/"
fi

echo "  Copying docker-compose.yml..."
rsync -ahP "${PROJECT_ROOT}/docker-compose.yml" "${DOCKER_HOST}:${REMOTE_DIR}/"

echo "  Copying .env file..."
rsync -ahP "$ENV_FILE" "${DOCKER_HOST}:${REMOTE_DIR}/.env"
ssh "$DOCKER_HOST" "chmod 600 ${REMOTE_DIR}/.env"

# ── Load images and deploy on remote ─────────────────────────────
echo ""
echo "── Loading images on remote ───────────────────────────────────"

ssh "$DOCKER_HOST" bash <<REMOTE
set -euo pipefail
cd ${REMOTE_DIR}

echo "  Loading praxova-admin:${RELEASE_TAG}..."
docker load -i praxova-admin-${RELEASE_TAG}.tar

echo "  Loading praxova-agent:${RELEASE_TAG}..."
docker load -i praxova-agent-${RELEASE_TAG}.tar

if [[ -f ollama-latest.tar ]]; then
    echo "  Loading ollama/ollama:latest..."
    docker load -i ollama-latest.tar
fi

echo ""
echo "── Stopping existing stack ────────────────────────────────────"
docker compose down --timeout 30 2>/dev/null || true

echo ""
echo "── Starting stack ─────────────────────────────────────────────"
docker compose up -d --no-build

echo ""
echo "── Container status ───────────────────────────────────────────"
docker compose ps
REMOTE

# ── Health check ─────────────────────────────────────────────────
echo ""
echo "── Health check ───────────────────────────────────────────────"

MAX_WAIT=60
INTERVAL=5
ELAPSED=0

while [[ $ELAPSED -lt $MAX_WAIT ]]; do
    if ssh "$DOCKER_HOST" "curl -sf http://localhost:5000/api/health/" > /dev/null 2>&1; then
        echo "  Admin portal is healthy!"
        echo ""
        echo "═══════════════════════════════════════════════════════════════"
        echo "  Deploy complete: praxova ${RELEASE_TAG} → ${DOCKER_HOST}"
        echo "═══════════════════════════════════════════════════════════════"
        exit 0
    fi
    ELAPSED=$((ELAPSED + INTERVAL))
    echo "  Waiting for portal... (${ELAPSED}/${MAX_WAIT}s)"
    sleep "$INTERVAL"
done

echo "ERROR: Admin portal health check failed after ${MAX_WAIT}s" >&2
echo "  Dumping recent portal logs:" >&2
ssh "$DOCKER_HOST" "cd ${REMOTE_DIR} && docker compose logs --tail=20 admin-portal" 2>/dev/null || true
exit 1
