#!/bin/bash
# ============================================================================
# cleanup.sh — Prepare VM for Proxmox template conversion
# ============================================================================
# This is the final step before Packer converts the VM to a template.
# It removes Packer-specific artifacts and resets machine identity so
# each clone gets fresh IDs, SSH host keys, and cloud-init state.
#
# IMPORTANT: The SSH hardening (password auth disabled) was written to
# sshd_config by harden.sh but sshd was NOT restarted — so Packer's
# current session stays alive. The hardened config takes effect on
# first boot of any clone. This is intentional.
# ============================================================================

set -euo pipefail
echo "=== Cleaning up for template conversion ==="

# --- Keep the packer user and sudoers intact ---
# Cloud-init recreates the user on clone, but the sudoers file must
# persist in the template so clones get passwordless sudo immediately.
# When Tofu manages deployments, it can inject its own user via cloud-init.
echo "Keeping packer user and sudoers for clone compatibility..."

# --- Clean cloud-init state ---
# Forces cloud-init to re-run on first boot of each clone
# This is how Tofu sets hostname, IP, SSH keys per-instance
echo "Resetting cloud-init state..."
cloud-init clean --logs

# --- Truncate machine-id ---
# systemd generates a new one on boot if this is empty
# Without this, all clones share the same machine-id (causes
# DHCP conflicts, duplicate syslog entries, etc.)
echo "Truncating machine-id..."
truncate -s 0 /etc/machine-id
rm -f /var/lib/dbus/machine-id
ln -s /etc/machine-id /var/lib/dbus/machine-id 2>/dev/null || true

# --- Remove SSH host keys ---
# Each clone should generate its own host keys on first boot
echo "Removing SSH host keys..."
rm -f /etc/ssh/ssh_host_*

# --- Clean apt cache ---
echo "Cleaning apt cache..."
apt-get autoremove -y
apt-get clean
rm -rf /var/lib/apt/lists/*

# --- Clear logs ---
echo "Clearing logs..."
find /var/log -type f -name "*.log" -exec truncate -s 0 {} \;
find /var/log -type f -name "*.gz" -delete
find /var/log -type f -name "*.1" -delete
journalctl --vacuum-time=0 2>/dev/null || true

# --- Clear shell history ---
echo "Clearing shell history..."
rm -f /root/.bash_history
rm -f /home/*/.bash_history
unset HISTFILE

# --- Clear temp files ---
rm -rf /tmp/*
rm -rf /var/tmp/*

# --- Sync filesystem ---
sync

echo "=== Cleanup complete — VM ready for template conversion ==="
