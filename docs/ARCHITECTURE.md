# Architecture

## System Overview

Lucid IT Agent is designed as a modular, extensible system for automating IT helpdesk operations.
The architecture prioritizes:

1. **Separation of concerns** - Agent logic separate from tool implementations
2. **Extensibility** - Easy to add new connectors and tools
3. **Deployment flexibility** - Local LLM or cloud, on-prem or hosted
4. **Auditability** - Full logging of all decisions and actions

## High-Level Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              TICKET SOURCES                                   │
├──────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │
│  │ ServiceNow  │  │    Jira     │  │   Email     │  │   Custom    │         │
│  │  Connector  │  │  Connector  │  │  Connector  │  │  Connector  │         │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘         │
│         │                │                │                │                 │
│         └────────────────┴────────────────┴────────────────┘                 │
│                                   │                                          │
│                                   ▼                                          │
│                    ┌──────────────────────────────┐                          │
│                    │     Connector Interface      │                          │
│                    │   (Abstract Base Class)      │                          │
│                    └──────────────┬───────────────┘                          │
└──────────────────────────────────┼───────────────────────────────────────────┘
                                   │
                                   ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                              AGENT CORE                                       │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │                         Main Workflow                                    │ │
│  │  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐               │ │
│  │  │   Ingestion   │  │   Planning    │  │  Execution    │               │ │
│  │  │   Pipeline    │──▶│   Pipeline    │──▶│  Pipeline    │               │ │
│  │  └───────────────┘  └───────────────┘  └───────────────┘               │ │
│  │         │                  │                   │                        │ │
│  │         ▼                  ▼                   ▼                        │ │
│  │  ┌─────────────┐   ┌─────────────┐    ┌─────────────┐                 │ │
│  │  │ Parse Ticket│   │Create Plan  │    │Execute Tools│                 │ │
│  │  │ Classify    │   │Check Perms  │    │Validate     │                 │ │
│  │  │ Route       │   │Validate     │    │Communicate  │                 │ │
│  │  └─────────────┘   └─────────────┘    └─────────────┘                 │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                   │                                          │
│                                   ▼                                          │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐          │
│  │    Rulesets      │  │  Conversation    │  │   Event Bus      │          │
│  │ (Behavior/Safety)│  │    Memory        │  │  (Audit Log)     │          │
│  └──────────────────┘  └──────────────────┘  └──────────────────┘          │
│                                                                              │
└──────────────────────────────────┬───────────────────────────────────────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
                    ▼                             ▼
┌─────────────────────────────────┐  ┌─────────────────────────────────┐
│          LLM BACKEND            │  │         TOOL SERVER             │
├─────────────────────────────────┤  ├─────────────────────────────────┤
│  ┌───────────────────────────┐  │  │  ┌───────────────────────────┐  │
│  │   Ollama (Local)          │  │  │  │   REST API                │  │
│  │   - Llama 3.1 8B          │  │  │  │   /api/v1/tools/*         │  │
│  │   - Tool calling          │  │  │  └───────────┬───────────────┘  │
│  └───────────────────────────┘  │  │              │                  │
│              OR                 │  │              ▼                  │
│  ┌───────────────────────────┐  │  │  ┌───────────────────────────┐  │
│  │   Cloud LLM               │  │  │  │   Tool Implementations    │  │
│  │   - OpenAI GPT-4          │  │  │  │   - PasswordResetTool     │  │
│  │   - Anthropic Claude      │  │  │  │   - ADGroupTool           │  │
│  │   - Azure OpenAI          │  │  │  │   - FilePermissionTool    │  │
│  └───────────────────────────┘  │  │  └───────────┬───────────────┘  │
└─────────────────────────────────┘  │              │                  │
                                     │              ▼                  │
                                     │  ┌───────────────────────────┐  │
                                     │  │   Target Systems          │  │
                                     │  │   - Active Directory      │  │
                                     │  │   - File Servers          │  │
                                     │  │   - Exchange/M365         │  │
                                     │  └───────────────────────────┘  │
                                     └─────────────────────────────────┘
```

## Component Details

### 1. Connectors

Connectors interface with external ticket systems. All connectors implement `BaseConnector`:

```python
class BaseConnector(ABC):
    @abstractmethod
    async def poll_queue(self) -> list[Ticket]: ...
    
    @abstractmethod
    async def get_ticket(self, ticket_id: str) -> Ticket: ...
    
    @abstractmethod
    async def update_ticket(self, ticket_id: str, update: TicketUpdate) -> Ticket: ...
    
    @abstractmethod
    async def add_comment(self, ticket_id: str, comment: str) -> None: ...
    
    @abstractmethod
    async def close_ticket(self, ticket_id: str, resolution: str) -> Ticket: ...
```

**Implemented Connectors:**
- `ServiceNowConnector` - ServiceNow REST API integration

**Planned Connectors:**
- `JiraConnector` - Jira Service Management
- `EmailConnector` - Direct email-to-ticket
- `ZendeskConnector` - Zendesk

### 2. Agent Core

The agent is built using Griptape structures:

#### Main Workflow

```python
# Simplified structure
workflow = Workflow(
    tasks=[
        IngestionPipeline(),      # Parallel: fetch ticket, load context
        PlanningPipeline(),       # Sequential: classify, plan, validate
        ExecutionPipeline(),      # Sequential: execute, validate, communicate
    ]
)
```

#### Pipelines

**Ingestion Pipeline**
- Fetch ticket from queue
- Parse ticket content (NLP extraction)
- Load user context (previous tickets, permissions)
- Initial classification

**Planning Pipeline**
- Detailed ticket classification
- Capability check (do we have tools for this?)
- Permission validation (is agent allowed to act?)
- Plan generation (sequence of tool calls)
- Plan validation (safety checks)

**Execution Pipeline**
- Execute plan steps
- Handle errors and retries
- Validate results
- Generate user communication
- Update/close ticket

### 3. Rulesets

Rulesets control agent behavior without modifying code:

```yaml
# rulesets/security_rules.yaml
name: security
rules:
  - "Never reset passwords for accounts in the 'Administrators' group"
  - "Always verify user identity before making changes"
  - "Log all actions with ticket number and timestamp"
  - "Escalate to human if confidence is below 80%"
```

```yaml
# rulesets/communication_rules.yaml
name: communication
rules:
  - "Use professional, friendly tone"
  - "Explain what actions were taken"
  - "Provide next steps if applicable"
  - "Include ticket number in all communications"
```

### 4. Tool Server

The tool server is a separate component that executes actual system changes:

**MVP (Python)**
```
tool-server/python/
├── src/
│   ├── api.py              # FastAPI REST server
│   └── tools/
│       ├── base.py         # BaseTool class
│       ├── ad_password.py  # Password reset implementation
│       ├── ad_groups.py    # Group membership
│       └── file_perms.py   # NTFS permissions
```

**Production (C#/.NET)** - Future
```
tool-server/dotnet/
├── LucidToolServer/
│   ├── Controllers/
│   │   └── ToolsController.cs
│   ├── Services/
│   │   ├── ActiveDirectoryService.cs
│   │   └── FileSystemService.cs
│   └── Program.cs
```

**Tool Server API:**
```
POST /api/v1/tools/password/reset
POST /api/v1/tools/groups/add-member
POST /api/v1/tools/groups/remove-member
POST /api/v1/tools/permissions/grant
POST /api/v1/tools/permissions/revoke
GET  /api/v1/tools/health
GET  /api/v1/tools/capabilities
```

### 5. LLM Backend

The agent supports multiple LLM backends via Griptape's driver abstraction:

```python
# Local LLM (default)
from griptape.drivers.prompt.ollama import OllamaPromptDriver
driver = OllamaPromptDriver(model="llama3.1")

# OpenAI
from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
driver = OpenAiChatPromptDriver(model="gpt-4")

# Anthropic
from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
driver = AnthropicPromptDriver(model="claude-3-sonnet-20240229")
```

## Data Flow

### Ticket Processing Flow

```
1. Connector polls queue
   └─▶ New ticket found

2. Ingestion Pipeline
   ├─▶ Fetch full ticket details
   ├─▶ Parse: extract issue type, affected user, urgency
   └─▶ Load context: user history, permissions

3. Planning Pipeline
   ├─▶ Classify: "password_reset" | "group_access" | "file_permission" | "unknown"
   ├─▶ Check capability: Do we have tools for this?
   │   └─▶ If NO: Escalate to human, add comment, EXIT
   ├─▶ Check permissions: Is agent allowed to modify this user/resource?
   │   └─▶ If NO: Escalate to human, add comment, EXIT
   └─▶ Generate plan: [verify_user, reset_password, notify_user]

4. Execution Pipeline
   ├─▶ Execute step 1: Call tool server /verify-user
   │   └─▶ If FAIL: Handle error or escalate
   ├─▶ Execute step 2: Call tool server /reset-password
   │   └─▶ If FAIL: Handle error or escalate
   ├─▶ Validate: Confirm password was reset
   ├─▶ Communicate: Generate resolution message
   └─▶ Close ticket with resolution notes

5. Audit
   └─▶ Log all actions, decisions, and outcomes
```

## Security Considerations

### Authentication & Authorization

1. **Service Accounts**: Tool server uses dedicated service accounts with minimal required permissions
2. **API Authentication**: Tool server API requires authentication (API key or mTLS)
3. **Ticket Verification**: Agent verifies ticket authenticity before acting
4. **User Verification**: Agent confirms affected user identity

### Audit Trail

All agent actions are logged:
- Ticket ID
- Action taken
- Timestamp
- Success/failure
- Any errors

### Safeguards

1. **Deny List**: Certain accounts/groups cannot be modified (e.g., Domain Admins)
2. **Rate Limiting**: Maximum actions per hour to prevent runaway automation
3. **Confidence Threshold**: Low-confidence classifications escalate to humans
4. **Dry Run Mode**: Test mode that logs actions without executing

## Deployment Options

### Option 1: Fully Local

```
┌─────────────────────────────────────────────────┐
│              Customer Data Center               │
│  ┌─────────┐  ┌─────────┐  ┌─────────────────┐ │
│  │ Agent   │  │ Ollama  │  │  Tool Server    │ │
│  │ Server  │  │ (LLM)   │  │  (AD/NTFS)      │ │
│  └─────────┘  └─────────┘  └─────────────────┘ │
└─────────────────────────────────────────────────┘
```

### Option 2: Hybrid (Local Tools, Cloud LLM)

```
┌───────────────────────────────┐
│      Customer Data Center     │     ┌─────────────────┐
│  ┌─────────┐  ┌────────────┐ │     │   Cloud LLM     │
│  │ Agent   │──│Tool Server │ │────▶│ (OpenAI/Claude) │
│  │ Server  │  │ (AD/NTFS)  │ │     └─────────────────┘
│  └─────────┘  └────────────┘ │
└───────────────────────────────┘
```

### Option 3: Fully Hosted (Future)

```
┌─────────────────────────────────────────────────┐
│              Lucid Cloud                        │
│  ┌─────────┐  ┌─────────┐                      │
│  │ Agent   │  │  LLM    │                      │
│  │ Server  │  │         │                      │
│  └────┬────┘  └─────────┘                      │
└───────┼─────────────────────────────────────────┘
        │ VPN/Secure Connection
        ▼
┌─────────────────────────────────────────────────┐
│              Customer Data Center               │
│           ┌─────────────────┐                  │
│           │  Tool Server    │                  │
│           │  (AD/NTFS)      │                  │
│           └─────────────────┘                  │
└─────────────────────────────────────────────────┘
```

## Extensibility

### Adding a New Tool

1. Implement tool in tool server (`tool-server/python/src/tools/`)
2. Create Griptape tool wrapper (`agent/src/agent/tools/`)
3. Register tool in configuration
4. Add any required rulesets

### Adding a New Connector

1. Implement `BaseConnector` interface
2. Add connector to `connectors/` directory
3. Register in configuration
4. Test with mock tickets

See [TOOL_DEVELOPMENT.md](TOOL_DEVELOPMENT.md) for detailed guides.
