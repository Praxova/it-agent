# Test Plan: Delegated Service Account Health Checks

**Feature**: Service Account connectivity testing with Tool Server delegation for infrastructure providers

**Sprint**: Current
**Date**: 2026-01-30

---

## Overview

This test plan validates the delegated health check system that routes Active Directory connectivity tests through Tool Servers while maintaining direct testing for HTTP-based providers (ServiceNow, LLMs).

### Components Under Test

1. **Tool Server** - Health check endpoint (`/api/v1/health/test-connection`)
2. **Admin Portal Backend** - Service account test endpoint with routing logic
3. **Admin Portal Frontend** - UI for triggering tests and displaying results with user guidance

### Test Environment Requirements

- Admin Portal running (Blazor Server, ASP.NET Core)
- SQL Server database with migrations applied
- At least one configured Service Account (Windows-AD type)
- Optional: Tool Server instance for full integration testing
- Optional: Active Directory environment for end-to-end validation

---

## Test Scenarios

### Scenario 1: No Tool Server Configured (Warning State)

**Objective**: Verify graceful degradation when Tool Server infrastructure isn't available

**Prerequisites**:
- At least one Windows-AD service account exists
- No Tool Server records in database (or all disabled)

**Steps**:
1. Navigate to Service Accounts page
2. Click "Edit" on a Windows-AD service account
3. Scroll to "Test Connectivity" section
4. Observe the informational alert message
5. Click "Test Connection" button

**Expected Results**:
- ✅ Alert appears: "Tool Server Required: Testing AD connections requires a Tool Server with capability mappings"
- ✅ Alert includes links to "Configure Tool Server" and "Configure Capability Mappings"
- ✅ Test button returns "warning" status
- ✅ Result message: "No tool server available to test this connection"
- ✅ Details include recommendation to create Tool Server + Capability Mapping
- ✅ Health status NOT updated in database (remains Unknown or previous state)

**API Response Expected**:
```json
{
  "status": "warning",
  "message": "No tool server available to test this connection",
  "checkedAt": "2026-01-30T12:00:00Z",
  "details": {
    "recommendation": "Create a Tool Server and Capability Mapping for AD operations first. The tool server will perform the actual AD health check."
  }
}
```

---

### Scenario 2: Tool Server Without Capability Mapping (Warning State)

**Objective**: Verify system detects missing capability mappings

**Prerequisites**:
- Tool Server exists and is enabled
- NO capability mappings exist with CapabilityId starting with "ad-"

**Steps**:
1. Create a Tool Server (endpoint: http://localhost:5001)
2. Mark as enabled, save
3. Navigate to Service Accounts > Edit Windows-AD account
4. Click "Test Connection"

**Expected Results**:
- ✅ Same warning behavior as Scenario 1
- ✅ FindToolServerForProvider returns null (no ad-* capabilities found)
- ✅ User guidance shown in UI

**Database Query to Verify**:
```sql
SELECT * FROM CapabilityMappings
WHERE CapabilityId LIKE 'ad-%'
  AND ToolServerId IN (SELECT Id FROM ToolServers WHERE IsEnabled = 1)
```
Should return 0 rows.

---

### Scenario 3: Full Setup - Successful AD Connection

**Objective**: Validate end-to-end delegated health check with valid credentials

**Prerequisites**:
- Tool Server running at configured endpoint
- Capability Mapping exists (ToolServerId → CapabilityId "ad-user-management" or similar)
- Windows-AD service account with valid domain credentials
- Credentials stored in database (CredentialStorageType.Database)

**Steps**:
1. Ensure Tool Server is reachable (curl http://localhost:5001/health)
2. Navigate to Service Accounts > Edit Windows-AD account
3. Verify NO warning alert is shown (Tool Server detected)
4. Click "Test Connection"
5. Observe response time (should be ~2-5 seconds for AD validation)
6. Check result display

**Expected Results**:
- ✅ No warning alert visible (Tool Server mapping found)
- ✅ Test returns "healthy" status
- ✅ Message: "Successfully connected to Active Directory"
- ✅ Details include connected domain controller name
- ✅ ServiceAccount.HealthStatus updated to `Healthy`
- ✅ ServiceAccount.LastHealthCheck updated to current timestamp
- ✅ ServiceAccount.LastHealthMessage updated
- ✅ Audit event created with Action = `ServiceAccountConnectivityTest`, Success = true

**API Response Expected**:
```json
{
  "status": "healthy",
  "message": "Successfully connected to Active Directory",
  "checkedAt": "2026-01-30T12:05:00Z",
  "details": {
    "info": "Connected to domain controller: DC01.contoso.local"
  }
}
```

**Tool Server Request Sent**:
```json
{
  "providerType": "windows-ad",
  "domain": "contoso.local",
  "server": "dc01.contoso.local",
  "username": "svc-lucid",
  "password": "***",
  "additionalConfig": { "domain": "contoso.local", "server": "dc01.contoso.local" }
}
```

---

### Scenario 4: Full Setup - Failed AD Connection (Invalid Credentials)

**Objective**: Validate error handling for authentication failures

**Prerequisites**:
- Same as Scenario 3 but with INVALID password

**Steps**:
1. Edit service account, update password to incorrect value
2. Click "Test Connection"

**Expected Results**:
- ✅ Test returns "unhealthy" status
- ✅ Message: "Active Directory authentication failed"
- ✅ Details include error description
- ✅ ServiceAccount.HealthStatus updated to `Unhealthy`
- ✅ Audit event created with Success = false, ErrorMessage populated

**API Response Expected**:
```json
{
  "status": "unhealthy",
  "message": "Active Directory authentication failed",
  "checkedAt": "2026-01-30T12:10:00Z",
  "details": {
    "error": "The username or password is incorrect"
  }
}
```

---

### Scenario 5: Tool Server Unreachable

**Objective**: Validate error handling when Tool Server is offline

**Prerequisites**:
- Tool Server configured but NOT running (endpoint unreachable)
- Capability mapping exists

**Steps**:
1. Stop Tool Server process
2. Navigate to Service Accounts > Edit Windows-AD account
3. Click "Test Connection"

**Expected Results**:
- ✅ Test returns "unhealthy" status after timeout (~30 seconds)
- ✅ Message: "Cannot reach tool server at http://localhost:5001"
- ✅ Details include endpoint and connection error
- ✅ ServiceAccount.HealthStatus updated to `Unhealthy`
- ✅ Audit event created with error details

**API Response Expected**:
```json
{
  "status": "unhealthy",
  "message": "Cannot reach tool server at http://localhost:5001",
  "checkedAt": "2026-01-30T12:15:00Z",
  "details": {
    "endpoint": "http://localhost:5001",
    "error": "No connection could be made because the target machine actively refused it."
  }
}
```

---

### Scenario 6: Direct Provider Testing (ServiceNow)

**Objective**: Verify HTTP-based providers bypass Tool Server delegation

**Prerequisites**:
- ServiceNow service account configured
- Valid ServiceNow instance URL and credentials

**Steps**:
1. Navigate to Service Accounts > Edit ServiceNow account
2. Observe UI (should NOT show Tool Server warning)
3. Click "Test Connection"
4. Monitor network traffic (should call ServiceNow API directly, NOT Tool Server)

**Expected Results**:
- ✅ No Tool Server warning shown (provider doesn't require delegation)
- ✅ Test uses `provider.TestConnectivityAsync()` directly
- ✅ No HTTP call to Tool Server endpoint
- ✅ Response comes from ServiceNow provider implementation
- ✅ Audit event reflects ServiceNow test (not Tool Server delegation)

**Code Path Verification**:
```csharp
var requiresToolServer = providerType == "windows-ad";  // false for servicenow
if (requiresToolServer) { /* NOT EXECUTED */ }
else {
    var provider = providerRegistry.GetProvider(account.Provider);
    var testResult = await provider.TestConnectivityAsync(account);  // ✅ THIS PATH
}
```

---

### Scenario 7: Credential Retrieval Failure

**Objective**: Validate error handling when credentials cannot be retrieved

**Prerequisites**:
- Windows-AD service account with CredentialStorageType = Database
- CredentialReference points to non-existent record OR encryption key missing

**Steps**:
1. Manually corrupt credential reference in database
2. Click "Test Connection"

**Expected Results**:
- ✅ Test returns "unhealthy" status
- ✅ Message: "Failed to retrieve credentials from secure storage"
- ✅ Details: "Ensure credentials are stored and encrypted properly"
- ✅ No HTTP call made to Tool Server (fails before delegation)

**Known Issue**:
Line 332 in ServiceAccountEndpoints.cs has placeholder credential extraction:
```csharp
password = credentials.ToString(); // Placeholder - needs proper implementation
```
This scenario may need code updates to properly extract password from CredentialSet.

---

### Scenario 8: Multiple Tool Servers Available

**Objective**: Verify system selects appropriate Tool Server

**Prerequisites**:
- Multiple Tool Servers configured
- Only ONE has capability mapping for "ad-*" capabilities

**Steps**:
1. Create Tool Server A with "ad-user-management" mapping
2. Create Tool Server B with "file-operations" mapping (no ad-*)
3. Test Windows-AD service account

**Expected Results**:
- ✅ Query selects Tool Server A (has ad-* capability)
- ✅ Test request routed to Tool Server A endpoint
- ✅ Tool Server B ignored

**Database Query**:
```sql
SELECT ts.Name, cm.CapabilityId
FROM CapabilityMappings cm
INNER JOIN ToolServers ts ON cm.ToolServerId = ts.Id
WHERE ts.IsEnabled = 1
  AND cm.CapabilityId LIKE 'ad-%'
```

---

### Scenario 9: Tool Server Returns Non-Success Status Code

**Objective**: Validate error handling for Tool Server HTTP errors

**Prerequisites**:
- Tool Server running but returns 500 Internal Server Error

**Steps**:
1. Modify Tool Server to throw exception (or use test double)
2. Test Windows-AD service account

**Expected Results**:
- ✅ Test returns "unhealthy" status
- ✅ Message: "Tool server returned error: InternalServerError"
- ✅ Details include status code (500) and error content
- ✅ Audit event created with error details

---

### Scenario 10: UI State After Successful Test

**Objective**: Verify UI updates correctly after test completion

**Prerequisites**:
- Successful test from Scenario 3

**Steps**:
1. Complete successful test
2. Observe UI changes
3. Refresh page
4. Navigate away and back to Edit page

**Expected Results**:
- ✅ Health status badge updates to "Healthy" (green)
- ✅ Last check timestamp displays
- ✅ Result message shown below test button
- ✅ State persists after page refresh (database updated)
- ✅ Navigating away and back shows latest health status

---

## Edge Cases

### EC1: Tool Server Endpoint with Trailing Slash
- Config: `http://localhost:5001/`
- Expected: `.TrimEnd('/')` handles correctly → `http://localhost:5001/api/v1/health/test-connection`

### EC2: Missing Configuration JSON
- ServiceAccount.Configuration is null or empty
- Expected: config dictionary is null, username/password extraction handles gracefully

### EC3: Invalid Configuration JSON
- Configuration contains malformed JSON
- Expected: Deserialization fails, caught in try-catch, test fails gracefully

### EC4: Concurrent Test Requests
- Multiple users test same service account simultaneously
- Expected: Each request independent, no race conditions on health status update

---

## Performance Acceptance Criteria

| Scenario | Max Response Time | Notes |
|----------|------------------|-------|
| No Tool Server (warning) | < 500ms | Database query only |
| Tool Server unreachable | < 31s | 30s timeout + processing |
| Successful AD test | < 10s | AD validation + network latency |
| Failed AD test | < 10s | Same as successful |
| Direct provider test | < 5s | HTTP API call to ServiceNow/LLM |

---

## Test Data Setup Script

```sql
-- Create Tool Server
INSERT INTO ToolServers (Id, Name, Endpoint, IsEnabled, CreatedAt, UpdatedAt)
VALUES (NEWID(), 'Primary Tool Server', 'http://localhost:5001', 1, GETUTCDATE(), GETUTCDATE());

-- Create Capability Mapping
INSERT INTO CapabilityMappings (Id, ToolServerId, CapabilityId, IsEnabled, CreatedAt, UpdatedAt)
SELECT NEWID(), ts.Id, 'ad-user-management', 1, GETUTCDATE(), GETUTCDATE()
FROM ToolServers ts WHERE ts.Name = 'Primary Tool Server';

-- Create Test Service Account
INSERT INTO ServiceAccounts (Id, Name, DisplayName, Description, Provider, AccountType,
    Configuration, CredentialStorage, CredentialReference, IsEnabled, HealthStatus, CreatedAt, UpdatedAt)
VALUES (NEWID(), 'test-ad-account', 'Test AD Account', 'For health check testing',
    'windows-ad', 'service',
    '{"domain":"contoso.local","server":"dc01.contoso.local","username":"svc-lucid"}',
    1, 'cred-ref-123', 1, 2, GETUTCDATE(), GETUTCDATE());
```

---

## Automated Test Coverage

### Unit Tests (Tool Server)
- [ ] `HealthEndpoints.TestActiveDirectoryConnection` - valid credentials
- [ ] `HealthEndpoints.TestActiveDirectoryConnection` - invalid credentials
- [ ] `HealthEndpoints.TestActiveDirectoryConnection` - unreachable domain
- [ ] `HealthEndpoints.MapHealthEndpoints` - unsupported provider returns BadRequest

### Integration Tests (Admin Portal)
- [ ] `ServiceAccountEndpoints.DelegateTestToToolServer` - successful delegation
- [ ] `ServiceAccountEndpoints.DelegateTestToToolServer` - no tool server found
- [ ] `ServiceAccountEndpoints.DelegateTestToToolServer` - tool server unreachable
- [ ] `ServiceAccountEndpoints.DelegateTestToToolServer` - credential retrieval failure
- [ ] `ServiceAccountEndpoints.FindToolServerForProvider` - AD provider finds mapping
- [ ] `ServiceAccountEndpoints.FindToolServerForProvider` - non-AD provider returns null

### E2E Tests (Playwright/Selenium)
- [ ] Test connectivity button visible and clickable
- [ ] Warning alert appears when no Tool Server configured
- [ ] Health status updates after successful test
- [ ] Error message displays after failed test
- [ ] Links to Tool Server and Capability Mapping pages work

---

## Manual Testing Checklist

- [ ] Scenario 1: No Tool Server configured
- [ ] Scenario 2: Tool Server without capability mapping
- [ ] Scenario 3: Successful AD connection
- [ ] Scenario 4: Failed AD connection (invalid credentials)
- [ ] Scenario 5: Tool Server unreachable
- [ ] Scenario 6: Direct provider testing (ServiceNow)
- [ ] Scenario 7: Credential retrieval failure
- [ ] Scenario 8: Multiple Tool Servers
- [ ] Scenario 9: Tool Server returns 500 error
- [ ] Scenario 10: UI state after successful test
- [ ] EC1: Trailing slash in endpoint
- [ ] EC2: Missing configuration JSON
- [ ] EC3: Invalid configuration JSON
- [ ] EC4: Concurrent test requests

---

## Known Limitations & Future Work

1. **Credential Extraction (Line 332)**: Placeholder implementation needs proper CredentialSet password extraction
2. **Tool Server Selection**: Currently uses `.FirstOrDefaultAsync()` - no load balancing or priority
3. **Timeout Configuration**: Hard-coded 30s timeout - should be configurable
4. **Audit User Context**: Uses "System" instead of actual user performing test
5. **Capability Filtering**: Query filters on "ad-" prefix only - may need more granular mapping
6. **No Retry Logic**: Single HTTP call to Tool Server, no retry on transient failures

---

## Success Criteria

This feature is considered validated when:

✅ All 10 main scenarios pass
✅ All 4 edge cases handled correctly
✅ Performance criteria met
✅ No unhandled exceptions in logs
✅ Audit trail complete for all test operations
✅ UI provides clear guidance for configuration issues
✅ Direct providers bypass Tool Server (no regression)

---

## Rollback Plan

If critical issues found in production:

1. **Quick Fix**: Disable Tool Server delegation by hardcoding `requiresToolServer = false`
2. **Revert**: Roll back ServiceAccountEndpoints.cs to previous version (direct testing only)
3. **Feature Flag**: Add configuration flag `EnableToolServerDelegation` defaulting to false

---

**Prepared by**: Claude Code
**Review Date**: TBD
**Approved by**: TBD
