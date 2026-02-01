# Architecture

## System Overview

Lucid IT Agent is designed as a modular, extensible system for automating IT helpdesk operations.
The architecture prioritizes:

1. **Separation of concerns** - Agent logic separate from tool implementations
2. **Extensibility** - Easy to add new connectors, tools, and LLM providers
3. **Deployment flexibility** - Local LLM or cloud, on-prem or hosted
4. **Multi-platform support** - Multiple tool servers for different platforms (Windows, Linux, SAP, etc.)
5. **Centralized management** - Admin Portal for configuration and monitoring
6. **Auditability** - Full logging of all decisions and actions

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                                 ADMIN PORTAL                                         │
│                          (Centralized Configuration)                                 │
│                                                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌───────────┐ │
│  │ Service     │  │ Tool        │  │  Agents     │  │ Capability  │  │  Audit    │ │
│  │ Accounts    │  │ Servers     │  │             │  │ Mappings    │  │  Events   │ │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └─────┬─────┘ │
│         │                │                │                │                │       │
│         └────────────────┴────────────────┴────────────────┴────────────────┘       │
│                                         │                                           │
│                              REST API   │   /api/                                   │
└─────────────────────────────────────────┼───────────────────────────────────────────┘
                                          │
                    ┌─────────────────────┴─────────────────────┐
                    │         Agent Startup Configuration       │
                    │  • LLM Provider (ServiceAccount)          │
                    │  • ServiceNow Connection (ServiceAccount) │
                    │  • Available Capabilities                 │
                    └─────────────────────┬─────────────────────┘
                                          │
                                          ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                                  LUCID AGENT                                         │
│                              (Python / Griptape)                                     │
│                                                                                     │
│  ┌───────────────────────────────────────────────────────────────────────────────┐ │
│  │                           TicketExecutor                                       │ │
│  │                                                                               │ │
│  │  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐           │ │
│  │  │  ServiceNow     │    │  Ticket         │    │   Ticket        │           │ │
│  │  │  Connector      │───▶│  Classifier     │───▶│   Handlers      │           │ │
│  │  │                 │    │  (LLM-powered)  │    │                 │           │ │
│  │  └────────┬────────┘    └────────┬────────┘    └────────┬────────┘           │ │
│  │           │                      │                      │                     │ │
│  │           │                      │                      │                     │ │
│  │           ▼                      ▼                      ▼                     │ │
│  │  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐           │ │
│  │  │ ServiceAccount  │    │  LLM Provider   │    │  Capability     │           │ │
│  │  │ (servicenow-*)  │    │  (llm-*)        │    │  Router         │           │ │
│  │  └─────────────────┘    └─────────────────┘    └────────┬────────┘           │ │
│  │                                                         │                     │ │
│  └─────────────────────────────────────────────────────────┼─────────────────────┘ │
│                                                            │                       │
└────────────────────────────────────────────────────────────┼───────────────────────┘
                                                             │
                    ┌────────────────────────────────────────┼────────────────────────┐
                    │            Capability Routing          │                        │
                    │  "ad-password-reset" → Windows Server  │                        │
                    │  "linux-user-mgmt" → Linux Server      │                        │
                    │  "sap-role-assign" → SAP Server        │                        │
                    └────────────────────────────────────────┼────────────────────────┘
                                                             │
             ┌───────────────────────────────────────────────┼───────────────────────┐
             │                                               │                       │
             ▼                                               ▼                       ▼
┌───────────────────────┐               ┌───────────────────────┐   ┌───────────────────────┐
│   Windows ToolServer  │               │   Linux ToolServer    │   │   SAP ToolServer      │
│   (.NET)              │               │   (Python/Go)         │   │   (future)            │
│                       │               │                       │   │                       │
│ Capabilities:         │               │ Capabilities:         │   │ Capabilities:         │
│ • ad-password-reset   │               │ • linux-user-create   │   │ • sap-user-create     │
│ • ad-group-add        │               │ • linux-user-disable  │   │ • sap-role-assign     │
│ • ad-group-remove     │               │ • ssh-key-rotation    │   │ • sap-unlock          │
│ • ntfs-permission     │               │ • linux-group-mgmt    │   │ • sap-password-reset  │
│                       │               │ • linux-permissions   │   │                       │
│ Service Account:      │               │                       │   │ Service Account:      │
│ windows-ad            │               │ Service Account:      │   │ sap-admin             │
└───────────────────────┘               │ linux-admin           │   └───────────────────────┘
         │                              └───────────────────────┘            │
         │                                        │                         │
         ▼                                        ▼                         ▼
┌───────────────────────┐               ┌───────────────────────┐   ┌───────────────────────┐
│   Active Directory    │               │   Linux Servers       │   │   SAP System          │
│   montanifarms.com    │               │   (SSH/API)           │   │   (RFC/API)           │
└───────────────────────┘               └───────────────────────┘   └───────────────────────┘
```

## Core Concepts

### Service Accounts (Unified External Connection Pattern)

All external system connections use the **ServiceAccount** pattern. This provides:
- Centralized credential management
- Consistent configuration structure
- Audit trail for all external access
- Support for multiple credential storage methods (Vault, Environment, gMSA)

**Provider Types:**

| Provider | Purpose | Configuration |
|----------|---------|---------------|
| `windows-ad` | Active Directory operations | Domain, SAM Account Name, OU Path |
| `servicenow-basic` | ServiceNow API (Basic Auth) | Instance URL, Username |
| `servicenow-oauth` | ServiceNow API (OAuth) | Instance URL, Client ID, Token Endpoint |
| `llm-ollama` | Local LLM via Ollama | Endpoint URL, Model Name |
| `llm-openai` | OpenAI API | Model Name (API key in credentials) |
| `llm-anthropic` | Anthropic API | Model Name (API key in credentials) |
| `llm-azure-openai` | Azure OpenAI | Endpoint, Deployment Name |
| `llm-bedrock` | AWS Bedrock | Region, Model ID |

### Capability Routing

Agents don't directly reference Tool Servers. Instead, they request **capabilities** and the system routes to an appropriate server:

```
Agent requests: "ad-password-reset"
                    │
                    ▼
┌─────────────────────────────────────────────────────────────┐
│                    Capability Router                         │
│                                                             │
│  1. Query Admin Portal: GET /api/capabilities/ad-password-reset/servers
│  2. Receive list of Tool Servers with this capability       │
│  3. Filter by health status (Online, responding)            │
│  4. Select best server (by priority, load, proximity)       │
│  5. Return Tool Server URL                                  │
└─────────────────────────────────────────────────────────────┘
                    │
                    ▼
            Windows Tool Server
            http://dc01.montanifarms.com:8100
```

**Benefits:**
- **Decoupling**: Agent doesn't need to know about server infrastructure
- **Failover**: If one server is unhealthy, route to another
- **Load balancing**: Distribute requests across multiple servers
- **Platform abstraction**: Same capability can be provided by different platforms

### Agent Configuration Flow

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           AGENT STARTUP SEQUENCE                                 │
└─────────────────────────────────────────────────────────────────────────────────┘

1. Agent starts with minimal config: ADMIN_PORTAL_URL, AGENT_NAME

2. Agent calls: GET /api/agents/{name}/configuration
   ┌─────────────────────────────────────────────────────────────────────────────┐
   │ Response:                                                                    │
   │ {                                                                           │
   │   "agent": { "name": "helpdesk-agent-01", "display_name": "...", ... },    │
   │   "llm_provider": {                                                         │
   │     "provider_type": "llm-ollama",                                          │
   │     "config": { "endpoint": "http://localhost:11434", "model": "llama3.1" } │
   │   },                                                                        │
   │   "servicenow": {                                                           │
   │     "provider_type": "servicenow-basic",                                    │
   │     "config": { "instance_url": "https://dev12345.service-now.com" },       │
   │     "credentials": { "storage": "environment", "reference": "SNOW_*" }      │
   │   },                                                                        │
   │   "assignment_group": "Helpdesk"                                            │
   │ }                                                                           │
   └─────────────────────────────────────────────────────────────────────────────┘

3. Agent creates Griptape PromptDriver from LLM provider config

4. Agent creates ServiceNowConnector from ServiceNow config

5. Agent initializes CapabilityRouter (queries Admin Portal at runtime)

6. Agent enters main loop: poll → classify → route → execute → update
```

## Component Details

### 1. Admin Portal

The Admin Portal provides centralized management for all Lucid IT Agent deployments:

**Entities:**
- **ServiceAccounts** - Credentials for all external systems (AD, ServiceNow, LLMs)
- **ToolServers** - Registered tool server instances with health monitoring
- **Capabilities** - Defined operations (ad-password-reset, linux-user-create, etc.)
- **CapabilityMappings** - Links Tool Servers to Capabilities with Service Accounts
- **Agents** - Registered agent instances with their configurations
- **AuditEvents** - Log of all operations for compliance

**Key APIs:**
```
# Agent Configuration
GET  /api/agents/{name}/configuration

# Capability Routing
GET  /api/capabilities/{name}/servers
GET  /api/capabilities/{name}/servers?status=online

# Health & Monitoring
POST /api/tool-servers/{id}/heartbeat
GET  /api/agents/{name}/status
```

### 2. Lucid Agent (Python)

The agent runtime built on Griptape framework:

```
agent/src/agent/
├── classifier/           # LLM-powered ticket classification
│   ├── classifier.py     # TicketClassifier class
│   ├── models.py         # TicketType, ClassificationResult
│   └── prompts.py        # Few-shot prompts for classification
├── pipeline/             # Ticket processing orchestration
│   ├── executor.py       # TicketExecutor (main loop)
│   ├── config.py         # PipelineConfig from env/API
│   └── handlers/         # Type-specific handlers
│       ├── base.py       # BaseHandler interface
│       ├── password_reset.py
│       ├── group_access.py
│       └── file_permission.py
├── tools/                # Griptape tool wrappers
│   ├── base.py           # BaseToolServerTool
│   ├── password_reset.py
│   ├── group_management.py
│   └── file_permissions.py
├── routing/              # Capability routing (NEW)
│   ├── router.py         # CapabilityRouter
│   └── models.py         # ToolServerInfo, RoutingResult
└── drivers/              # LLM driver factory (NEW)
    └── factory.py        # create_prompt_driver()
```

#### LLM Driver Factory

Creates appropriate Griptape PromptDriver from ServiceAccount configuration:

```python
from griptape.drivers.prompt import BasePromptDriver

def create_prompt_driver(service_account: dict) -> BasePromptDriver:
    """Create Griptape driver from ServiceAccount config."""
    provider_type = service_account["provider_type"]
    config = service_account["provider_config"]
    credentials = resolve_credentials(service_account)
    
    if provider_type == "llm-ollama":
        from griptape.drivers.prompt.ollama import OllamaPromptDriver
        return OllamaPromptDriver(
            model=config["model"],
            host=config["endpoint"],
            options={"temperature": config.get("temperature", 0.1)},
        )
    
    elif provider_type == "llm-openai":
        from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
        return OpenAiChatPromptDriver(
            model=config["model"],
            api_key=credentials["api_key"],
            temperature=config.get("temperature", 0.1),
        )
    
    elif provider_type == "llm-anthropic":
        from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
        return AnthropicPromptDriver(
            model=config["model"],
            api_key=credentials["api_key"],
        )
    
    # Additional providers...
```

#### Capability Router

Routes capability requests to appropriate Tool Servers:

```python
class CapabilityRouter:
    """Routes capability requests to appropriate tool servers."""
    
    def __init__(self, admin_api_url: str):
        self._api_url = admin_api_url
        self._cache: dict[str, CacheEntry] = {}
        self._cache_ttl = 60  # seconds
    
    async def get_server_for_capability(
        self, 
        capability_name: str
    ) -> ToolServerInfo:
        """Find a healthy tool server that provides this capability.
        
        Args:
            capability_name: The capability needed (e.g., "ad-password-reset")
            
        Returns:
            ToolServerInfo with URL and connection details
            
        Raises:
            NoCapableServerError: If no healthy server provides this capability
        """
        # Check cache first
        if self._is_cached(capability_name):
            return self._cache[capability_name].server
        
        # Query Admin Portal
        response = await self._client.get(
            f"{self._api_url}/capabilities/{capability_name}/servers",
            params={"status": "online"}
        )
        
        servers = response.json()
        if not servers:
            raise NoCapableServerError(f"No server provides: {capability_name}")
        
        # Select best server (first healthy one for now)
        # Future: load balancing, proximity, priority
        server = ToolServerInfo(**servers[0])
        
        # Cache result
        self._cache[capability_name] = CacheEntry(server, time.time())
        
        return server
```

### 3. Tool Servers

Tool Servers execute actual operations against target systems. Each server:
- Registers with Admin Portal on startup
- Reports capabilities it provides
- Sends periodic heartbeats
- Executes operations via REST API

**Tool Server API (all platforms):**
```
# Health & Registration
GET  /api/health                    # Returns health status, version
POST /api/register                  # Register with Admin Portal (startup)
GET  /api/capabilities              # List capabilities this server provides

# Tool Execution
POST /api/v1/tools/password/reset
POST /api/v1/tools/groups/add-member
POST /api/v1/tools/groups/remove-member
POST /api/v1/tools/permissions/grant
POST /api/v1/tools/permissions/revoke
```

**Platform Implementations:**

| Platform | Technology | Target Systems |
|----------|------------|----------------|
| Windows | .NET 8 / C# | Active Directory, NTFS, Exchange |
| Linux | Python or Go | Linux users/groups, SSH keys, permissions |
| SAP | Python + PyRFC | SAP user management, role assignment |
| Cloud | Various | Azure AD, AWS IAM, GCP IAM |

### 4. Ticket Classification

The classifier uses LLM with few-shot prompting to categorize tickets:

```python
class TicketClassifier:
    """Classifies IT tickets using LLM with few-shot prompting."""
    
    def __init__(self, prompt_driver: BasePromptDriver):
        # Accept any Griptape prompt driver
        self.driver = prompt_driver
        self.agent = Agent(prompt_driver=self.driver)
    
    def classify(self, ticket: Ticket) -> ClassificationResult:
        prompt = build_classification_prompt(ticket.model_dump())
        result = self.agent.run(prompt)
        return self._parse_response(result.output_task.output.value)
```

**Ticket Types:**
- `password_reset` - Password forgotten, account locked
- `group_access_add` - Add user to AD group
- `group_access_remove` - Remove user from AD group
- `file_permission` - Grant/revoke file/folder access
- `unknown` - Requires human review

**Classification Output:**
```json
{
  "ticket_type": "password_reset",
  "confidence": 0.95,
  "reasoning": "User explicitly states forgot password",
  "affected_user": "jsmith",
  "target_group": null,
  "target_resource": null,
  "should_escalate": false,
  "escalation_reason": null
}
```

## Data Model

### Entity Relationships

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              ENTITY RELATIONSHIPS                                │
└─────────────────────────────────────────────────────────────────────────────────┘

ServiceAccount (1) ◄───────────────────────────────────────► (N) CapabilityMapping
     │                                                              │
     │  Provider Types:                                             │
     │  • windows-ad          ──► AD operations                     │
     │  • servicenow-basic    ──► Ticket queue                      │
     │  • servicenow-oauth    ──► Ticket queue                      │
     │  • llm-ollama          ──► Classification                    │
     │  • llm-openai          ──► Classification                    │
     │  • llm-anthropic       ──► Classification                    │
     │                                                              │
     └──────────────────────────────────────────────────────────────┘

Agent
  │
  ├──► LlmServiceAccountId (FK) ──────────────► ServiceAccount (llm-*)
  │
  ├──► ServiceNowAccountId (FK) ──────────────► ServiceAccount (servicenow-*)
  │
  └──► AssignmentGroup (string) ──────────────► ServiceNow queue to monitor

ToolServer (1) ◄───────────────────────────────────────────► (N) CapabilityMapping
     │
     │  Properties:
     │  • Name, DisplayName, Description
     │  • Url (endpoint)
     │  • Region (for routing)
     │  • Status (Online, Offline, Degraded)
     │  • LastHeartbeat
     │
     └──────────────────────────────────────────────────────────────┘

Capability (1) ◄───────────────────────────────────────────► (N) CapabilityMapping
     │
     │  Examples:
     │  • ad-password-reset
     │  • ad-group-add
     │  • ad-group-remove
     │  • ntfs-permission-grant
     │  • linux-user-create
     │
     └──────────────────────────────────────────────────────────────┘

CapabilityMapping
  │
  ├──► ToolServerId (FK) ─────► Which server provides this
  ├──► CapabilityId (FK) ─────► Which capability
  ├──► ServiceAccountId (FK) ─► Which credentials to use
  ├──► IsEnabled ─────────────► Active or not
  └──► Priority ──────────────► For routing preference
```

### Agent Entity

```csharp
public class Agent : BaseEntity
{
    // Identity
    public required string Name { get; set; }        // Unique identifier
    public string? DisplayName { get; set; }         // Human-friendly name
    public string? Description { get; set; }
    
    // Runtime monitoring
    public string? HostName { get; set; }            // Where agent runs
    public AgentStatus Status { get; set; }          // Running, Stopped, Error
    public DateTime? LastHeartbeat { get; set; }
    public DateTime? LastActivity { get; set; }
    public int TicketsProcessed { get; set; }
    
    // LLM Configuration (via ServiceAccount)
    public Guid? LlmServiceAccountId { get; set; }
    public ServiceAccount? LlmServiceAccount { get; set; }
    
    // ServiceNow Configuration (via ServiceAccount)
    public Guid? ServiceNowAccountId { get; set; }
    public ServiceAccount? ServiceNowAccount { get; set; }
    public string? AssignmentGroup { get; set; }     // Queue to monitor
    
    // Behavior
    public bool IsEnabled { get; set; }
    
    // NOTE: No direct ToolServer reference
    // Agent uses CapabilityRouter at runtime
}
```

## Data Flow

### Complete Ticket Processing Flow

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                         TICKET PROCESSING FLOW                                   │
└─────────────────────────────────────────────────────────────────────────────────┘

1. STARTUP
   Agent starts → Calls Admin Portal → Gets LLM + ServiceNow config
   
2. POLL
   ServiceNowConnector.poll_queue() → Returns list of new tickets
   
3. CLASSIFY (for each ticket)
   │
   ├─► Build prompt with ticket details + few-shot examples
   ├─► Send to LLM (via configured PromptDriver)
   ├─► Parse JSON response → ClassificationResult
   │
   │   Result: { ticket_type: "password_reset", confidence: 0.95, 
   │             affected_user: "luke.skywalker", ... }
   
4. ROUTE
   │
   ├─► If confidence < 0.6 or unknown → ESCALATE
   ├─► Find handler for ticket_type
   │
   │   PasswordResetHandler selected
   
5. VALIDATE
   │
   ├─► Check required fields present (affected_user)
   ├─► Check user exists in AD
   ├─► Check user not in deny list
   │
   │   If invalid → ESCALATE
   
6. EXECUTE
   │
   ├─► Handler requests capability: "ad-password-reset"
   ├─► CapabilityRouter queries Admin Portal
   ├─► Returns: Windows Tool Server URL
   ├─► Handler calls: POST /api/v1/tools/password/reset
   │   Body: { "username": "luke.skywalker", "new_password": "TempP@ss123!" }
   ├─► Tool Server executes against AD
   ├─► Returns: { "success": true, "message": "Password reset for luke.skywalker" }
   
7. COMMUNICATE
   │
   ├─► Generate customer message with temp password
   ├─► Add work notes to ticket
   ├─► Close ticket with resolution
   
8. AUDIT
   │
   └─► Log all actions to Admin Portal audit trail
```

## Security Considerations

### Authentication & Authorization

1. **Service Accounts**: All external access uses registered ServiceAccounts
2. **Credential Storage Options**:
   - `none` - For gMSA accounts (Windows)
   - `vault` - HashiCorp Vault or similar (recommended for production)
   - `environment` - Environment variables (development only)
3. **API Authentication**: All APIs require authentication (JWT, API key, or mTLS)
4. **Capability-Based Access**: Agents only access capabilities via router

### Audit Trail

All operations logged to Admin Portal:
- Agent ID
- Capability requested
- Tool Server used
- Operation result
- Timestamp
- Ticket reference

### Safeguards

1. **Deny Lists**: Certain accounts/groups cannot be modified
2. **Confidence Thresholds**: Low-confidence classifications escalate
3. **Health Checks**: Only healthy Tool Servers receive requests
4. **Rate Limiting**: Prevent runaway automation

## Deployment Configurations

### Configuration 1: Single Tool Server (Development/Small)

```
┌─────────────────────────────────────────────────────────────────┐
│                        Single Site                               │
│                                                                 │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────────────────┐│
│  │ Admin   │  │ Agent   │  │ Ollama  │  │  Windows Tool       ││
│  │ Portal  │  │         │  │ (LLM)   │  │  Server (AD/NTFS)   ││
│  └─────────┘  └─────────┘  └─────────┘  └─────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### Configuration 2: Multi-Tool Server (Enterprise)

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              Headquarters                                        │
│  ┌─────────┐  ┌─────────┐  ┌─────────────┐                                     │
│  │ Admin   │  │ Agent   │  │ OpenAI API  │  (Cloud LLM)                        │
│  │ Portal  │  │         │  │             │                                     │
│  └─────────┘  └─────────┘  └─────────────┘                                     │
└─────────────────────────────────────────────────────────────────────────────────┘
        │
        │  Capability Routing
        │
        ├──────────────────────────────────────┬──────────────────────────────────┐
        │                                      │                                  │
        ▼                                      ▼                                  ▼
┌───────────────────┐              ┌───────────────────┐              ┌───────────────────┐
│   US-EAST         │              │   US-WEST         │              │   EU-WEST         │
│   Windows Server  │              │   Linux Server    │              │   Windows Server  │
│                   │              │                   │              │                   │
│   Capabilities:   │              │   Capabilities:   │              │   Capabilities:   │
│   • ad-*          │              │   • linux-*       │              │   • ad-*          │
│   • ntfs-*        │              │   • ssh-*         │              │   • ntfs-*        │
└───────────────────┘              └───────────────────┘              └───────────────────┘
```

### Configuration 3: Hybrid LLM (Cost Optimization)

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              Agent with LLM Fallback                             │
│                                                                                 │
│  Primary LLM: Ollama (local, free, for simple tickets)                         │
│  Fallback LLM: OpenAI GPT-4 (cloud, paid, for complex tickets)                 │
│                                                                                 │
│  Routing Logic (future enhancement):                                            │
│  • Classification confidence < 0.7 → Retry with GPT-4                          │
│  • Ticket contains legal/compliance keywords → Use GPT-4                        │
│  • Simple password reset → Use local Ollama                                     │
└─────────────────────────────────────────────────────────────────────────────────┘
```

## Extensibility

### Adding a New LLM Provider

1. Add provider type to ServiceAccount (e.g., `llm-groq`)
2. Update `create_prompt_driver()` factory
3. Add Griptape driver dependency: `griptape[drivers-prompt-groq]`
4. Create ServiceAccount in Admin Portal

### Adding a New Tool Server Platform

1. Implement Tool Server API endpoints
2. Register capabilities with Admin Portal
3. Create ServiceAccount for target system credentials
4. Add CapabilityMappings

### Adding a New Ticket Type

1. Add type to `TicketType` enum
2. Add few-shot examples to `prompts.py`
3. Create new Handler in `pipeline/handlers/`
4. Register required capabilities

### Adding a New Connector (Jira, Email, etc.)

1. Implement `BaseConnector` interface
2. Add connector provider type to ServiceAccount
3. Update Agent config to support connector type

## Future Enhancements

### Planned Features

- **LLM Fallback Chain**: Primary → Secondary → Tertiary LLM routing
- **Capability Priority**: Route to preferred servers first
- **Geographic Routing**: Route to nearest Tool Server
- **Configuration Caching**: Agent caches config for resilience
- **Pipeline UI**: Visual editor for agent workflows
- **Multi-Tenant**: Isolated configurations per customer

### Architecture Decision Records

See [CLAUDE_CONTEXT.md](../CLAUDE_CONTEXT.md) for ADRs:
- ADR-001: Local LLM via Ollama
- ADR-002: Tool Server Architecture
- ADR-003: Queue Connector Pattern
- ADR-004: Agent Structure
- ADR-005: ServiceAccount as Unified Provider Pattern (NEW)
- ADR-006: Capability Routing (NEW)
- ADR-007: Agent Configuration from Admin Portal API (NEW)
