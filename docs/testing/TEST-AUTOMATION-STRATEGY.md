# Test Automation Strategy for Praxova

## Overview

The E2E test plan has ~40 test cases across three categories:
1. **API tests** (curl-based) — ~30 tests. Fully automatable with a script.
2. **UI tests** (browser interaction) — ~8 tests. Automatable via Playwright or Chrome MCP.
3. **Infrastructure tests** (container state, logs) — ~5 tests. Automatable with bash.

The goal: run one command, get a pass/fail report, investigate only the failures.

---

## Layer 1: API Test Runner (Python, pytest)

Most of the test plan is HTTP calls with expected status codes and response shapes.
This is textbook pytest territory.

### Structure

```
tests/
└── e2e/
    ├── conftest.py              # Shared fixtures: portal URL, API key, auth token
    ├── test_phase1_bootstrap.py
    ├── test_phase2_auth.py
    ├── test_phase3_tokens.py
    ├── test_phase4a_ad_auth.py
    ├── test_phase4b_llm.py
    ├── test_phase4c_mtls.py
    ├── test_phase5a_classification.py
    ├── test_phase5b_cert_renewal.py
    └── test_e2e_full_lifecycle.py
```

### conftest.py Fixtures

```python
import pytest
import httpx
import os
import time

@pytest.fixture(scope="session")
def portal_url():
    return os.environ.get("PRAXOVA_PORTAL_URL", "https://localhost:5001")

@pytest.fixture(scope="session")
def ca_cert():
    """Fetch and cache the CA cert for TLS verification."""
    http_url = os.environ.get("PRAXOVA_PORTAL_HTTP", "http://localhost:5000")
    resp = httpx.get(f"{http_url}/api/pki/trust-bundle")
    cert_path = "/tmp/praxova-ca-test.pem"
    with open(cert_path, "w") as f:
        f.write(resp.text)
    return cert_path

@pytest.fixture(scope="session")
def portal_client(portal_url, ca_cert):
    """Authenticated HTTPS client for portal API calls."""
    client = httpx.Client(base_url=portal_url, verify=ca_cert, timeout=30)
    # Login and attach token
    resp = client.post("/api/auth/login", json={
        "username": os.environ["PRAXOVA_ADMIN_USER"],
        "password": os.environ["PRAXOVA_ADMIN_PASSWORD"],
    })
    token = resp.json()["token"]
    client.headers["Authorization"] = f"Bearer {token}"
    return client

@pytest.fixture(scope="session")
def agent_api_key():
    return os.environ["PRAXOVA_AGENT_API_KEY"]

@pytest.fixture(scope="session")
def agent_client(portal_url, ca_cert, agent_api_key):
    """Client authenticated as the agent (API key)."""
    client = httpx.Client(base_url=portal_url, verify=ca_cert, timeout=30)
    client.headers["X-Api-Key"] = agent_api_key
    return client

@pytest.fixture(scope="session")
def tool_server_url():
    return os.environ.get("PRAXOVA_TOOL_SERVER_URL", "https://tool01.montanifarms.com:8443")
```

### Example Test Module

```python
# test_phase3_tokens.py

import pytest
import time
import base64
import json

class TestTokenIssuance:
    """Phase 3: Operation token issuance from portal."""

    def test_happy_path(self, agent_client, tool_server_url):
        resp = agent_client.post("/api/authz/operation-token", json={
            "agent_name": "helpdesk-01",
            "capability": "ad-password-reset",
            "target": "testuser",
            "target_type": "user",
            "tool_server_url": tool_server_url,
            "workflow_context": {"ticket_number": "INC0099999"},
        })
        assert resp.status_code == 200
        data = resp.json()
        assert "token" in data
        assert data["expires_in"] == 300

        # Decode and verify claims
        payload = data["token"].split(".")[1]
        payload += "=" * (4 - len(payload) % 4)  # pad base64
        claims = json.loads(base64.b64decode(payload))
        assert claims["cap"] == "ad-password-reset"
        assert claims["target"] == "testuser"
        assert claims["sub"] == "helpdesk-01"

    def test_invalid_capability(self, agent_client, tool_server_url):
        resp = agent_client.post("/api/authz/operation-token", json={
            "agent_name": "helpdesk-01",
            "capability": "nonexistent-capability",
            "target": "testuser",
            "target_type": "user",
            "tool_server_url": tool_server_url,
        })
        assert resp.status_code in (400, 404)
        assert "capability_not_found" in resp.json().get("error", "")

    def test_wrong_tool_server(self, agent_client):
        resp = agent_client.post("/api/authz/operation-token", json={
            "agent_name": "helpdesk-01",
            "capability": "ad-password-reset",
            "target": "testuser",
            "target_type": "user",
            "tool_server_url": "https://wrong-server:8443",
        })
        assert resp.status_code == 400
        assert "tool_server_mismatch" in resp.json().get("error", "")

    def test_rate_limiting(self, agent_client, tool_server_url):
        """Fire 65 requests rapidly. First ~60 should succeed, rest should 429."""
        results = []
        for i in range(65):
            resp = agent_client.post("/api/authz/operation-token", json={
                "agent_name": "helpdesk-01",
                "capability": "ad-password-reset",
                "target": f"ratelimit-user-{i}",
                "target_type": "user",
                "tool_server_url": tool_server_url,
            })
            results.append(resp.status_code)

        success_count = results.count(200)
        limited_count = results.count(429)
        assert success_count >= 55, f"Expected ~60 successes, got {success_count}"
        assert limited_count > 0, "Rate limiting never triggered"


class TestToolServerTokenValidation:
    """Phase 3: Tool server validates operation tokens."""

    @pytest.fixture
    def valid_token(self, agent_client, tool_server_url):
        resp = agent_client.post("/api/authz/operation-token", json={
            "agent_name": "helpdesk-01",
            "capability": "ad-password-reset",
            "target": "testuser",
            "target_type": "user",
            "tool_server_url": tool_server_url,
        })
        return resp.json()["token"]

    def test_no_token_rejected(self, tool_server_url, ca_cert):
        """Call tool server without token → 403."""
        client = httpx.Client(verify=ca_cert, timeout=10)
        resp = client.post(
            f"{tool_server_url}/api/v1/tools/password/reset",
            json={"username": "testuser", "new_password": "Test1234!"},
        )
        assert resp.status_code == 403

    def test_forged_token_rejected(self, tool_server_url, ca_cert):
        """Manually crafted JWT with wrong signature → 403."""
        header = base64.urlsafe_b64encode(b'{"alg":"HS256","typ":"JWT"}').rstrip(b'=').decode()
        payload = base64.urlsafe_b64encode(
            b'{"jti":"fake","iat":1740000000,"exp":9999999999,'
            b'"iss":"praxova-portal","sub":"helpdesk-01",'
            b'"cap":"ad-password-reset","target":"testuser"}'
        ).rstrip(b'=').decode()
        fake = f"{header}.{payload}.not-a-real-signature"

        client = httpx.Client(verify=ca_cert, timeout=10)
        resp = client.post(
            f"{tool_server_url}/api/v1/tools/password/reset",
            json={"username": "testuser", "new_password": "Test1234!"},
            headers={"Authorization": f"Bearer {fake}"},
        )
        assert resp.status_code == 403

    def test_health_exempt(self, tool_server_url, ca_cert):
        """Health endpoint works without token."""
        client = httpx.Client(verify=ca_cert, timeout=10)
        resp = client.get(f"{tool_server_url}/api/v1/health")
        assert resp.status_code == 200
```

### Running

```bash
cd /home/alton/Documents/lucid-it-agent

# Set test environment
export PRAXOVA_ADMIN_USER=admin
export PRAXOVA_ADMIN_PASSWORD="YourPassword1!"
export PRAXOVA_AGENT_API_KEY="prx_your_key"
export PRAXOVA_TOOL_SERVER_URL="https://tool01.montanifarms.com:8443"

# Run all E2E tests
pytest tests/e2e/ -v --tb=short

# Run just one phase
pytest tests/e2e/test_phase3_tokens.py -v

# Run with JUnit XML output (for CI)
pytest tests/e2e/ -v --junitxml=build/test-results/e2e.xml
```

---

## Layer 2: UI Tests (Playwright)

For the browser-based tests (forced password change, AD login, classification
config UI, cert dashboard), Playwright is the right tool. It's Python-native,
handles modern Blazor apps well, and can run headless.

### Why Playwright Over Chrome MCP

Chrome MCP is useful for interactive exploration — you can see what's happening
and intervene. But for repeatable automated testing, Playwright is better:

- **Deterministic**: Wait conditions, auto-retries, built-in assertions
- **Headless**: Runs in CI without a display
- **Screenshots on failure**: Automatic evidence capture
- **Parallel execution**: Multiple browser contexts
- **Network interception**: Can mock API responses for edge cases

Chrome MCP is still useful for:
- **Developing Playwright tests**: Use Chrome MCP to interactively explore the
  portal UI, find selectors, understand page flow, then codify into Playwright
- **Investigating failures**: When a Playwright test fails, use Chrome MCP to
  manually reproduce and debug
- **One-off testing**: Quick ad-hoc checks that don't warrant a test script

### Setup

```bash
pip install playwright pytest-playwright
playwright install chromium
```

### Example UI Tests

```python
# test_ui_phase2_auth.py

import pytest
from playwright.sync_api import Page, expect

PORTAL_URL = "https://localhost:5001"

@pytest.fixture(scope="session")
def browser_context(browser):
    context = browser.new_context(ignore_https_errors=True)  # Internal CA
    yield context
    context.close()

class TestForcedPasswordChange:

    def test_first_login_forces_change(self, browser_context):
        page = browser_context.new_page()
        page.goto(f"{PORTAL_URL}/login")

        # Login with default creds
        page.fill('input[name="username"]', 'admin')
        page.fill('input[name="password"]', 'admin')
        page.click('button[type="submit"]')

        # Should redirect to password change
        page.wait_for_url("**/change-password**", timeout=5000)
        expect(page).to_have_url(re.compile(r"change-password"))

    def test_password_policy_feedback(self, browser_context):
        page = browser_context.new_page()
        # ... navigate to change password page ...

        # Enter a weak password
        page.fill('input[name="new_password"]', 'short')
        page.fill('input[name="confirm_password"]', 'short')
        page.click('button[type="submit"]')

        # Should show policy violation
        expect(page.locator('.validation-error')).to_be_visible()

    def test_successful_password_change(self, browser_context):
        page = browser_context.new_page()
        # ... navigate to change password page ...

        page.fill('input[name="new_password"]', 'NewSecurePassword1!')
        page.fill('input[name="confirm_password"]', 'NewSecurePassword1!')
        page.click('button[type="submit"]')

        # Should redirect to dashboard
        page.wait_for_url("**/dashboard**", timeout=5000)


class TestRecoveryKeyDisplay:

    def test_recovery_key_shown_once(self, browser_context):
        page = browser_context.new_page()
        # ... login and change password ...

        # Recovery key should be displayed
        key_element = page.locator('[data-testid="recovery-key"]')
        expect(key_element).to_be_visible()

        # Confirm saving
        page.check('[data-testid="recovery-key-confirmed"]')
        page.click('[data-testid="recovery-key-continue"]')

        # Navigate back — key should NOT be displayed again
        page.goto(f"{PORTAL_URL}/settings/recovery-key")
        expect(page.locator('[data-testid="recovery-key"]')).not_to_be_visible()
```

### Running UI Tests

```bash
# Headless (for automation)
pytest tests/e2e/test_ui_*.py -v --headed=false

# Headed (for debugging — you can watch it)
pytest tests/e2e/test_ui_*.py -v --headed

# With video recording on failure
pytest tests/e2e/test_ui_*.py -v --video=retain-on-failure
```

---

## Layer 3: Test Orchestration Script

A single script that handles the complete test lifecycle:

```bash
#!/bin/bash
# scripts/run-e2e-tests.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
RESULTS_DIR="$PROJECT_DIR/build/test-results"

mkdir -p "$RESULTS_DIR"

echo "=== Praxova E2E Test Suite ==="
echo "Started: $(date)"
echo ""

# ── Step 1: Fresh deployment ──────────────────────────
echo "── Step 1: Fresh deployment ──"
cd "$PROJECT_DIR"

if [[ "${SKIP_DEPLOY:-}" != "true" ]]; then
    echo "Tearing down existing stack..."
    docker compose down
    docker volume rm praxova-admin-data 2>/dev/null || true

    echo "Building and starting fresh stack..."
    docker compose up -d --build

    echo "Waiting for portal to initialize..."
    for i in $(seq 1 60); do
        if curl -sf http://localhost:5000/api/health/ > /dev/null 2>&1; then
            echo "Portal is healthy after ${i}s"
            break
        fi
        sleep 1
    done

    # Capture recovery key from logs
    RECOVERY_KEY=$(docker compose logs admin-portal 2>&1 | \
        grep -oP 'Recovery key: \K[A-Za-z0-9+/=]+' || echo "NOT_FOUND")
    echo "Recovery key: $RECOVERY_KEY" > "$RESULTS_DIR/recovery-key.txt"
else
    echo "SKIP_DEPLOY=true — using existing stack"
fi

# ── Step 2: First-time setup ──────────────────────────
echo ""
echo "── Step 2: First-time setup ──"

# This could be automated with Playwright or curl calls
# For now, check if setup is needed
HEALTH=$(curl -sf http://localhost:5000/api/health/ 2>/dev/null || echo '{}')
echo "Portal health: $HEALTH"

# ── Step 3: Run API tests ─────────────────────────────
echo ""
echo "── Step 3: API tests ──"
source "$PROJECT_DIR/.venv/bin/activate"

pytest tests/e2e/ \
    -v \
    --tb=short \
    --junitxml="$RESULTS_DIR/e2e-api.xml" \
    -k "not test_ui_" \
    2>&1 | tee "$RESULTS_DIR/e2e-api.log"

API_RESULT=$?

# ── Step 4: Run UI tests ─────────────────────────────
echo ""
echo "── Step 4: UI tests ──"

pytest tests/e2e/ \
    -v \
    --tb=short \
    --junitxml="$RESULTS_DIR/e2e-ui.xml" \
    --video=retain-on-failure \
    --output="$RESULTS_DIR/playwright" \
    -k "test_ui_" \
    2>&1 | tee "$RESULTS_DIR/e2e-ui.log"

UI_RESULT=$?

# ── Step 5: Collect evidence ──────────────────────────
echo ""
echo "── Step 5: Collecting evidence ──"

# Dump container logs
docker compose logs admin-portal > "$RESULTS_DIR/portal.log" 2>&1
docker compose logs agent-helpdesk-01 > "$RESULTS_DIR/agent.log" 2>&1
docker compose logs llm > "$RESULTS_DIR/llm.log" 2>&1

# Audit log export
TOKEN=$(curl -sk https://localhost:5001/api/auth/login \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"$PRAXOVA_ADMIN_USER\",\"password\":\"$PRAXOVA_ADMIN_PASSWORD\"}" \
    | jq -r .token 2>/dev/null || echo "")

if [[ -n "$TOKEN" ]]; then
    curl -sk https://localhost:5001/api/audit \
        -H "Authorization: Bearer $TOKEN" | jq > "$RESULTS_DIR/audit-log.json"

    curl -sk https://localhost:5001/api/audit/verify \
        -H "Authorization: Bearer $TOKEN" | jq > "$RESULTS_DIR/audit-verification.json"
fi

# ── Results ───────────────────────────────────────────
echo ""
echo "=== Results ==="
echo "API tests: $([ $API_RESULT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
echo "UI tests:  $([ $UI_RESULT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
echo "Artifacts: $RESULTS_DIR/"
echo ""
echo "Finished: $(date)"

exit $((API_RESULT + UI_RESULT))
```

---

## Layer 4: Chrome MCP for Interactive Debugging

When a test fails and you need to investigate, Chrome MCP shines for interactive
exploration. Here's how to use it alongside the automated tests:

### Workflow

1. **Run automated tests** → get failure report
2. **Open Chrome MCP** → navigate to the failing scenario
3. **Interact manually** → inspect state, try variations, check console errors
4. **Fix the code** → re-run just the failing test

### Chrome MCP Use Cases

**Investigating Blazor UI issues:**
- Navigate to the portal, inspect component state
- Check for JavaScript console errors
- Watch network requests to API endpoints
- Verify Blazor SignalR connection status

**Investigating auth flow issues:**
- Step through the login flow manually
- Check cookie/token state in browser DevTools
- Verify redirect behavior
- Test AD login with different users

**Investigating visual issues:**
- Check cert dashboard rendering
- Verify recovery key display formatting
- Test responsive layout for approval queue

### Chrome MCP is NOT for:

- Repeatable regression testing (use Playwright)
- CI/CD pipeline testing (use pytest + headless Playwright)
- Performance testing (use dedicated load testing tools)

---

## Suggested Implementation Order

1. **Start with the orchestration script** (`scripts/run-e2e-tests.sh`).
   Even without automated tests, it handles deployment, log collection, and
   evidence gathering. You run it, then manually execute the test plan from the
   markdown document. Already a massive improvement.

2. **Write the API tests** (Layer 1). These cover ~75% of the test plan and
   are straightforward pytest+httpx. You could give this to Claude Code as a
   coding task — the test plan document is the spec, the conftest.py fixtures
   above are the scaffolding, each test is a function.

3. **Add Playwright UI tests** (Layer 2) for the critical UI flows: forced
   password change, recovery key display, AD login, cert dashboard. Skip
   automating tests that are easy to verify visually and hard to automate
   (e.g., "does the header show the right role badge?").

4. **Use Chrome MCP** (Layer 4) for debugging failures, not for running tests.

---

## CI Integration (Future)

When you have a CI pipeline (GitHub Actions, GitLab CI, etc.):

```yaml
# .github/workflows/e2e.yml
name: E2E Security Tests
on:
  push:
    branches: [main]
  pull_request:

jobs:
  e2e:
    runs-on: ubuntu-latest  # or self-hosted with GPU for LLM tests
    steps:
      - uses: actions/checkout@v4
      - name: Start infrastructure
        run: docker compose up -d --build
      - name: Wait for portal
        run: scripts/wait-for-portal.sh
      - name: Run E2E tests
        run: scripts/run-e2e-tests.sh
        env:
          SKIP_DEPLOY: true  # Already deployed above
          PRAXOVA_ADMIN_USER: admin
          PRAXOVA_ADMIN_PASSWORD: ${{ secrets.TEST_ADMIN_PASSWORD }}
          PRAXOVA_AGENT_API_KEY: ${{ secrets.TEST_AGENT_API_KEY }}
      - name: Upload artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: e2e-results
          path: build/test-results/
```

The GPU requirement for LLM tests (Phase 4b) means full E2E needs a self-hosted
runner with your NVIDIA hardware. API-only tests (everything except 4b) can run
on standard CI runners with the LLM tests skipped.
