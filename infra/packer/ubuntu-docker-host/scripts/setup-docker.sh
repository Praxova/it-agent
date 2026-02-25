#!/bin/bash
# ============================================================================
# setup-docker.sh — Install Docker CE from official Docker repository
# ============================================================================
# Why the official repo and not Ubuntu's docker.io package?
# - Ubuntu's snap-based docker.io lags 6-12 months behind
# - Snap has quirks with volume mounts and GPU passthrough
# - Official Docker CE gets security patches faster
# - Compose is a plugin (docker compose) not a separate binary
# ============================================================================

set -euo pipefail
echo "=== Installing Docker CE ==="

# --- Add Docker's official GPG key ---
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc

# --- Add Docker apt repository ---
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  tee /etc/apt/sources.list.d/docker.list > /dev/null

# --- Install Docker CE + Compose plugin ---
apt-get update -y
apt-get install -y \
  docker-ce \
  docker-ce-cli \
  containerd.io \
  docker-buildx-plugin \
  docker-compose-plugin

# --- Enable Docker to start on boot ---
systemctl enable docker
systemctl enable containerd

# --- Add default user to docker group (no sudo needed at runtime) ---
usermod -aG docker packer

# --- Verify installation ---
docker --version
docker compose version

# --- Create /opt/praxova directory structure for app deployment ---
# Tofu will populate these with docker-compose.yml and .env files
mkdir -p /opt/praxova/{ollama,admin-portal,it-agent}
mkdir -p /opt/praxova/data/{ollama-models,portal-data,agent-data}

echo "=== Docker CE installation complete ==="
