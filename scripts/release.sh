#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Praxova — Release Orchestrator
# Builds images, deploys to remote host, verifies health.
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# Defaults
DRY_RUN=false
SKIP_OLLAMA=false
DOCKER_HOST=""
TAG=""
ENV_FILE=""

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Build and deploy Praxova containers to a remote Docker host.

Options:
  --host HOST        SSH target (required, e.g., deploy@192.168.1.100)
  --tag TAG          Release tag (default: git describe --tags --always)
  --env-file FILE    Path to .env file (default: .env in project root)
  --skip-ollama      Skip building/deploying ollama image
  --dry-run          Show what would happen without executing
  -h, --help         Show this help message

Examples:
  $(basename "$0") --host deploy@proxmox-docker
  $(basename "$0") --host deploy@10.0.0.50 --tag v1.2.3 --skip-ollama
  $(basename "$0") --host deploy@10.0.0.50 --dry-run
EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --host) DOCKER_HOST="$2"; shift 2 ;;
        --tag) TAG="$2"; shift 2 ;;
        --env-file) ENV_FILE="$2"; shift 2 ;;
        --skip-ollama) SKIP_OLLAMA=true; shift ;;
        --dry-run) DRY_RUN=true; shift ;;
        -h|--help) usage ;;
        -*) echo "ERROR: Unknown option: $1" >&2; exit 1 ;;
        *) echo "ERROR: Unexpected argument: $1" >&2; exit 1 ;;
    esac
done

if [[ -z "$DOCKER_HOST" ]]; then
    echo "ERROR: --host is required" >&2
    echo "Run with --help for usage." >&2
    exit 1
fi

# Default tag from git
if [[ -z "$TAG" ]]; then
    TAG=$(cd "$PROJECT_ROOT" && git describe --tags --always 2>/dev/null || echo "dev")
fi

# ── Dry run ──────────────────────────────────────────────────────
if [[ "$DRY_RUN" == "true" ]]; then
    echo "═══════════════════════════════════════════════════════════════"
    echo "  Praxova Release — DRY RUN"
    echo "═══════════════════════════════════════════════════════════════"
    echo ""
    echo "  Would build:"
    echo "    - praxova-admin:${TAG}"
    echo "    - praxova-agent:${TAG}"
    [[ "$SKIP_OLLAMA" != "true" ]] && echo "    - ollama/ollama:latest (if not cached)"
    echo ""
    echo "  Would save tarballs to:"
    echo "    - build/artifacts/praxova-admin-${TAG}.tar"
    echo "    - build/artifacts/praxova-agent-${TAG}.tar"
    [[ "$SKIP_OLLAMA" != "true" ]] && echo "    - build/artifacts/ollama-latest.tar"
    echo ""
    echo "  Would deploy to:"
    echo "    - Host:   ${DOCKER_HOST}"
    echo "    - Remote: /opt/praxova/"
    echo "    - Env:    ${ENV_FILE:-${PROJECT_ROOT}/.env}"
    echo ""
    echo "  Would run on remote:"
    echo "    - docker load (all tarballs)"
    echo "    - docker compose down --timeout 30"
    echo "    - docker compose up -d --no-build"
    echo "    - Health check: curl http://localhost:5000/api/health/"
    echo ""
    echo "  Run without --dry-run to execute."
    exit 0
fi

# ── Build ────────────────────────────────────────────────────────
echo "═══════════════════════════════════════════════════════════════"
echo "  Praxova Release"
echo "  Tag:  ${TAG}"
echo "  Host: ${DOCKER_HOST}"
echo "═══════════════════════════════════════════════════════════════"

BUILD_ARGS=()
[[ "$SKIP_OLLAMA" == "true" ]] && BUILD_ARGS+=(--skip-ollama)
BUILD_ARGS+=("$TAG")

echo ""
echo "Phase 1: Build"
echo "───────────────────────────────────────────────────────────────"
"${SCRIPT_DIR}/build-containers.sh" "${BUILD_ARGS[@]}"

# ── Deploy ───────────────────────────────────────────────────────
DEPLOY_ARGS=()
[[ "$SKIP_OLLAMA" == "true" ]] && DEPLOY_ARGS+=(--skip-ollama)
DEPLOY_ARGS+=("$DOCKER_HOST" "$TAG")
[[ -n "$ENV_FILE" ]] && DEPLOY_ARGS+=("$ENV_FILE")

echo ""
echo "Phase 2: Deploy"
echo "───────────────────────────────────────────────────────────────"
"${SCRIPT_DIR}/deploy-containers.sh" "${DEPLOY_ARGS[@]}"

# ── Summary ──────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo "  Release Summary"
echo "───────────────────────────────────────────────────────────────"
echo "  Tag:      ${TAG}"
echo "  Host:     ${DOCKER_HOST}"
echo "  Images:   praxova-admin:${TAG}, praxova-agent:${TAG}"
[[ "$SKIP_OLLAMA" != "true" ]] && echo "            ollama/ollama:latest"
echo "  Status:   Healthy"
echo "═══════════════════════════════════════════════════════════════"
