#!/bin/bash
# ============================================================================
# setup-nvidia.sh — Install NVIDIA driver + Container Toolkit
# ============================================================================
# Two Titan XP (Pascal GP102) cards will be passed through to this VM.
#
# During the Packer build there's NO GPU present — the driver installs,
# DKMS builds kernel modules, but nothing loads. That's expected.
# On first boot with GPU passthrough, the modules load automatically.
#
# Components:
#   1. nvidia-driver-550 — production branch, supports Pascal+
#   2. nvidia-container-toolkit — lets Docker containers access the GPU
#      This installs nvidia-container-runtime and configures Docker's
#      daemon.json to use it as the default runtime
# ============================================================================

set -euo pipefail
echo "=== Installing NVIDIA driver and Container Toolkit ==="

# --- Install NVIDIA driver from Ubuntu's official repo ---
# Using the packaged driver avoids manual .run file headaches
# and integrates with DKMS for automatic kernel module rebuilds
apt-get update -y
apt-get install -y nvidia-driver-550 nvidia-utils-550

echo "NVIDIA driver 550 packages installed (modules will load when GPU is present)"

# --- Add NVIDIA Container Toolkit repository ---
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | \
  gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg

curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
  sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
  tee /etc/apt/sources.list.d/nvidia-container-toolkit.list > /dev/null

# --- Install the toolkit ---
apt-get update -y
apt-get install -y nvidia-container-toolkit

# --- Configure Docker to use NVIDIA runtime ---
# This modifies /etc/docker/daemon.json to register the nvidia runtime
nvidia-ctk runtime configure --runtime=docker

# --- Restart Docker to pick up the new runtime ---
systemctl restart docker

# --- Verify the runtime is registered ---
echo "Docker runtime configuration:"
cat /etc/docker/daemon.json

echo "=== NVIDIA Container Toolkit installation complete ==="
echo "NOTE: nvidia-smi will only work after GPU passthrough is configured"
