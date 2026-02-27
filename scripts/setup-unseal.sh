#!/bin/bash
set -e
# ─────────────────────────────────────────────────────────────────
# Praxova Unseal Passphrase Setup
# Creates /etc/praxova/unseal.env with appropriate permissions
# ─────────────────────────────────────────────────────────────────

UNSEAL_FILE="/etc/praxova/unseal.env"
PRAXOVA_DIR="/etc/praxova"

echo "Praxova Unseal Passphrase Setup"
echo "================================"
echo "This creates the secure unseal passphrase file at ${UNSEAL_FILE}"
echo "The passphrase is used to derive the master encryption key for all stored credentials."
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "ERROR: This script must be run as root (sudo ./scripts/setup-unseal.sh)"
    exit 1
fi

# Create directory
mkdir -p "${PRAXOVA_DIR}"
chmod 700 "${PRAXOVA_DIR}"

# Check if file already exists
if [ -f "${UNSEAL_FILE}" ]; then
    echo "WARNING: ${UNSEAL_FILE} already exists."
    read -rp "Overwrite? [y/N] " CONFIRM
    if [[ ! "$CONFIRM" =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 0
    fi
fi

# Get passphrase
echo ""
read -rsp "Enter unseal passphrase (or press Enter to generate a random one): " PASSPHRASE
echo ""

if [ -z "$PASSPHRASE" ]; then
    PASSPHRASE=$(openssl rand -base64 32)
    echo "Generated random passphrase (save this somewhere secure):"
    echo "  ${PASSPHRASE}"
    echo ""
fi

# Write file
printf "PRAXOVA_UNSEAL_PASSPHRASE=%s\n" "$PASSPHRASE" > "${UNSEAL_FILE}"
chmod 600 "${UNSEAL_FILE}"
chown root:root "${UNSEAL_FILE}"

echo "Unseal passphrase written to ${UNSEAL_FILE}"
echo "Permissions set to 600 (root read/write only)"
echo ""
echo "Next steps:"
echo "  1. Start the stack: docker compose up -d"
echo "  2. Verify portal unseals: docker compose logs admin-portal | grep -i unseal"
echo ""
echo "IMPORTANT: If you lose this passphrase, you cannot access stored credentials."
echo "Back it up to a secure location (password manager, HSM, etc.)."
