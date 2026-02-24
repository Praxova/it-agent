# =============================================================================
# lucid-it-agent — Build & Deploy Pipeline
# =============================================================================
# Usage:
#   make              → show this help
#   make build        → build all container images + tarballs
#   make deploy       → build + deploy to VM 110
#   make deploy-only  → deploy without rebuilding (use existing tarballs)
#   make full         → full sequence from scratch (build → deploy → pull model)
#
# Override variables on the command line:
#   make deploy TAG=v1.2.0
#   make deploy HOST=packer@10.0.0.52
#   make deploy ENV_FILE=/path/to/other.env
# =============================================================================

# ── Project root (this Makefile lives here) ──────────────────────────────────
PROJECT_DIR := $(shell pwd)

# ── Remote host ───────────────────────────────────────────────────────────────
HOST     := packer@10.0.0.51
HOST_IP  := 10.0.0.51

# ── Image tag — auto-derived from git, overridable ───────────────────────────
TAG := $(shell git describe --tags --always 2>/dev/null || echo "dev")

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPTS_DIR   := $(PROJECT_DIR)/scripts
ARTIFACTS_DIR := $(PROJECT_DIR)/build/artifacts
ENV_FILE      := $(PROJECT_DIR)/.env
ENV_EXAMPLE   := $(PROJECT_DIR)/.env.example

# ── Required .env keys (checked before every deploy) ─────────────────────────
REQUIRED_ENV_KEYS :=

# ── Default LLM model ────────────────────────────────────────────────────────
LLM_MODEL := llama3.1

# ── Colour helpers ────────────────────────────────────────────────────────────
RESET  := \033[0m
BOLD   := \033[1m
GREEN  := \033[32m
YELLOW := \033[33m
RED    := \033[31m
CYAN   := \033[36m

# =============================================================================
# Help (default target)
# =============================================================================
.DEFAULT_GOAL := help

.PHONY: help
help:
	@printf "$(BOLD)lucid-it-agent pipeline$(RESET)  TAG=$(TAG)  HOST=$(HOST)\n\n"
	@printf "$(BOLD)CORE TARGETS$(RESET)\n"
	@printf "  $(GREEN)make build$(RESET)               Build all images + save tarballs\n"
	@printf "  $(GREEN)make build-skip-ollama$(RESET)   Build but skip the ~3GB ollama tarball\n"
	@printf "  $(GREEN)make build-all$(RESET)            Build containers + tool server\n"
	@printf "  $(GREEN)make build-toolserver$(RESET)    Build tool server on Windows build VM\n"
	@printf "  $(GREEN)make build-toolserver-msi$(RESET)  Build tool server + MSI + Setup EXE\n"
	@printf "  $(GREEN)make deploy$(RESET)              build then deploy to VM 110\n"
	@printf "  $(GREEN)make deploy-only$(RESET)         Deploy existing tarballs (no rebuild)\n"
	@printf "  $(GREEN)make deploy-skip-ollama$(RESET)  Deploy without reloading ollama image\n"
	@printf "  $(GREEN)make ship$(RESET)                Build + deploy, skip ollama (daily workflow)\n"
	@printf "  $(GREEN)make full$(RESET)                Full from-scratch sequence\n"
	@printf "\n$(BOLD)MODEL$(RESET)\n"
	@printf "  $(GREEN)make pull-model$(RESET)          Pull $(LLM_MODEL) inside ollama on remote\n"
	@printf "\n$(BOLD)INFRA CHECKS$(RESET)\n"
	@printf "  $(GREEN)make verify-gpu$(RESET)          Check nvidia-smi + CUDA container on remote\n"
	@printf "  $(GREEN)make ping$(RESET)                SSH connectivity check\n"
	@printf "  $(GREEN)make status$(RESET)              docker compose ps on remote\n"
	@printf "  $(GREEN)make logs$(RESET)                Tail last 50 lines from all services\n"
	@printf "  $(GREEN)make logs-portal$(RESET)         Tail admin-portal logs\n"
	@printf "  $(GREEN)make logs-agent$(RESET)          Tail agent-helpdesk-01 logs\n"
	@printf "\n$(BOLD)ENV$(RESET)\n"
	@printf "  $(GREEN)make env-check$(RESET)           Validate .env has all required keys\n"
	@printf "  $(GREEN)make env-init$(RESET)            Copy .env.example to .env if missing\n"
	@printf "\n$(BOLD)LOCAL STACK$(RESET)\n"
	@printf "  $(GREEN)make local-up$(RESET)            Start local docker compose stack\n"
	@printf "  $(GREEN)make local-down$(RESET)          Stop local stack\n"
	@printf "  $(GREEN)make local-restart$(RESET)       Restart agent-helpdesk-01 locally\n"
	@printf "  $(GREEN)make local-build$(RESET)         docker compose build\n"
	@printf "  $(GREEN)make local-rebuild$(RESET)       docker compose build --no-cache\n"
	@printf "  $(GREEN)make local-status$(RESET)        docker compose ps (local)\n"
	@printf "  $(GREEN)make local-logs$(RESET)          Follow agent logs (local)\n"
	@printf "\n$(BOLD)SERVICENOW TESTING$(RESET)\n"
	@printf "  $(GREEN)make test-tickets$(RESET)        Create test tickets in ServiceNow\n"
	@printf "  $(GREEN)make test-cleanup$(RESET)        Remove test tickets\n"
	@printf "  $(GREEN)make test-list$(RESET)           List test tickets\n"
	@printf "\n$(BOLD)CLEANUP$(RESET)\n"
	@printf "  $(GREEN)make clean$(RESET)               Remove local build/artifacts/\n"
	@printf "  $(GREEN)make clean-remote$(RESET)        Remove tarballs from /opt/praxova on remote\n"
	@printf "\n$(BOLD)VARIABLES$(RESET)  (override on command line)\n"
	@printf "  TAG=$(TAG)\n"
	@printf "  HOST=$(HOST)\n"
	@printf "  ENV_FILE=$(ENV_FILE)\n"
	@printf "  LLM_MODEL=$(LLM_MODEL)\n"

# =============================================================================
# Environment management
# =============================================================================
.PHONY: env-init
env-init:
	@printf "$(CYAN)-- Initialising .env --$(RESET)\n"
	@if [ -f "$(ENV_FILE)" ]; then \
		printf "$(YELLOW)WARNING: .env already exists -- not overwriting.\n  Delete it first if you want a fresh copy.$(RESET)\n"; \
	elif [ ! -f "$(ENV_EXAMPLE)" ]; then \
		printf "$(RED)ERROR: .env.example not found at $(ENV_EXAMPLE)\n  Commit a .env.example to the repo first.$(RESET)\n"; \
		exit 1; \
	else \
		cp $(ENV_EXAMPLE) $(ENV_FILE); \
		printf "$(GREEN)OK: .env created from .env.example\n  Fill in real values before running make deploy.$(RESET)\n"; \
	fi

.PHONY: env-check
env-check:
	@printf "$(CYAN)-- Validating .env --$(RESET)\n"
	@if [ ! -f "$(ENV_FILE)" ]; then \
		printf "$(RED)ERROR: .env not found -- run: make env-init$(RESET)\n"; \
		exit 1; \
	fi; \
	MISSING=""; \
	for KEY in $(REQUIRED_ENV_KEYS); do \
		VALUE=$$(grep -E "^$${KEY}=" "$(ENV_FILE)" | cut -d= -f2- | tr -d '"' | tr -d "'"); \
		if [ -z "$$VALUE" ] || echo "$$VALUE" | grep -qi "changeme"; then \
			MISSING="$$MISSING $$KEY"; \
		fi; \
	done; \
	if [ -n "$$MISSING" ]; then \
		printf "$(RED)ERROR: Missing or placeholder values in .env:$(RESET)\n"; \
		for K in $$MISSING; do printf "    - $$K\n"; done; \
		exit 1; \
	fi; \
	printf "$(GREEN)OK: .env looks good$(RESET)\n"

# =============================================================================
# Build targets
# =============================================================================
.PHONY: build
build:
	@printf "\n$(BOLD)$(CYAN)-- Building container images TAG=$(TAG) --$(RESET)\n"
	@mkdir -p $(ARTIFACTS_DIR)
	$(SCRIPTS_DIR)/build-containers.sh $(TAG)
	@printf "$(GREEN)OK: Build complete -- artifacts in build/artifacts/$(RESET)\n"

.PHONY: build-skip-ollama
build-skip-ollama:
	@printf "\n$(BOLD)$(CYAN)-- Building images (skipping ollama tarball) --$(RESET)\n"
	@mkdir -p $(ARTIFACTS_DIR)
	$(SCRIPTS_DIR)/build-containers.sh --skip-ollama $(TAG)
	@printf "$(GREEN)OK: Build complete$(RESET)\n"

.PHONY: build-toolserver build-toolserver-msi
build-toolserver:
	@printf "\n$(BOLD)$(CYAN)-- Building tool server on Windows build VM --$(RESET)\n"
	$(SCRIPTS_DIR)/build-toolserver.sh
	@printf "$(GREEN)OK: Tool server build complete$(RESET)\n"

build-toolserver-msi:
	@printf "\n$(BOLD)$(CYAN)-- Building tool server + MSI + Setup EXE on Windows build VM --$(RESET)\n"
	$(SCRIPTS_DIR)/build-toolserver.sh --msi
	@printf "$(GREEN)OK: Tool server + MSI + Setup EXE build complete$(RESET)\n"

.PHONY: build-all
build-all: build build-toolserver
	@printf "\n$(BOLD)$(GREEN)OK: All builds complete (containers + tool server)$(RESET)\n"

# =============================================================================
# Internal pre-deploy guard (not meant to be called directly)
# =============================================================================
.PHONY: _pre-deploy
_pre-deploy: env-check ping

# =============================================================================
# Deploy targets
# =============================================================================
.PHONY: deploy
deploy: build _pre-deploy
	@printf "\n$(BOLD)$(CYAN)-- Deploying TAG=$(TAG) to $(HOST) --$(RESET)\n"
	$(SCRIPTS_DIR)/deploy-containers.sh $(HOST) $(TAG) $(ENV_FILE) || \
		printf "$(YELLOW)WARNING: deploy-containers.sh exited non-zero.\n  Health check may have timed out. Run: make status$(RESET)\n"
	@printf "$(GREEN)OK: Deploy step complete$(RESET)\n"

.PHONY: deploy-only
deploy-only: _pre-deploy
	@printf "\n$(BOLD)$(CYAN)-- Deploying existing tarballs TAG=$(TAG) --$(RESET)\n"
	$(SCRIPTS_DIR)/deploy-containers.sh $(HOST) $(TAG) $(ENV_FILE) || \
		printf "$(YELLOW)WARNING: deploy-containers.sh exited non-zero. Run: make status$(RESET)\n"
	@printf "$(GREEN)OK: Deploy step complete$(RESET)\n"

.PHONY: deploy-skip-ollama
deploy-skip-ollama: _pre-deploy
	@printf "\n$(BOLD)$(CYAN)-- Deploying (skipping ollama reload) --$(RESET)\n"
	$(SCRIPTS_DIR)/deploy-containers.sh --skip-ollama $(HOST) $(TAG) $(ENV_FILE) || \
		printf "$(YELLOW)WARNING: deploy-containers.sh exited non-zero. Run: make status$(RESET)\n"
	@printf "$(GREEN)OK: Deploy step complete$(RESET)\n"

# =============================================================================
# Full from-scratch sequence
# =============================================================================
.PHONY: full
full: build _pre-deploy
	@printf "\n$(BOLD)$(CYAN)-- Full from-scratch deploy TAG=$(TAG) --$(RESET)\n"
	$(SCRIPTS_DIR)/deploy-containers.sh $(HOST) $(TAG) $(ENV_FILE) || \
		printf "$(YELLOW)WARNING: Health check timed out -- continuing$(RESET)\n"
	$(MAKE) pull-model
	@printf "\n$(BOLD)$(GREEN)OK: Full deploy complete$(RESET)\n"
	@printf "  Portal:  https://$(HOST_IP):5001\n"
	@printf "  API:     https://$(HOST_IP):5000/api/health/\n"
	@printf "\n  Next steps:\n"
	@printf "    1. Visit portal, API Keys, New Key, Role Agent\n"
	@printf "    2. Add LUCID_API_KEY to .env\n"
	@printf "    3. Run: make deploy-skip-ollama\n"

# =============================================================================
# Model management
# =============================================================================
.PHONY: pull-model
pull-model:
	@printf "\n$(BOLD)$(CYAN)-- Pulling $(LLM_MODEL) on $(HOST) --$(RESET)\n"
	ssh $(HOST) "cd /opt/praxova && docker compose exec ollama ollama pull $(LLM_MODEL)"
	@printf "$(GREEN)OK: Model pull complete$(RESET)\n"

# =============================================================================
# Infra / status checks
# =============================================================================
.PHONY: ping
ping:
	@printf "$(CYAN)Checking SSH connectivity to $(HOST)$(RESET)\n"
	@ssh -o ConnectTimeout=5 -o BatchMode=yes $(HOST) "echo ok" > /dev/null 2>&1 || { \
		printf "$(RED)ERROR: Cannot reach $(HOST) -- is the VM up and SSH key loaded?$(RESET)\n"; \
		exit 1; \
	}
	@printf "$(GREEN)OK: $(HOST) reachable$(RESET)\n"

.PHONY: verify-gpu
verify-gpu:
	@printf "\n$(BOLD)$(CYAN)-- GPU passthrough verification on $(HOST) --$(RESET)\n"
	@printf "$(CYAN)nvidia-smi:$(RESET)\n"
	ssh $(HOST) "nvidia-smi"
	@printf "\n$(CYAN)CUDA container test:$(RESET)\n"
	ssh $(HOST) "docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi"
	@printf "$(GREEN)OK: Expect Titan Xp 12GB / CUDA 12.4 / Driver 580+$(RESET)\n"

.PHONY: status
status:
	@printf "\n$(BOLD)$(CYAN)-- Stack status on $(HOST) --$(RESET)\n"
	ssh $(HOST) "cd /opt/praxova && docker compose ps"

.PHONY: logs
logs:
	ssh $(HOST) "cd /opt/praxova && docker compose logs --tail=50"

.PHONY: logs-portal
logs-portal:
	ssh $(HOST) "cd /opt/praxova && docker compose logs --tail=100 admin-portal"

.PHONY: logs-agent
logs-agent:
	ssh $(HOST) "cd /opt/praxova && docker compose logs --tail=100 agent-helpdesk-01"

# =============================================================================
# Cleanup
# =============================================================================
.PHONY: clean
clean:
	@printf "\n$(BOLD)$(CYAN)-- Cleaning local build artifacts --$(RESET)\n"
	rm -rf $(ARTIFACTS_DIR)
	@printf "$(GREEN)OK: build/artifacts/ removed$(RESET)\n"

.PHONY: clean-remote
clean-remote:
	@printf "\n$(BOLD)$(CYAN)-- Removing tarballs on $(HOST) --$(RESET)\n"
	ssh $(HOST) "rm -f /opt/praxova/*.tar"
	@printf "$(GREEN)OK: Remote tarballs removed$(RESET)\n"

# =============================================================================
# Local docker compose (workstation stack)
# These operate on the local docker compose stack, not VM 110.
# =============================================================================
.PHONY: local-up local-down local-restart local-build local-rebuild local-status local-logs

local-up:
	@printf "\n$(BOLD)$(CYAN)-- Starting local stack --$(RESET)\n"
	docker compose up -d
	@printf "$(GREEN)OK: Local stack started$(RESET)\n"

local-down:
	@printf "\n$(BOLD)$(CYAN)-- Stopping local stack --$(RESET)\n"
	docker compose down
	@printf "$(GREEN)OK: Local stack stopped$(RESET)\n"

local-restart:
	@printf "\n$(BOLD)$(CYAN)-- Restarting agent-helpdesk-01 --$(RESET)\n"
	docker compose restart agent-helpdesk-01
	@printf "$(GREEN)OK: Agent restarted$(RESET)\n"

local-build:
	@printf "\n$(BOLD)$(CYAN)-- Building local images (docker compose) --$(RESET)\n"
	docker compose build
	@printf "$(GREEN)OK: Local build complete$(RESET)\n"

local-rebuild:
	@printf "\n$(BOLD)$(CYAN)-- Rebuilding local images (no cache) --$(RESET)\n"
	docker compose build --no-cache
	@printf "$(GREEN)OK: Local rebuild complete$(RESET)\n"

local-status:
	docker compose ps

local-logs:
	docker compose logs -f agent-helpdesk-01

# =============================================================================
# ServiceNow test ticket management
# =============================================================================
.PHONY: test-tickets test-cleanup test-list

test-tickets:
	@printf "\n$(BOLD)$(CYAN)-- Creating test tickets in ServiceNow --$(RESET)\n"
	python agent/scripts/create_test_tickets.py --all

test-cleanup:
	@printf "\n$(BOLD)$(CYAN)-- Cleaning up test tickets --$(RESET)\n"
	python agent/scripts/create_test_tickets.py --cleanup

test-list:
	python agent/scripts/create_test_tickets.py --list

# =============================================================================
# Convenience: build + deploy, skip ollama (most common daily workflow)
# =============================================================================
.PHONY: ship

ship: build-skip-ollama deploy-skip-ollama
	@printf "\n$(BOLD)$(GREEN)OK: Build + deploy complete (ollama unchanged)$(RESET)\n"
