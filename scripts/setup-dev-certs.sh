#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Praxova IT Agent — Development Certificate Setup
# ─────────────────────────────────────────────────────────────────
# This script sets up locally-trusted TLS certificates for the
# Admin Portal using mkcert. Run this once on a new dev machine.
#
# Prerequisites:
#   - mkcert installed (https://github.com/FiloSottile/mkcert)
#   - libnss3-tools installed (for browser trust on Linux)
#
# Usage:
#   ./scripts/setup-dev-certs.sh
# ─────────────────────────────────────────────────────────────────

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
CERT_DIR="$PROJECT_ROOT/docker/certs"
CA_TRUST_DIR="$CERT_DIR/ca-trust"

echo "═══════════════════════════════════════════════════════════"
echo " Praxova IT Agent — Certificate Setup"
echo "═══════════════════════════════════════════════════════════"
echo ""

# Check for mkcert
if ! command -v mkcert &> /dev/null; then
    echo "ERROR: mkcert is not installed."
    echo ""
    echo "Install it:"
    echo "  Ubuntu/Debian: sudo apt install mkcert libnss3-tools"
    echo "  macOS:         brew install mkcert"
    echo "  Other:         https://github.com/FiloSottile/mkcert#installation"
    exit 1
fi

# Ensure directories exist
mkdir -p "$CERT_DIR"
mkdir -p "$CA_TRUST_DIR"

# Step 1: Install the local CA into system + browser trust stores
echo "[1/4] Installing mkcert local CA..."
mkcert -install
echo ""

# Step 2: Generate TLS certificate for the portal
echo "[2/4] Generating TLS certificate..."
cd "$CERT_DIR"
mkcert -cert-file cert.pem -key-file key.pem \
    localhost \
    127.0.0.1 \
    admin.praxova.local
echo ""

# Step 3: Copy the CA root to the container trust directory
echo "[3/4] Copying CA root for container trust..."
CA_ROOT="$(mkcert -CAROOT)/rootCA.pem"
if [ -f "$CA_ROOT" ]; then
    cp "$CA_ROOT" "$CA_TRUST_DIR/mkcert-ca.crt"
    echo "  Copied: $CA_ROOT → $CA_TRUST_DIR/mkcert-ca.crt"
else
    echo "  WARNING: Could not find mkcert CA root at $CA_ROOT"
    echo "  You may need to copy it manually."
fi
echo ""

# Step 4: Remind about /etc/hosts
echo "[4/4] Hosts file check..."
if grep -q "admin.praxova.local" /etc/hosts 2>/dev/null; then
    echo "  ✓ admin.praxova.local already in /etc/hosts"
else
    echo "  ⚠ Add this line to /etc/hosts:"
    echo "    127.0.0.1 admin.praxova.local"
    echo ""
    echo "  Run: echo '127.0.0.1 admin.praxova.local' | sudo tee -a /etc/hosts"
fi

echo ""
echo "═══════════════════════════════════════════════════════════"
echo " ✅ Certificate setup complete!"
echo ""
echo " Next steps:"
echo "   1. Rebuild the portal:  docker compose build admin-portal"
echo "   2. Start everything:    docker compose up -d"
echo "   3. Browse to:           https://admin.praxova.local:5001"
echo "═══════════════════════════════════════════════════════════"
