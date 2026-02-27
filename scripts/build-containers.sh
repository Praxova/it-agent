#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Praxova — Build Container Images
# Builds admin portal and agent images, saves as tarballs for deploy.
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ARTIFACTS_DIR="${PROJECT_ROOT}/build/artifacts"

# Defaults
SKIP_LLM=false
TAG=""

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS] [TAG]

Build Praxova container images and save as tarballs.

Arguments:
  TAG                 Release tag (default: git describe --tags --always)

Options:
  --skip-llm          Skip building the LLM server image (large CUDA build)
  -h, --help          Show this help message

Note: By default, the LLM tarball is skipped if it already exists
in the artifacts directory (it's large and rarely changes).
EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-llm) SKIP_LLM=true; shift ;;
        -h|--help) usage ;;
        -*) echo "ERROR: Unknown option: $1" >&2; exit 1 ;;
        *) TAG="$1"; shift ;;
    esac
done

# Default tag from git
if [[ -z "$TAG" ]]; then
    TAG=$(cd "$PROJECT_ROOT" && git describe --tags --always 2>/dev/null || echo "dev")
fi

echo "═══════════════════════════════════════════════════════════════"
echo "  Praxova Container Build"
echo "  Tag: ${TAG}"
echo "═══════════════════════════════════════════════════════════════"

# Create artifacts directory
mkdir -p "$ARTIFACTS_DIR"

# ── Build admin portal ───────────────────────────────────────────
echo ""
echo "── Building praxova-admin:${TAG} ──────────────────────────────"
docker build \
    -t "praxova-admin:${TAG}" \
    -t "praxova-admin:latest" \
    -f "${PROJECT_ROOT}/admin/dotnet/Dockerfile" \
    "$PROJECT_ROOT"

# ── Build agent ──────────────────────────────────────────────────
echo ""
echo "── Building praxova-agent:${TAG} ──────────────────────────────"
docker build \
    -t "praxova-agent:${TAG}" \
    -t "praxova-agent:latest" \
    -f "${PROJECT_ROOT}/agent/Dockerfile" \
    "${PROJECT_ROOT}/agent"

# ── Build LLM server ────────────────────────────────────────────
if [[ "$SKIP_LLM" != "true" ]]; then
    echo ""
    echo "── Building praxova-llm:${TAG} ──────────────────────────────"
    docker build \
        -t "praxova-llm:${TAG}" \
        -t "praxova-llm:latest" \
        -f "${PROJECT_ROOT}/docker/llama-server/Dockerfile" \
        "${PROJECT_ROOT}/docker/llama-server"
fi

# ── Save tarballs ────────────────────────────────────────────────
echo ""
echo "── Saving image tarballs ──────────────────────────────────────"

echo "  Saving praxova-admin:${TAG}..."
docker save "praxova-admin:${TAG}" "praxova-admin:latest" \
    -o "${ARTIFACTS_DIR}/praxova-admin-${TAG}.tar"

echo "  Saving praxova-agent:${TAG}..."
docker save "praxova-agent:${TAG}" "praxova-agent:latest" \
    -o "${ARTIFACTS_DIR}/praxova-agent-${TAG}.tar"

LLM_TAR="${ARTIFACTS_DIR}/praxova-llm-${TAG}.tar"
if [[ "$SKIP_LLM" == "true" ]]; then
    echo "  Skipping LLM server (--skip-llm)"
elif [[ -f "$LLM_TAR" ]]; then
    echo "  Skipping LLM server (tarball already cached at ${LLM_TAR})"
else
    echo "  Saving praxova-llm:${TAG}..."
    docker save "praxova-llm:${TAG}" "praxova-llm:latest" -o "$LLM_TAR"
fi

# ── Summary ──────────────────────────────────────────────────────
echo ""
echo "── Build Artifacts ────────────────────────────────────────────"
printf "%-45s %10s  %s\n" "FILE" "SIZE" "SHA256"
echo "───────────────────────────────────────────────────────────────────────"
for f in "${ARTIFACTS_DIR}"/*.tar; do
    if [[ -f "$f" ]]; then
        SIZE=$(du -h "$f" | cut -f1)
        SHA=$(sha256sum "$f" | cut -d' ' -f1)
        printf "%-45s %10s  %.16s...\n" "$(basename "$f")" "$SIZE" "$SHA"
    fi
done

echo ""
echo "Build complete. Artifacts in: ${ARTIFACTS_DIR}"
