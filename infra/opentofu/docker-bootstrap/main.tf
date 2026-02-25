terraform {
  required_providers {
    null = {
      source  = "hashicorp/null"
      version = "~> 3.0"
    }
  }
  required_version = ">= 1.6"
}

# No cloud provider needed — this config manages post-deploy bootstrapping
# on an already-running Docker host via SSH.
#
# The Docker host VM (VM 110) must already be:
#   - Cloned from template 9010 (manual step)
#   - Running at the configured IP
#   - Containers deployed via scripts/deploy-containers.sh
#
# This config handles everything after that:
#   - Ollama model pull
#   - Agent entity creation in the portal
#   - API key generation and injection into .env
#   - Agent container restart
