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

### Agent (Python/Linux)

```
griptape>=1.8.0  (currently using 1.9.0)
python>=3.10
ollama (latest) with llama3.1
```

### Tool Server (C#/Windows) - **PRIMARY**

```
.NET 8 (LTS)
ASP.NET Core Minimal APIs
System.DirectoryServices.AccountManagement
System.Security.AccessControl
Windows Server 2022 Container
```

### Tool Server (Python/Linux) - **DEPRECATED**

The Python tool server in `tool-server/python/` was an MVP implementation that hit limitations
with cross-platform AD operations (LDAPS requirements, WinRM complexity). It is being replaced
by the .NET version. See ADR-005.

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

### Supported LLM Providers (via Griptape)

Griptape provides driver abstractions for multiple LLM providers. Install extras as needed:

| Provider | Package Extra | Import Path |
|----------|---------------|-------------|
| Ollama (local) | `drivers-prompt-ollama` | `griptape.drivers.prompt.ollama.OllamaPromptDriver` |
| OpenAI | `drivers-prompt-openai` | `griptape.drivers.prompt.openai.OpenAiChatPromptDriver` |
| Anthropic | `drivers-prompt-anthropic` | `griptape.drivers.prompt.anthropic.AnthropicPromptDriver` |
| Azure OpenAI | `drivers-prompt-openai` | `griptape.drivers.prompt.openai.AzureOpenAiChatPromptDriver` |
| AWS Bedrock | `drivers-prompt-amazon-bedrock` | `griptape.drivers.prompt.amazon_bedrock.AmazonBedrockPromptDriver` |
| Google | `drivers-prompt-google` | `griptape.drivers.prompt.google.GooglePromptDriver` |

**Driver Factory Pattern** (agent/src/agent/drivers/factory.py):
```python
def create_prompt_driver(service_account: dict) -> BasePromptDriver:
    """Create Griptape driver from ServiceAccount configuration."""
    provider_type = service_account["provider_type"]
    config = service_account["provider_config"]
    
    if provider_type == "llm-ollama":
        return OllamaPromptDriver(model=config["model"], host=config["endpoint"])
    elif provider_type == "llm-openai":
        return OpenAiChatPromptDriver(model=config["model"], api_key=credentials["api_key"])
    # ... etc
```

## Architecture Decisions

### ADR-001: Local LLM via Ollama
- **Decision**: Use Ollama with Llama 3.1 8B as the default local LLM
- **Rationale**: Native tool calling support, OpenAI-compatible API, easy deployment
- **Trade-off**: Slightly lower performance than raw llama.cpp, but much better DX

### ADR-002: Tool Server Architecture (Superseded by ADR-005)
- **Decision**: Start with Python tools, plan migration to C#/.NET
- **Status**: Python MVP complete; migrating to .NET per ADR-005

### ADR-003: Queue Connector Pattern
- **Decision**: ServiceNow first, abstract interface for future connectors
- **Rationale**: ServiceNow is most common enterprise ITSM; abstraction allows Jira, Zendesk, etc.

### ADR-004: Agent Structure
- **Decision**: Use Griptape Workflow with nested Pipelines
- **Rationale**: Allows parallel context gathering while maintaining sequential execution phases

### ADR-005: Windows-Native Tool Server (January 2025)
- **Decision**: Migrate tool server to .NET 8 in Windows containers
- **Rationale**: Native AD operations (password reset requires Kerberos), gMSA support, native NTFS ACLs
- **See**: `docs/adr/ADR-005-windows-native-tool-server.md`

### ADR-006: ServiceAccount as Unified Provider Pattern (January 2025)
- **Decision**: All external connections (AD, ServiceNow, LLMs) use the ServiceAccount entity
- **Rationale**: 
  - Consistent credential management across all integrations
  - Same pattern for Windows AD, ServiceNow, Ollama, OpenAI, Anthropic, etc.
  - Centralized audit trail for all external access
  - Support for multiple credential storage methods (Vault, Environment, gMSA)
- **Provider Types**: `windows-ad`, `servicenow-basic`, `servicenow-oauth`, `llm-ollama`, `llm-openai`, `llm-anthropic`, `llm-azure-openai`, `llm-bedrock`

### ADR-007: Capability Routing (January 2025)
- **Decision**: Agents request capabilities, not specific Tool Servers
- **Rationale**:
  - Decouples agent logic from infrastructure details
  - Enables multi-tool-server deployments (Windows, Linux, SAP, etc.)
  - Supports automatic failover to healthy servers
  - Allows load balancing across multiple servers with same capability
- **Flow**: Agent requests "ad-password-reset" → CapabilityRouter queries Admin Portal → Returns healthy Tool Server URL
- **Note**: CapabilityRouter lives in the Agent (Python code), queries Admin Portal API

### ADR-008: Agent Configuration from Admin Portal (January 2025)
- **Decision**: Agent retrieves configuration from Admin Portal API at startup
- **Rationale**:
  - Centralized configuration management
  - No need to update agent config files for changes
  - Admin Portal becomes single source of truth
- **Startup Flow**: Agent starts with `ADMIN_PORTAL_URL` + `AGENT_NAME` → Calls `/api/agents/{name}/configuration` → Receives LLM provider, ServiceNow connection, assignment group
- **Future Enhancement**: Cache configuration locally for resilience if Admin Portal is unavailable

### ADR-009: LLM Reasons, Tools Execute (January 2026)
- **Decision**: LLM handles ambiguity (user lookup, group selection), Tools execute precise commands
- **Pattern**: Query first (GET endpoints return options), Act second (POST with exact parameters)
- **Tool Types**: Query Tools (read-only, inform LLM) vs Action Tools (execute changes)
- **See**: `docs/adr/ADR-009-llm-reasons-tools-execute.md`

## Directory Structure

```
lucid-it-agent/
├── agent/                  # Griptape agent (Python)
│   └── src/agent/
│       ├── classifier/     # LLM-powered ticket classification
│       ├── pipeline/       # Ticket processing orchestration
│       │   └── handlers/   # Type-specific handlers
│       ├── tools/          # Griptape tool wrappers (call tool-server)
│       ├── routing/        # Capability routing (NEW)
│       ├── drivers/        # LLM driver factory (NEW)
│       ├── rulesets/       # Agent behavior rules
│       └── config/         # Runtime configuration
├── admin/                  # Admin Portal (NEW)
│   └── dotnet/
│       └── src/
│           ├── LucidAdmin.Core/           # Domain entities
│           ├── LucidAdmin.Infrastructure/ # EF Core, data access
│           └── LucidAdmin.Web/            # Blazor UI + REST API
├── connectors/             # Queue connectors (ServiceNow, email, etc.)
├── tool-server/            # Actual tool implementations
│   ├── python/             # MVP version (DEPRECATED - see ADR-005)
│   └── dotnet/             # Production version (.NET 8)
├── env-setup/              # Development environment bootstrapping
│   ├── servicenow/         # PDI setup scripts
│   └── dc/                 # Domain Controller mock data
├── config/                 # Configuration file examples
├── docs/                   # Documentation
│   ├── adr/                # Architecture Decision Records
│   ├── ARCHITECTURE.md
│   ├── WINDOWS_CONTAINERS_PRIMER.md
│   └── CLAUDE_CODE_PROMPT_DOTNET_SCAFFOLD.md
└── docker/                 # Container definitions
```

## .NET Tool Server Structure

```
tool-server/dotnet/
├── LucidToolServer.sln
├── src/
│   └── LucidToolServer/
│       ├── LucidToolServer.csproj
│       ├── Program.cs                 # Minimal API entry point
│       ├── Services/
│       │   ├── IActiveDirectoryService.cs
│       │   ├── ActiveDirectoryService.cs
│       │   ├── IFilePermissionService.cs
│       │   └── FilePermissionService.cs
│       ├── Models/
│       │   ├── Requests/
│       │   └── Responses/
│       └── Dockerfile
└── tests/
    └── LucidToolServer.Tests/
```

## Admin Portal Structure

```
admin/dotnet/
├── LucidAdmin.sln
├── src/
│   ├── LucidAdmin.Core/              # Domain layer (no dependencies)
│   │   ├── Entities/
│   │   │   ├── BaseEntity.cs
│   │   │   ├── ServiceAccount.cs     # All external connections
│   │   │   ├── ToolServer.cs
│   │   │   ├── Capability.cs
│   │   │   ├── CapabilityMapping.cs
│   │   │   ├── Agent.cs
│   │   │   └── AuditEvent.cs
│   │   └── Enums/
│   ├── LucidAdmin.Infrastructure/    # Data access layer
│   │   ├── Data/
│   │   │   ├── LucidDbContext.cs
│   │   │   └── Configurations/       # EF Core fluent config
│   │   └── Migrations/
│   └── LucidAdmin.Web/               # Presentation layer
│       ├── Program.cs
│       ├── Endpoints/                # Minimal API endpoints
│       │   ├── ServiceAccountEndpoints.cs
│       │   ├── ToolServerEndpoints.cs
│       │   ├── CapabilityEndpoints.cs
│       │   ├── AgentEndpoints.cs
│       │   └── AuditEndpoints.cs
│       └── Components/               # Blazor Server UI
│           └── Pages/
└── tests/
    └── LucidAdmin.Tests/
```

## Admin Portal API Reference

**Base URL**: `http://<admin-portal>:5000/api`

### Agent Configuration (for Agent startup)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /agents/{name}/configuration | Get full agent config (LLM, ServiceNow, etc.) |
| POST | /agents/{name}/heartbeat | Report agent health |
| POST | /agents/{name}/status | Update agent status |

### Capability Routing (for Agent runtime)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /capabilities | List all capabilities |
| GET | /capabilities/{name}/servers | Get healthy Tool Servers for capability |
| GET | /capabilities/{name}/servers?status=online | Filter by status |

### Management (for Admin UI)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET/POST/PUT/DELETE | /service-accounts | CRUD service accounts |
| GET/POST/PUT/DELETE | /tool-servers | CRUD tool servers |
| POST | /tool-servers/{id}/test | Test connectivity |
| POST | /tool-servers/{id}/heartbeat | Tool server heartbeat |
| GET/POST/PUT/DELETE | /agents | CRUD agents |
| GET/POST/PUT/DELETE | /capability-mappings | CRUD mappings |
| GET | /audit-events | Query audit log |

## Coding Standards

### Python
- Use `pyproject.toml` for dependencies (not requirements.txt)
- Type hints required for all public functions
- Docstrings in Google format
- Black for formatting, Ruff for linting

### C# (.NET)
- Use `record` types for DTOs (immutable, clean)
- XML documentation comments on public interfaces
- Minimal APIs (not MVC controllers)
- Dependency injection for services
- `ILogger<T>` for logging

### Naming Conventions
- Tool classes: `<Action><Target>Tool` (e.g., `ResetPasswordTool`, `ModifyGroupTool`)
- Structures: `<Phase>Pipeline` or `<Purpose>Workflow` (e.g., `TicketIngestionPipeline`)
- Rulesets: `<context>_rules.py` (e.g., `security_rules.py`, `communication_rules.py`)
- .NET Services: `I<Name>Service` interface, `<Name>Service` implementation

### Configuration
- All secrets via environment variables
- Runtime config in YAML (Python) or appsettings.json (.NET)
- Use `pydantic` (Python) or `IOptions<T>` (.NET) for config validation

## Current Sprint Focus

**Sprint: .NET Tool Server Migration** (Current)
- [ ] Set up Windows container host (member server)
- [ ] Install Docker on Windows Server
- [ ] Scaffold .NET 8 solution (see `docs/CLAUDE_CODE_PROMPT_DOTNET_SCAFFOLD.md`)
- [ ] Implement password reset endpoint
- [ ] Integration test against DC
- [ ] Configure gMSA for production

**Previous Work (Complete)**
- [x] Project structure
- [x] Local development environment (Ollama + Llama 3.1 8B)
- [x] Basic agent scaffold with tool calling test (5/5 tests passing)
- [x] DC mock environment PowerShell script (Setup-TestEnvironment.ps1)
- [x] Python tool server MVP (deprecated, see ADR-005)
- [x] ServiceNow connector and ticket classification

## Tool Server API Reference

The .NET tool server maintains API compatibility with the Python version.
Agent code does not need changes - only the tool server URL configuration.

**Base URL**: `http://<tool-server>:8080/api/v1`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /health | Health check (AD connectivity) |
| POST | /password/reset | Reset user password |
| POST | /groups/add-member | Add user to AD group |
| POST | /groups/remove-member | Remove user from AD group |
| GET | /groups/{groupName} | Get group info and members |
| GET | /user/{username}/groups | Get user's group memberships |
| POST | /permissions/grant | Grant NTFS permission |
| POST | /permissions/revoke | Revoke NTFS permission |
| GET | /permissions/{path} | List NTFS permissions |

## Testing Approach

### Agent Tests (Python/Linux)
1. **Unit tests**: Mock external services (ServiceNow, tool server)
2. **Integration tests**: Run against PDI and tool server

### Tool Server Tests (.NET)
1. **Unit tests**: Mock AD calls with interfaces (runs on Linux)
2. **Integration tests**: Run on Windows against real AD

```bash
# Run agent tests
cd agent
pytest

# Run .NET tool server tests (Linux - unit tests only)
cd tool-server/dotnet
dotnet test

# Run .NET integration tests (Windows with AD access)
dotnet test --filter "Category=Integration"
```

## Common Gotchas

1. **Ollama tool calling**: Requires model that supports tools (llama3.1, mistral, etc.)
2. **Griptape logging**: Set `Defaults.logging_config.logger_name` for custom logging
3. **ServiceNow rate limits**: PDI has strict rate limits; implement backoff
4. **Windows containers**: Require OS version match (container ≤ host)
5. **gMSA**: Container host must be domain-joined and authorized for the gMSA

## References

### Agent & LLM
- [Griptape Docs](https://docs.griptape.ai/latest/)
- [Griptape GitHub](https://github.com/griptape-ai/griptape)
- [Ollama](https://ollama.com/)

### Tool Server
- [.NET 8 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8)
- [ASP.NET Core Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [System.DirectoryServices.AccountManagement](https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.accountmanagement)
- [Windows Containers gMSA](https://learn.microsoft.com/en-us/virtualization/windowscontainers/manage-containers/manage-serviceaccounts)

### ServiceNow
- [ServiceNow REST API](https://developer.servicenow.com/dev.do#!/reference/api/latest/rest/)
