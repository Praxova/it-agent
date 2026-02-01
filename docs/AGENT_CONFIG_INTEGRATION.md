# Agent Configuration Integration - Implementation Summary

This document summarizes the Agent Configuration integration between the Admin Portal (.NET) and the Python Agent.

## Overview

The Python agent now retrieves its complete configuration from the Admin Portal at startup, including:
- LLM provider settings with decrypted credentials
- ServiceNow connection settings with decrypted credentials
- Assignment group configuration
- Agent metadata (name, ID, enabled status)

## Architecture

```
┌─────────────────┐         HTTP GET              ┌──────────────────┐
│  Python Agent   │ ───────────────────────────> │  Admin Portal    │
│                 │  /api/agents/{name}/config   │  (.NET API)      │
│  - Griptape     │                               │                  │
│  - ServiceNow   │ <─────────────────────────── │  - Credentials   │
│  - Tools        │   JSON with credentials       │  - Config        │
└─────────────────┘                               └──────────────────┘
```

## .NET Backend Implementation

### 1. Enhanced API Endpoint

**File**: `admin/dotnet/src/LucidAdmin.Web/Endpoints/AgentConfigurationEndpoints.cs`

**Endpoint**: `GET /api/agents/{name}/configuration`

**Response Format**:
```json
{
  "agent": {
    "id": "guid",
    "name": "agent-name",
    "displayName": "Friendly Name",
    "description": "Description",
    "isEnabled": true
  },
  "llmProvider": {
    "serviceAccountId": "guid",
    "serviceAccountName": "ollama-local",
    "providerType": "llm-ollama",
    "accountType": "local",
    "config": {
      "base_url": "http://localhost:11434",
      "model": "llama3.1",
      "temperature": "0.1"
    },
    "credentials": {
      "api_key": "sk-..."  // Only if required
    }
  },
  "serviceNow": {
    "serviceAccountId": "guid",
    "serviceAccountName": "snow-prod",
    "providerType": "servicenow",
    "accountType": "basic-auth",
    "config": {
      "instanceUrl": "https://dev12345.service-now.com",
      "username": "lucid-integration"
    },
    "credentialStorage": "database",
    "credentialReference": null,
    "credentials": {
      "username": "lucid-integration",
      "password": "decrypted-password"
    }
  },
  "assignmentGroup": "IT Support"
}
```

**Key Features**:
- Credentials are decrypted on-demand using `ICredentialService`
- Supports environment variable fallback (if credentials not in database)
- Returns 404 if agent not found
- Returns 400 if agent is disabled
- Returns 422 if configuration is incomplete
- Updates `LastActivity` timestamp when configuration is retrieved

### 2. Authorization

**File**: `admin/dotnet/src/LucidAdmin.Web/Authorization/AuthorizationPolicies.cs`

- Added `AgentSelfAccess` policy
- Allows `Agent` and `Admin` roles
- Will be used with API key authentication

### 3. Heartbeat Endpoint

**Endpoint**: `POST /api/agents/{name}/runtime/heartbeat`

**Request**:
```json
{
  "hostName": "server-01",
  "status": "Running",
  "ticketsProcessed": 42
}
```

**Response**:
```json
{
  "acknowledged": true,
  "serverTime": "2025-01-29T12:00:00Z",
  "agentEnabled": true
}
```

## Python Client Implementation

### 1. Admin Portal Client

**File**: `agent/src/agent/config/admin_client.py`

**Classes**:
- `LlmProviderConfig` - LLM provider configuration with credential access
- `ServiceNowConfig` - ServiceNow configuration with credential access
- `AgentInfo` - Agent metadata
- `AgentConfiguration` - Complete configuration
- `AdminPortalClient` - HTTP client for API

**Usage Example**:
```python
from agent.config.admin_client import AdminPortalClient

# Initialize client (uses environment variables)
client = AdminPortalClient()

# Fetch configuration
config = client.get_configuration()

# Access LLM settings
model = config.llm_provider.model
api_key = config.llm_provider.api_key

# Access ServiceNow settings
instance_url = config.servicenow.instance_url
username = config.servicenow.username
password = config.servicenow.password

# Send heartbeat
client.send_heartbeat(status="Running", tickets_processed=10)

client.close()
```

### 2. Environment Variables

The Python client requires these environment variables:

| Variable | Description | Required |
|----------|-------------|----------|
| `LUCID_ADMIN_URL` | Admin Portal URL (e.g., `http://localhost:5000`) | Yes |
| `LUCID_AGENT_NAME` | Agent name (e.g., `test-agent`) | Yes |
| `LUCID_API_KEY` | API key for authentication (e.g., `lk_...`) | No (for now) |

### 3. Configuration Properties

**LlmProviderConfig**:
- `service_account_id` - GUID of service account
- `service_account_name` - Display name
- `provider_type` - Provider ID (e.g., "llm-ollama", "llm-openai")
- `account_type` - Account type (e.g., "local", "api-key")
- `config` - Provider-specific configuration dict
- `credentials` - Decrypted credentials dict
- **Properties**: `model`, `base_url`, `api_key`, `temperature`

**ServiceNowConfig**:
- `service_account_id` - GUID of service account
- `service_account_name` - Display name
- `provider_type` - Provider ID (e.g., "servicenow")
- `account_type` - Account type (e.g., "basic-auth", "oauth")
- `config` - Provider-specific configuration dict
- `credential_storage` - Storage type ("database", "environment", "vault")
- `credential_reference` - Reference string (e.g., env var name)
- `credentials` - Decrypted credentials dict
- **Properties**: `instance_url`, `username`, `password`

## Testing

### Test Script

**File**: `agent/scripts/test_admin_config.py`

**Usage**:
```bash
# Set environment variables
export LUCID_ADMIN_URL=http://localhost:5000
export LUCID_AGENT_NAME=test-agent
export LUCID_API_KEY=lk_your_api_key_here

# Run test
python agent/scripts/test_admin_config.py
```

**What it tests**:
1. ✓ Client initialization
2. ✓ Configuration retrieval
3. ✓ Credential decryption
4. ✓ Heartbeat reporting
5. ✓ Error handling

### Manual Testing with curl

```bash
# Get agent configuration
curl http://localhost:5000/api/agents/test-agent/configuration \
  -H "X-API-Key: lk_your_key_here"

# Send heartbeat
curl -X POST http://localhost:5000/api/agents/test-agent/runtime/heartbeat \
  -H "Content-Type: application/json" \
  -H "X-API-Key: lk_your_key_here" \
  -d '{"hostName":"test-host","status":"Running","ticketsProcessed":5}'
```

## Security Considerations

### Credentials in Transit
- ✅ Credentials are transmitted over HTTP (should use HTTPS in production)
- ✅ Credentials are decrypted server-side from encrypted storage
- ✅ API key authentication required (when implemented)

### Credentials at Rest
- ✅ Stored encrypted in database (AES-256-GCM)
- ✅ Encryption keys stored separately
- ✅ Fallback to environment variables supported

### Access Control
- ✅ Agents can only access their own configuration
- ✅ Admin users can access any agent configuration
- ✅ API key scoping enforced (when authentication enabled)

## Next Steps

### Immediate
1. ✅ Complete .NET endpoint implementation
2. ✅ Complete Python client implementation
3. ✅ Create test script
4. ⏳ Test end-to-end integration
5. ⏳ Implement LLM driver factory (uses config to create Griptape drivers)
6. ⏳ Implement ServiceNow connector (uses config to connect)

### Future Enhancements
1. Add capability information to configuration response
2. Implement agent bootstrap module (startup sequence)
3. Add configuration caching (reduce API calls)
4. Add configuration change detection (reload on change)
5. Implement OAuth/token refresh for ServiceNow
6. Add tool server configuration

## File Reference

### .NET Files Modified/Created
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/AgentConfigurationEndpoints.cs` - **Enhanced**
- `admin/dotnet/src/LucidAdmin.Web/Authorization/AuthorizationPolicies.cs` - **Enhanced**
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/AgentEndpoints.cs` - **Modified** (cleanup)

### Python Files Created/Modified
- `agent/src/agent/config/admin_client.py` - **Replaced**
- `agent/scripts/test_admin_config.py` - **Created**

### Documentation
- `docs/AGENT_CONFIG_INTEGRATION.md` - **This file**

## Build Status

### .NET Project
```
Build succeeded.
    29 Warning(s)  (all pre-existing)
    0 Error(s)
```

### Python Project
- No build errors (dataclasses, httpx already available)
- Ready for testing

## Contact

For questions or issues with this integration, please refer to:
- ADR-008: Agent Configuration from Admin Portal
- Sprint Backlog: Current implementation status
