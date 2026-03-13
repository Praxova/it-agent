# Praxova IT Agent

> ⚠️ **Beta Release** — This project is functional but under active development. APIs and configuration formats may change before v1.0 stable. Use in production at your own risk.

An AI-powered IT helpdesk automation platform that autonomously resolves Level 1 support tickets with human oversight built in.

## Overview

Praxova IT Agent monitors your IT ticket queue, classifies incoming tickets using LLM-powered analysis, and automatically resolves common IT requests — while keeping humans in the loop for sensitive actions.

**What it automates today:**

- 🔐 Password resets
- 👥 Active Directory group membership changes
- 🔓 Account unlocks
- 📁 NTFS file share permissions
- 💾 Approved software deployments

Built on the [Griptape](https://github.com/griptape-ai/griptape) agent framework, with a Blazor Server admin portal, a Windows-native .NET 8 tool server, and support for local or cloud LLM deployment.

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│   ServiceNow    │────▶│  Praxova Agent   │────▶│   Admin Portal   │
│   (Tickets)     │     │  (Griptape/Py)   │     │  (Blazor Server) │
└─────────────────┘     └──────────────────┘     └──────────────────┘
                               │                         │
                               ▼                         ▼
                        ┌──────────────┐        ┌──────────────────┐
                        │  LLM Backend │        │   Tool Server    │
                        │  (Ollama or  │        │  (.NET 8, Win,   │
                        │   Cloud API) │        │   domain-joined) │
                        └──────────────┘        └──────────────────┘
```

**Components:**

- **Agent** (`agent/`) — Python/Griptape agent that polls the ticket queue, classifies tickets, and orchestrates resolution workflows
- **Admin Portal** (`admin/`) — Blazor Server web UI for configuration, visual workflow design, approval queue management, and audit log review
- **Tool Server** (`tool-server/dotnet/`) — Windows .NET 8 service that executes privileged AD and file system operations via mTLS
- **Connectors** (`connectors/`) — Queue integrations (ServiceNow today; Jira, email, and Teams planned)

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for full system design details.

## Key Features

**Security-first by design:**
- Internal PKI — auto-generated RSA 4096 root CA; all inter-component traffic uses mTLS with certs issued from this CA (no external CA required)
- Envelope encryption (AES-256-GCM + Argon2id) for all stored credentials; a raw database dump exposes nothing
- Token-based authentication for all agent↔portal communication

**Human oversight built in:**
- Human approval gates — workflows pause before sensitive actions for operator review
- Confidence-based routing — low-confidence classifications automatically route to human review
- Full audit trail — every agent action is logged and queryable from the portal

**Flexible LLM support:**
- Local deployment via llama.cpp server with native TLS — entirely on-premises, no data leaves your network
- Cloud providers: OpenAI, Anthropic, Azure OpenAI, AWS Bedrock — swap without code changes

**Composable workflows:**
- Visual workflow designer in the admin portal
- Pluggable sub-workflows and triggers
- Capability routing — agent requests capabilities by name; portal resolves to the right tool server at runtime


## Quick Start

### Prerequisites

- Docker and Docker Compose
- Python 3.10+ (for agent development)
- .NET 8 SDK (for tool server development)
- Access to a ServiceNow instance (or PDI for development)
- Windows domain-joined machine for the tool server (for AD operations)
- Ollama (for local LLM) or API keys for a cloud provider

### Installation

```bash
# Clone the repository
git clone https://github.com/Praxova/it-agent.git
cd it-agent

# Copy and configure environment
cp .env.example .env
# Edit .env with your settings

# Copy and configure component settings
cp config/agent.yaml.example config/agent.yaml
cp config/servicenow.yaml.example config/servicenow.yaml
cp config/tools.yaml.example config/tools.yaml

# Start the stack
docker compose up -d
```

### Tool Server (Windows)

The tool server runs as a Windows Service on a domain-joined machine. Install using the provided MSI from the [Releases](https://github.com/Praxova/it-agent/releases) page, then provision certificates:

```powershell
# On the domain-joined tool server machine
.\scripts\provision-toolserver-certs.ps1
```

See [tool-server/dotnet/DEPLOYMENT.md](tool-server/dotnet/DEPLOYMENT.md) for full deployment instructions.


### Development Environment

```bash
# Install Python agent dependencies
cd agent
pip install -e ".[dev]"

# Set up ServiceNow PDI
python env-setup/servicenow/setup_pdi.py

# Set up test Domain Controller (PowerShell, run on DC)
./env-setup/dc/Setup-TestEnvironment.ps1

# Run tests
pytest
```

For local infrastructure provisioning (Proxmox/Packer/OpenTofu):

```bash
cd infra/opentofu
cp terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars
tofu apply
```

See [docs/DEV-QUICKREF.md](docs/DEV-QUICKREF.md) for the full development quick reference.

## Project Structure

```
it-agent/
├── agent/              # Python/Griptape agent
├── admin/              # Blazor Server admin portal
├── connectors/         # Queue connectors (ServiceNow, etc.)
├── tool-server/        # Tool implementations (.NET and Python)
├── env-setup/          # Dev environment setup scripts
├── infra/              # Proxmox/Packer/OpenTofu IaC
├── config/             # Configuration examples
├── docs/               # Architecture docs, ADRs, runbooks
└── scripts/            # Build, deploy, and cert provisioning scripts
```

## Documentation

Full documentation is in progress and will be published with the v1.0 stable release. In the meantime, see [ROADMAP.md](ROADMAP.md) for the project direction and current status.


## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full roadmap. Current status:

- ✅ **v1.0** — Core platform: admin portal, composable workflows, human approval gates, mTLS PKI, secrets encryption, AD tools, ServiceNow connector
- 🔧 **v1.1** — Hardening & polish: portal auth hardening, reliability fixes, dynamic classification
- 📋 **v1.2** — Connector expansion: Jira, email, Teams, Zendesk
- 📋 **v2.0** — Enterprise platform: external CA/vault integration, multi-agent, RBAC, compliance exports

## Contributing

Bug reports and feature requests: [GitHub Issues](https://github.com/Praxova/it-agent/issues)

Contributions welcome. Please open an issue before submitting a PR for significant changes so we can discuss the approach first.

## License

Apache License 2.0 — See [LICENSE](LICENSE) for details.

## Support

- **Documentation**: [docs/](docs/)
- **Issues**: [GitHub Issues](https://github.com/Praxova/it-agent/issues)
- **Commercial Support**: [support@praxova.ai](mailto:support@praxova.ai)

---

*Praxova IT Agent is open source software. Commercial support and implementation services available from [Praxova](https://praxova.ai).*
