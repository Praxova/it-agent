# Sprint Backlog

## Overview

This document tracks the development sprints for Lucid IT Agent. Each sprint is approximately 1-2 weeks depending on complexity.

**Target**: MVP capable of handling password resets from ServiceNow queue with local LLM.

---

## Sprint 0: Foundation (Current)

**Goal**: Project setup, development environment, basic agent scaffold

**Status**: 🟡 In Progress

### Tasks

- [x] Project structure created
- [x] Documentation framework (README, ARCHITECTURE, CLAUDE_CONTEXT)
- [x] License selection (Apache 2.0)
- [x] pyproject.toml configuration
- [ ] Local development environment setup
  - [ ] Install Ollama
  - [ ] Pull Llama 3.1 8B model
  - [ ] Verify Griptape + Ollama integration
- [ ] Basic agent scaffold
  - [ ] Simple agent with OllamaPromptDriver
  - [ ] Verify tool calling works
  - [ ] Basic logging setup
- [ ] ServiceNow PDI setup
  - [ ] Create PDI instance
  - [ ] Create API user
  - [ ] Document PDI setup process
  - [ ] Create PDI bootstrap script
- [ ] DC mock environment
  - [ ] PowerShell script: Create-MockUsers.ps1
  - [ ] PowerShell script: Create-MockGroups.ps1
  - [ ] PowerShell script: Create-MockShares.ps1
  - [ ] Document DC setup process

### Acceptance Criteria

1. `ollama run llama3.1` works on development machine
2. Simple Griptape agent can call a mock tool
3. ServiceNow PDI accessible via API
4. DC has test users and groups for development

### Notes

- PDI instances are deleted after 10 days of inactivity - need bootstrap script
- DC environment scripts should be idempotent (can run multiple times safely)

---

## Sprint 1: ServiceNow Connector

**Goal**: Fetch tickets from ServiceNow queue

**Status**: ⬜ Not Started

### Tasks

- [ ] ServiceNow REST client
  - [ ] Authentication (Basic auth initially)
  - [ ] Incident table queries
  - [ ] Error handling and retries
- [ ] Ticket model
  - [ ] Pydantic model for Incident
  - [ ] Field mapping (ServiceNow → internal)
- [ ] Queue polling
  - [ ] Poll for new/updated incidents
  - [ ] Filter by assignment group
  - [ ] Handle pagination
- [ ] Connector interface
  - [ ] Define BaseConnector abstract class
  - [ ] Implement ServiceNowConnector
- [ ] Unit tests
  - [ ] Mock ServiceNow responses
  - [ ] Test error conditions

### Acceptance Criteria

1. Can authenticate to ServiceNow PDI
2. Can retrieve list of incidents from queue
3. Can fetch single incident by sys_id
4. Proper error handling for network/auth failures

### Definition of Done

- [ ] Code reviewed
- [ ] Unit tests passing
- [ ] Integration test with PDI passing
- [ ] Documentation updated

---

## Sprint 2: Ticket Classification

**Goal**: Agent can classify tickets by type

**Status**: ⬜ Not Started

### Tasks

- [ ] Classification pipeline
  - [ ] PromptTask for classification
  - [ ] Output schema (structured output)
  - [ ] Confidence scoring
- [ ] Ticket types
  - [ ] password_reset
  - [ ] group_access_add
  - [ ] group_access_remove
  - [ ] file_permission
  - [ ] unknown/escalate
- [ ] Classification rules
  - [ ] Ruleset for classification behavior
  - [ ] Examples in prompt
- [ ] Routing logic
  - [ ] Route to appropriate pipeline based on type
  - [ ] Escalate unknown types
- [ ] Tests
  - [ ] Test classification accuracy
  - [ ] Test edge cases

### Acceptance Criteria

1. Agent correctly classifies password reset tickets >90% of time
2. Agent correctly classifies group access tickets >90% of time
3. Unknown ticket types are escalated (not misclassified)

---

## Sprint 3: Password Reset Tool

**Goal**: Agent can reset AD passwords

**Status**: ⬜ Not Started

### Tasks

- [ ] Tool server setup (Python MVP)
  - [ ] FastAPI application
  - [ ] Health check endpoint
  - [ ] API authentication
- [ ] Password reset implementation
  - [ ] ldap3 integration
  - [ ] Reset password function
  - [ ] Generate temporary password
  - [ ] Error handling
- [ ] Griptape tool wrapper
  - [ ] PasswordResetTool class
  - [ ] Input validation
  - [ ] Call tool server API
- [ ] Security
  - [ ] Deny list (admin accounts)
  - [ ] Audit logging
  - [ ] Rate limiting
- [ ] Tests
  - [ ] Unit tests with mocked LDAP
  - [ ] Integration tests with DC

### Acceptance Criteria

1. Can reset password for test user in DC
2. Cannot reset password for admin accounts
3. Audit log shows all attempts
4. Temporary password meets complexity requirements

---

## Sprint 4: AD Group Management Tool

**Goal**: Agent can add/remove users from AD groups

**Status**: ⬜ Not Started

### Tasks

- [ ] Group membership implementation
  - [ ] Add user to group
  - [ ] Remove user from group
  - [ ] Verify membership
- [ ] Griptape tool wrapper
  - [ ] AddToGroupTool class
  - [ ] RemoveFromGroupTool class
- [ ] Security
  - [ ] Protected groups deny list
  - [ ] Audit logging
- [ ] Tests

### Acceptance Criteria

1. Can add test user to test group
2. Can remove test user from test group
3. Cannot modify protected groups
4. Changes verified in AD

---

## Sprint 5: Execution Pipeline

**Goal**: Full ticket processing workflow

**Status**: ⬜ Not Started

### Tasks

- [ ] Planning pipeline
  - [ ] Generate execution plan
  - [ ] Validate plan against capabilities
  - [ ] Permission checking
- [ ] Execution pipeline
  - [ ] Execute plan steps
  - [ ] Handle step failures
  - [ ] Rollback on failure (where possible)
- [ ] Validation
  - [ ] Verify tool execution succeeded
  - [ ] Confirm expected state
- [ ] Integration
  - [ ] Connect all pipelines
  - [ ] End-to-end flow

### Acceptance Criteria

1. Agent processes password reset ticket end-to-end
2. Agent processes group access ticket end-to-end
3. Failed executions are handled gracefully
4. Partial failures don't leave inconsistent state

---

## Sprint 6: User Communication

**Goal**: Agent communicates with end users via ticket updates

**Status**: ⬜ Not Started

### Tasks

- [ ] Response generation
  - [ ] Generate user-friendly messages
  - [ ] Include relevant details
  - [ ] Professional tone
- [ ] ServiceNow updates
  - [ ] Add work notes
  - [ ] Add customer-visible comments
  - [ ] Update ticket state
  - [ ] Close ticket with resolution
- [ ] Communication rules
  - [ ] Ruleset for communication style
- [ ] Tests

### Acceptance Criteria

1. Agent adds appropriate comments to tickets
2. Messages are professional and helpful
3. Ticket state reflects actual status
4. Users understand what was done

---

## Sprint 7: Production Hardening

**Goal**: Production-ready error handling and logging

**Status**: ⬜ Not Started

### Tasks

- [ ] Comprehensive logging
  - [ ] Structured logging (JSON)
  - [ ] Log levels appropriate
  - [ ] Sensitive data redacted
- [ ] Error handling
  - [ ] Graceful degradation
  - [ ] Retry logic with backoff
  - [ ] Circuit breaker for external services
- [ ] Monitoring
  - [ ] Health check endpoints
  - [ ] Metrics collection
- [ ] Configuration
  - [ ] Environment-based config
  - [ ] Secrets management
- [ ] Documentation
  - [ ] Deployment guide
  - [ ] Operations runbook

### Acceptance Criteria

1. No unhandled exceptions in normal operation
2. Failures are logged with context
3. Can diagnose issues from logs alone
4. Secrets not exposed in logs or errors

---

## Sprint 8: File Permissions Tool

**Goal**: Agent can modify NTFS permissions

**Status**: ⬜ Not Started

### Tasks

- [ ] File permission implementation
  - [ ] Grant permissions
  - [ ] Revoke permissions
  - [ ] List current permissions
- [ ] Griptape tool wrapper
- [ ] Security controls
- [ ] Tests

### Acceptance Criteria

1. Can grant read access to test share
2. Can revoke access from test share
3. Cannot modify system folders
4. Changes verified on file system

---

## Future Sprints (Backlog)

### Teams Integration
- Microsoft Teams connector
- Direct message notifications
- Interactive approval workflows

### Email Integration
- Email-to-ticket processing
- Email notifications

### Web UI
- Dashboard for monitoring
- Configuration interface
- Audit log viewer

### Onboarding Agent
- Automated customer setup
- Environment validation
- Configuration wizard

### C# Tool Server
- Production tool server rewrite
- Windows Service deployment
- Enhanced security

### Multi-Agent Support
- Multiple agents for scale
- Work distribution
- Agent coordination

---

## Sprint Metrics Template

Track these for each completed sprint:

| Metric | Target | Actual |
|--------|--------|--------|
| Tasks Completed | 100% | - |
| Test Coverage | >80% | - |
| Bugs Found | <3 | - |
| Bugs Fixed | 100% | - |
| Documentation Updated | Yes | - |

---

## Notes

- Sprints may be combined or split based on actual complexity
- Priorities may shift based on customer feedback
- Security reviews before each major feature release
