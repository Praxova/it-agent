# Lucid IT Agent

An AI-powered IT helpdesk automation agent that autonomously resolves Level 1 support tickets.

## Overview

Lucid IT Agent monitors your IT ticket queue, classifies incoming tickets, and automatically resolves common issues like:

- 🔐 Password resets
- 👥 Active Directory group membership changes
- 📁 File and folder permission modifications
- *(More tools coming soon)*

Built on the [Griptape](https://github.com/griptape-ai/griptape) framework with support for local LLM deployment via [Ollama](https://ollama.com/).

## Features

- **Queue Integration**: Connects to ServiceNow (more connectors planned)
- **Local LLM Support**: Run entirely on-premises with Ollama + Llama 3.1
- **Cloud LLM Support**: Optional integration with OpenAI, Anthropic, Azure
- **Extensible Tools**: Add custom tools for your environment
- **Audit Trail**: Full logging of all agent actions
- **Human Escalation**: Automatic escalation for tickets outside agent capability

## Quick Start

### Prerequisites

- Python 3.10+
- Ollama (for local LLM)
- Access to ServiceNow instance (or PDI for development)
- Domain Controller with appropriate service account (for AD tools)

### Installation

```bash
# Clone the repository
git clone https://github.com/your-org/lucid-it-agent.git
cd lucid-it-agent

# Install the agent
cd agent
pip install -e ".[dev]"

# Install Ollama and pull a model
# See https://ollama.com for installation
ollama pull llama3.1

# Copy and configure settings
cp config/agent.yaml.example config/agent.yaml
# Edit config/agent.yaml with your settings
```

### Running the Agent

```bash
# Start Ollama (if not running as service)
ollama serve

# Run the agent
python -m agent.main
```

## Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   ServiceNow    │────▶│   Lucid Agent   │────▶│   Tool Server   │
│   (Tickets)     │     │   (Griptape)    │     │   (AD/NTFS)     │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                               │
                               ▼
                        ┌─────────────────┐
                        │   Ollama/LLM    │
                        │  (Llama 3.1)    │
                        └─────────────────┘
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed system design.

## Configuration

Configuration is managed via YAML files in the `config/` directory:

- `agent.yaml` - Core agent settings (LLM, logging, behavior)
- `servicenow.yaml` - ServiceNow connection and queue settings
- `tools.yaml` - Tool server connection and enabled tools

See the `.example` files for documentation of all options.

## Development

### Setting Up Development Environment

```bash
# Set up ServiceNow PDI (Personal Developer Instance)
python env-setup/servicenow/setup_pdi.py

# Set up test Domain Controller (PowerShell, run on DC)
./env-setup/dc/Setup-TestEnvironment.ps1
```

### Running Tests

```bash
cd agent
pytest
```

### Project Structure

```
lucid-it-agent/
├── agent/              # Griptape agent
├── connectors/         # Queue connectors (ServiceNow, etc.)
├── tool-server/        # Tool implementations
├── env-setup/          # Development environment scripts
├── config/             # Configuration examples
└── docs/               # Documentation
```

## Adding Custom Tools

See [docs/TOOL_DEVELOPMENT.md](docs/TOOL_DEVELOPMENT.md) for guide on creating custom tools.

## License

Apache License 2.0 - See [LICENSE](LICENSE) for details.

## Support

- **Documentation**: [docs/](docs/)
- **Issues**: GitHub Issues
- **Commercial Support**: Contact [support@lucidsoftware.ai](mailto:support@lucidsoftware.ai)

## Roadmap

- [x] Core agent framework
- [x] ServiceNow connector
- [x] Password reset tool
- [x] AD group management tool
- [ ] File permission tool
- [ ] Microsoft Teams integration
- [ ] Jira connector
- [ ] Web UI for monitoring
- [ ] Multi-agent support

---

*Lucid IT Agent is open source software. Commercial support and implementation services available.*
