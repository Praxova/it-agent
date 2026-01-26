# Claude Context - Lucid IT Agent

> **Purpose**: This file provides context for Claude Code and other AI assistants working on this project.
> Keep this file updated as architectural decisions are made.

## Project Overview

**Lucid IT Agent** is an AI-powered IT helpdesk automation agent built on the Griptape framework.
The agent monitors IT ticket queues (ServiceNow), classifies tickets, and autonomously resolves
common Level 1 support issues like password resets, AD group modifications, and file permission changes.

**Business Model**: Open source (Apache 2.0) with revenue from implementation services ($5k) and
annual support contracts ($20k/agent/year).

## Technology Stack

### Pinned Versions (January 2025)

```
griptape>=1.8.0  (currently using 1.9.0)
python>=3.10
ollama (latest)
```

### Griptape Import Patterns (v1.8.x)

**IMPORTANT**: Griptape 1.x changed import paths significantly from 0.x. Use these patterns:

```python
# Prompt Drivers (note the nested path)
from griptape.drivers.prompt.ollama import OllamaPromptDriver
from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
from griptape.drivers.prompt.anthropic import AnthropicPromptDriver

# Structures
from griptape.structures import Agent, Pipeline, Workflow

# Tasks
from griptape.tasks import PromptTask, ToolTask, ToolkitTask

# Tools - Base class for custom tools
from griptape.tools import BaseTool
from griptape.tools import CalculatorTool, WebScraperTool  # Built-in tools

# Rules and Rulesets
from griptape.rules import Rule, Ruleset

# Artifacts
from griptape.artifacts import TextArtifact, ListArtifact, ErrorArtifact

# Configuration
from griptape.configs import Defaults

# Memory
from griptape.memory.structure import ConversationMemory
```

### DO NOT USE (Old 0.x patterns)

```python
# WRONG - these are old import paths
from griptape.drivers import OllamaPromptDriver  # Missing nested path
from griptape.structures import Agent  # This one is actually correct
from griptape.tools import Tool  # Use BaseTool instead
```

## Architecture Decisions

### ADR-001: Local LLM via Ollama
- **Decision**: Use Ollama with Llama 3.1 8B as the default local LLM
- **Rationale**: Native tool calling support, OpenAI-compatible API, easy deployment
- **Trade-off**: Slightly lower performance than raw llama.cpp, but much better DX

### ADR-002: Tool Server Architecture
- **Decision**: Start with Python tools, plan migration to C#/.NET
- **Rationale**: Faster MVP with Python; C# provides better Windows integration for production
- **Migration Path**: HTTP API abstraction allows transparent backend swap

### ADR-003: Queue Connector Pattern
- **Decision**: ServiceNow first, abstract interface for future connectors
- **Rationale**: ServiceNow is most common enterprise ITSM; abstraction allows Jira, Zendesk, etc.

### ADR-004: Agent Structure
- **Decision**: Use Griptape Workflow with nested Pipelines
- **Rationale**: Allows parallel context gathering while maintaining sequential execution phases

## Directory Structure

```
lucid-it-agent/
├── agent/                  # Griptape agent (Python)
│   └── src/agent/
│       ├── structures/     # Pipelines, workflows
│       ├── tools/          # Griptape tool wrappers (call tool-server)
│       ├── rulesets/       # Agent behavior rules
│       └── config/         # Runtime configuration
├── connectors/             # Queue connectors (ServiceNow, email, etc.)
├── tool-server/            # Actual tool implementations
│   ├── python/             # MVP version
│   └── dotnet/             # Production version (future)
├── env-setup/              # Development environment bootstrapping
│   ├── servicenow/         # PDI setup scripts
│   └── dc/                 # Domain Controller mock data
├── config/                 # Configuration file examples
└── docker/                 # Container definitions
```

## Coding Standards

### Python
- Use `pyproject.toml` for dependencies (not requirements.txt)
- Type hints required for all public functions
- Docstrings in Google format
- Black for formatting, Ruff for linting

### Naming Conventions
- Tool classes: `<Action><Target>Tool` (e.g., `ResetPasswordTool`, `ModifyGroupTool`)
- Structures: `<Phase>Pipeline` or `<Purpose>Workflow` (e.g., `TicketIngestionPipeline`)
- Rulesets: `<context>_rules.py` (e.g., `security_rules.py`, `communication_rules.py`)

### Configuration
- All secrets via environment variables
- Runtime config in YAML files
- Use `pydantic` for config validation

## Current Sprint Focus

**Sprint 0 - Foundation** (Current - ~80% Complete)
- [x] Project structure
- [x] Local development environment (Ollama + Llama 3.1 8B)
- [x] Basic agent scaffold with tool calling test (5/5 tests passing)
- [x] DC mock environment PowerShell script (Setup-TestEnvironment.ps1)
- [ ] ServiceNow PDI setup (next task)
- [ ] Run DC script on actual Domain Controller

**Sprint 1 - ServiceNow Connector** (Next)
- ServiceNow REST client
- Ticket model (Pydantic)
- Queue polling logic
- BaseConnector interface

## ServiceNow API Reference

PDI Base URL: `https://<instance>.service-now.com`

Key endpoints:
- `GET /api/now/table/incident` - List incidents
- `GET /api/now/table/incident/{sys_id}` - Get single incident
- `PATCH /api/now/table/incident/{sys_id}` - Update incident
- `POST /api/now/table/incident` - Create incident

Authentication: Basic auth or OAuth 2.0

## Testing Approach

1. **Unit tests**: Mock external services (ServiceNow, AD)
2. **Integration tests**: Run against PDI and test DC
3. **Agent tests**: Griptape provides structure testing utilities

## Common Gotchas

1. **Ollama tool calling**: Requires model that supports tools (llama3.1, mistral, etc.)
2. **Griptape logging**: Set `Defaults.logging_config.logger_name` for custom logging
3. **ServiceNow rate limits**: PDI has strict rate limits; implement backoff
4. **AD connection**: Use `ldap3` library with TLS; test connectivity before operations

## Integration Test Suite

Run the integration tests to verify your environment:

```bash
cd /home/alton/Documents/lucid-it-agent
source .venv/bin/activate
cd agent
python scripts/test_ollama_integration.py
```

**Tests included**:
1. Import verification (structures, drivers, tasks, rules, tools, artifacts)
2. Ollama connection (server running, llama3.1 model available)
3. Simple agent (basic prompt without tools)
4. Agent with tool (CalculatorTool - verifies tool calling works)
5. Agent with ruleset (agent identifies as "Lucid")

All 5 tests should pass. Response times with warm model:
- Simple prompt: ~0.15s
- Tool call: ~0.50s
- Ruleset: ~0.30s

## References

- [Griptape Docs](https://docs.griptape.ai/latest/)
- [Griptape GitHub](https://github.com/griptape-ai/griptape)
- [Ollama](https://ollama.com/)
- [ServiceNow REST API](https://developer.servicenow.com/dev.do#!/reference/api/latest/rest/)
- [ldap3 Documentation](https://ldap3.readthedocs.io/)
