# Lucid IT Agent - Claude Project Prompt

## Project Overview

**Lucid IT Agent** is an open-source AI-powered IT helpdesk automation system that monitors ServiceNow (and eventually other ITSM) queues and autonomously resolves common IT issues like password resets, AD group membership changes, and file permission requests.

**Business Model**: Red Hat-style open source
- Apache 2.0 license (open source core)
- Revenue: $5k setup + $20k/year support per agent
- Target: Automate 50% of Level 1 IT tickets
- 1 agent can handle ~5000 users

**Author**: Alton Lord  
**Domain**: lucidsoftware.ai (purchased, LLC not formed yet)

---

## Technology Stack (Decisions Made)

### LLM: Ollama + Llama 3.1 8B
- **Why Ollama**: OpenAI-compatible API, simple model management, great for customer deployment
- **Why Llama 3.1**: Native tool calling support, fits in 16GB VRAM (RTX 5080)
- **Abstraction**: Driver pattern allows swapping to OpenAI/Anthropic for cloud deployments

### Framework: Griptape 1.9.0
- Python AI agent framework with Structures (Agent, Pipeline, Workflow)
- Native Ollama support via OllamaPromptDriver
- Tool calling, Rulesets, and MCP support

### Tool Server Architecture
- **MVP (Sprints 1-5)**: Python with ldap3, pyad for AD operations
- **Production (Sprint 6+)**: Refactor to C#/.NET REST API
  - Better Windows integration (System.DirectoryServices)
  - Can run as Windows Service
  - Compiled code for security
- **Pattern**: HTTP API abstraction - agent doesn't know/care about backend language

### Project Location
```
/home/alton/Documents/lucid-it-agent/
```

---

## Current State (End of Session 1)

### ✅ Completed (Sprint 0)
1. **Project structure** - Full directory layout created
2. **Documentation** - README, ARCHITECTURE.md, CLAUDE_CONTEXT.md, SPRINT_BACKLOG.md
3. **Python environment** - venv with Griptape 1.9.0 installed
4. **Ollama integration** - Llama 3.1 8B running locally
5. **Integration tests passing (5/5)**:
   - Import verification
   - Ollama connection
   - Simple agent
   - Agent with tool (CalculatorTool)
   - Agent with ruleset (identifies as "Lucid")
6. **DC setup script** - `env-setup/dc/Setup-TestEnvironment.ps1` ready to run
7. **Config templates** - agent.yaml, servicenow.yaml, tools.yaml examples

### ⏳ Remaining Sprint 0 Tasks
1. **ServiceNow PDI setup** - Sign up at developer.servicenow.com, create Personal Developer Instance
2. **Run DC script** - Execute Setup-TestEnvironment.ps1 on a Windows Domain Controller
3. **Basic logging** - Currently using Griptape defaults

### 🎯 Next Sprint (Sprint 1: ServiceNow Connector)
- REST client for ServiceNow API
- Ticket model (Pydantic)
- Queue polling logic
- BaseConnector interface

---

## Key Files Reference

### Documentation
- `README.md` - Project overview, quick start
- `docs/ARCHITECTURE.md` - Full system design, deployment options
- `docs/SPRINT_BACKLOG.md` - All sprints with task checklists
- `CLAUDE_CONTEXT.md` - Technical context for AI assistants (import patterns, ADRs)

### Agent Code
- `agent/pyproject.toml` - Dependencies, build config
- `agent/src/agent/` - Main agent code (mostly scaffolds currently)
- `agent/scripts/test_ollama_integration.py` - Integration test suite

### Configuration Templates
- `config/agent.yaml.example` - LLM settings, behavior rules, security deny lists
- `config/servicenow.yaml.example` - ServiceNow connection, queue config
- `config/tools.yaml.example` - AD/LDAP settings, password policy, permissions

### Environment Setup
- `env-setup/dc/Setup-TestEnvironment.ps1` - Creates test AD users/groups/shares
- `env-setup/servicenow/` - PDI bootstrap scripts (to be created)

---

## Working With This Project

### Activate Environment
```bash
cd /home/alton/Documents/lucid-it-agent
source .venv/bin/activate
```

### Run Tests
```bash
cd agent
python scripts/test_ollama_integration.py
```

### Verify Ollama
```bash
ollama list  # Should show llama3.1:latest
ollama run llama3.1 "Hello"  # Quick test
```

### Git Workflow
```bash
git status
git add .
git commit -m "Description of changes"
```

---

## Architecture Decisions (ADRs)

### ADR-001: Local LLM via Ollama
Ollama provides OpenAI-compatible API with simple model management. Slight overhead vs raw llama.cpp is worth the developer experience.

### ADR-002: Hybrid Tool Server
Start with Python for rapid development, migrate to C# for production Windows integration. HTTP API abstraction makes this transparent to the agent.

### ADR-003: Queue Connector Pattern
Abstract connector interface allows supporting multiple ITSM systems. ServiceNow first (most common), then Jira Service Management, Zendesk.

### ADR-004: Griptape Workflow Structure
Main Workflow orchestrates parallel Pipelines:
- Ingestion Pipeline (fetch ticket, parse, load user context)
- Planning Pipeline (classify, check capabilities, generate plan)
- Execution Pipeline (execute, validate, communicate, close)

---

## Griptape 1.9.0 Import Patterns

```python
# Structures
from griptape.structures import Agent, Pipeline, Workflow

# Drivers (note nested path)
from griptape.drivers.prompt.ollama import OllamaPromptDriver

# Tasks
from griptape.tasks import PromptTask, ToolkitTask

# Rules
from griptape.rules import Rule, Ruleset

# Tools
from griptape.tools import BaseTool, CalculatorTool

# Artifacts
from griptape.artifacts import TextArtifact, ErrorArtifact
```

---

## Session Continuation Checklist

When resuming work:

1. **Read SPRINT_BACKLOG.md** - Check current sprint status
2. **Read CLAUDE_CONTEXT.md** - Technical patterns and decisions
3. **Activate venv** - `source .venv/bin/activate`
4. **Run tests** - Verify environment still works
5. **Check git status** - See any uncommitted changes

---

## Notes for Claude

- Alton prefers Python or C++ depending on application
- He uses "minimal viable learning" - hands-on first, then tutorials
- Has 20+ years Linux experience, extensive Unreal Engine background
- Currently on RTX 5080 (16GB VRAM), Ryzen 9 7950X, 128GB RAM, Ubuntu 22.04
- Working from off-grid farm in West Virginia
- 3-month intensive training program: QuantumBI (50hr/wk), robotics (20hr/wk), ACFT prep
- Ask clarifying questions as needed - he appreciates learning opportunities
