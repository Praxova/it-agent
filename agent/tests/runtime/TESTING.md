# Runtime Testing Guide

## Test Categories

### Unit Tests (Default)
Fast tests that mock all external services.
```bash
cd agent
pytest tests/runtime/ -v
```

### Integration Tests
Tests that require running services (Admin Portal, Ollama).
```bash
# Set environment
export ADMIN_PORTAL_URL=http://localhost:5000
export OLLAMA_URL=http://localhost:11434

# Run integration tests
pytest tests/runtime/ -v -m integration
```

### Manual/Live Tests
Tests that interact with real systems (ServiceNow).
```bash
# Set ServiceNow credentials
export SNOW_INSTANCE_URL=https://dev12345.service-now.com
export SNOW_USERNAME=admin
export SNOW_PASSWORD=password
export SNOW_ASSIGNMENT_GROUP=helpdesk-group-id

# Run manual tests
pytest tests/runtime/test_live_integration.py -v -m manual
```

## Test Scenarios

### Happy Path
- Complete password reset: Trigger → Classify → Validate → Execute → Notify → Close → End
- All steps succeed, ticket resolved

### Escalation Scenarios
1. **Low Confidence**: Classification confidence < 0.8
2. **Validation Failure**: Admin user, service account, deny list match
3. **Execution Failure**: Tool Server error, timeout

### Edge Cases
- Missing ticket fields
- Invalid workflow transitions
- Max steps limit

## Test Fixtures

### Key Fixtures (conftest.py)
- `sample_export`: Complete agent export for testing
- `password_reset_workflow`: Full workflow with all steps and transitions
- `sample_rulesets`: Security and classification rulesets
- `mock_servicenow_client`: Async mock for ServiceNow
- `mock_capability_router`: Mock for Tool Server routing

### Mock Drivers (mock_drivers.py)
- `MockPromptDriver`: Returns configurable classification responses
- `MockToolServerClient`: Returns configurable tool responses

## Running Specific Tests
```bash
# Run only happy path tests
pytest tests/runtime/test_e2e_workflow.py::TestPasswordResetHappyPath -v

# Run escalation tests
pytest tests/runtime/test_e2e_workflow.py::TestLowConfidenceEscalation -v

# Run with coverage
pytest tests/runtime/ --cov=agent.runtime --cov-report=html

# Run with detailed output
pytest tests/runtime/ -v --tb=long
```

## CI/CD Integration

For CI pipelines, run unit tests only (no external dependencies):
```yaml
- name: Run unit tests
  run: |
    cd agent
    pytest tests/runtime/ -v -m "not integration and not manual"
```
