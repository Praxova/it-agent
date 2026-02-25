#!/bin/bash
# ============================================================================
# harden.sh — SSH hardening + unattended security updates
# ============================================================================
# This runs during the Packer build BEFORE cleanup. Password auth stays
# enabled for now so Packer can finish its work. The cleanup script
# will handle final lockdown.
#
# Post-clone, Tofu will deploy SSH keys for the deploy user, and
# password auth will already be disabled from this step.
# ============================================================================

set -euo pipefail
echo "=== Hardening SSH and enabling unattended upgrades ==="

# --- SSH hardening ---
# Disable password authentication (Tofu will use SSH keys)
sed -i 's/^#*PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sed -i 's/^#*PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config
sed -i 's/^#*PubkeyAuthentication.*/PubkeyAuthentication yes/' /etc/ssh/sshd_config

# Disable challenge-response auth (another password vector)
sed -i 's/^#*KbdInteractiveAuthentication.*/KbdInteractiveAuthentication no/' /etc/ssh/sshd_config

# --- Unattended security upgrades ---
apt-get install -y unattended-upgrades
# Enable automatic security updates
cat > /etc/apt/apt.conf.d/20auto-upgrades << 'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT::Periodic::AutocleanInterval "7";
EOF

# --- Basic firewall (allow SSH only, Tofu will open app ports) ---
apt-get install -y ufw
ufw default deny incoming
ufw default allow outgoing
ufw allow ssh
# Don't enable yet — Tofu will enable after configuring app ports
# ufw --force enable

echo "=== Hardening complete ==="
