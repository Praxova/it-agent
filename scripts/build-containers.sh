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
SKIP_OLLAMA=false
TAG=""

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS] [TAG]

Build Praxova container images and save as tarballs.

Arguments:
  TAG                 Release tag (default: git describe --tags --always)

Options:
  --skip-ollama       Always skip saving ollama image tarball
  -h, --help          Show this help message

Note: By default, the ollama tarball is skipped if it already exists
in the artifacts directory (it's large and rarely changes).
EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-ollama) SKIP_OLLAMA=true; shift ;;
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

# ── Pull ollama ──────────────────────────────────────────────────
if [[ "$SKIP_OLLAMA" != "true" ]]; then
    echo ""
    echo "── Pulling ollama/ollama:latest ────────────────────────────"
    docker pull ollama/ollama:latest
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

OLLAMA_TAR="${ARTIFACTS_DIR}/ollama-latest.tar"
if [[ "$SKIP_OLLAMA" == "true" ]]; then
    echo "  Skipping ollama (--skip-ollama)"
elif [[ -f "$OLLAMA_TAR" ]]; then
    echo "  Skipping ollama (tarball already cached at ${OLLAMA_TAR})"
else
    echo "  Saving ollama/ollama:latest (this may take a while)..."
    docker save "ollama/ollama:latest" -o "$OLLAMA_TAR"
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
