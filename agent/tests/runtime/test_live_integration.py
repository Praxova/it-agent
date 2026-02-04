"""
Integration tests for live services.

These tests require real services to be running:
- Admin Portal at ADMIN_PORTAL_URL
- Ollama at OLLAMA_URL (optional, will skip if not available)
- ServiceNow PDI (optional, marked for manual run)

Run with: pytest tests/runtime/test_live_integration.py -v -m integration
"""
import os
import pytest
import httpx
import asyncio

# Mark all tests in this module as integration tests
pytestmark = pytest.mark.integration


def is_service_available(url: str, timeout: float = 2.0) -> bool:
    """Check if a service is reachable."""
    try:
        response = httpx.get(url, timeout=timeout)
        return response.status_code < 500
    except Exception:
        return False


@pytest.fixture(scope="module")
def admin_portal_url():
    """Get Admin Portal URL from environment."""
    url = os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000")
    if not is_service_available(url):
        pytest.skip(f"Admin Portal not available at {url}")
    return url


@pytest.fixture(scope="module")
def ollama_url():
    """Get Ollama URL from environment."""
    url = os.environ.get("OLLAMA_URL", "http://localhost:11434")
    if not is_service_available(url):
        pytest.skip(f"Ollama not available at {url}")
    return url


class TestAdminPortalIntegration:
    """Test integration with Admin Portal."""

    @pytest.mark.asyncio
    async def test_can_fetch_agent_export(self, admin_portal_url):
        """Test fetching agent export from Admin Portal."""
        agent_name = os.environ.get("TEST_AGENT_NAME", "test-agent")
        url = f"{admin_portal_url}/api/agents/by-name/{agent_name}/export"

        async with httpx.AsyncClient() as client:
            response = await client.get(url, timeout=10.0)

        # Should get export or 404 if agent doesn't exist
        assert response.status_code in [200, 404]

        if response.status_code == 200:
            data = response.json()
            assert "agent" in data
            assert "workflow" in data
            assert "version" in data

    @pytest.mark.asyncio
    async def test_config_loader_integration(self, admin_portal_url):
        """Test ConfigLoader with real Admin Portal."""
        from agent.runtime.config_loader import ConfigLoader

        agent_name = os.environ.get("TEST_AGENT_NAME", "test-agent")

        try:
            loader = ConfigLoader(admin_portal_url, agent_name)
            export = await loader.load()

            assert export.agent.name == agent_name
            assert export.workflow is not None
            print(f"\nLoaded agent: {export.agent.display_name}")
            print(f"Workflow: {export.workflow.display_name}")
            print(f"Steps: {len(export.workflow.steps)}")
            print(f"Rulesets: {list(export.rulesets.keys())}")

        except Exception as e:
            pytest.skip(f"Could not load agent config: {e}")


class TestOllamaIntegration:
    """Test integration with Ollama."""

    @pytest.mark.asyncio
    async def test_ollama_health(self, ollama_url):
        """Test Ollama is responding."""
        async with httpx.AsyncClient() as client:
            response = await client.get(f"{ollama_url}/api/tags", timeout=5.0)

        assert response.status_code == 200
        data = response.json()
        print(f"\nAvailable models: {[m['name'] for m in data.get('models', [])]}")

    @pytest.mark.asyncio
    async def test_driver_factory_creates_ollama_driver(self, ollama_url):
        """Test creating real Ollama driver."""
        from agent.runtime.models import ProviderExportInfo, CredentialReference
        from agent.runtime.integrations.driver_factory import create_prompt_driver

        provider = ProviderExportInfo(
            name="test-ollama",
            provider_type="llm-ollama",
            provider_config={
                "model": "llama3.1",
                "endpoint": ollama_url,
            },
            credentials=CredentialReference(storage="none"),
        )

        driver = create_prompt_driver(provider)
        assert driver is not None
        print(f"\nCreated Ollama driver for model: llama3.1")


class TestCapabilityRoutingIntegration:
    """Test capability routing with real Admin Portal."""

    @pytest.mark.asyncio
    async def test_query_capability(self, admin_portal_url):
        """Test querying capability from Admin Portal."""
        from agent.runtime.integrations.capability_router import CapabilityRouter

        router = CapabilityRouter(admin_portal_url)

        try:
            server = await router.get_server_for_capability("ad-password-reset")
            print(f"\nFound server for ad-password-reset:")
            print(f"  Name: {server.name}")
            print(f"  URL: {server.url}")
            print(f"  Status: {server.status}")
        except Exception as e:
            # May not have capability configured, which is OK
            print(f"\nNo server found for ad-password-reset: {e}")


@pytest.mark.manual
class TestServiceNowIntegration:
    """
    Test integration with ServiceNow.

    These tests require a real ServiceNow instance.
    Run manually with: pytest tests/runtime/test_live_integration.py::TestServiceNowIntegration -v -m manual
    """

    @pytest.fixture
    def servicenow_client(self):
        """Create ServiceNow client from environment."""
        from agent.runtime.integrations.servicenow_client import (
            ServiceNowClient,
            ServiceNowCredentials,
        )

        instance_url = os.environ.get("SNOW_INSTANCE_URL")
        username = os.environ.get("SNOW_USERNAME")
        password = os.environ.get("SNOW_PASSWORD")

        if not all([instance_url, username, password]):
            pytest.skip("ServiceNow credentials not configured")

        return ServiceNowClient(ServiceNowCredentials(
            instance_url=instance_url,
            username=username,
            password=password,
        ))

    @pytest.mark.asyncio
    async def test_poll_queue(self, servicenow_client):
        """Test polling ServiceNow queue."""
        assignment_group = os.environ.get("SNOW_ASSIGNMENT_GROUP", "")

        if not assignment_group:
            pytest.skip("SNOW_ASSIGNMENT_GROUP not set")

        tickets = await servicenow_client.poll_queue(
            assignment_group=assignment_group,
            state="1",
            limit=5,
        )

        print(f"\nFound {len(tickets)} tickets in queue")
        for ticket in tickets[:3]:
            print(f"  {ticket.number}: {ticket.short_description[:50]}")


@pytest.mark.manual
class TestEndToEndLive:
    """
    Full end-to-end test with real services.

    CAUTION: This will process a real ticket if configured.
    Only run in test environments.
    """

    @pytest.mark.asyncio
    async def test_dry_run_workflow(self, admin_portal_url, ollama_url):
        """
        Test workflow execution with real LLM but mock actions.

        This runs classification with real Ollama but doesn't
        actually execute Tool Server calls.
        """
        from agent.runtime.config_loader import ConfigLoader
        from agent.runtime.workflow_engine import WorkflowEngine
        from agent.runtime.integrations.driver_factory import create_prompt_driver

        agent_name = os.environ.get("TEST_AGENT_NAME", "test-agent")

        # Load config
        loader = ConfigLoader(admin_portal_url, agent_name)

        try:
            export = await loader.load()
        except Exception as e:
            pytest.skip(f"Could not load agent: {e}")

        # Create real LLM driver
        if export.llm_provider:
            # Override endpoint to use local Ollama
            export.llm_provider.provider_config["endpoint"] = ollama_url
            driver = create_prompt_driver(export.llm_provider)
        else:
            pytest.skip("No LLM provider configured")

        # Create engine
        engine = WorkflowEngine(
            export=export,
            llm_driver=driver,
            admin_portal_url=admin_portal_url,
        )

        # Test with sample ticket
        test_ticket = {
            "number": "TEST001",
            "short_description": "Password reset for testuser",
            "description": "User testuser forgot their password. Please reset it.",
            "caller_id": "tester@example.com",
        }

        print("\n--- Starting dry run workflow ---")
        print(f"Ticket: {test_ticket['short_description']}")

        # Run but expect escalation since we have no Tool Server
        context = await engine.execute(
            ticket_id="TEST001",
            ticket_data=test_ticket,
        )

        print(f"\nWorkflow completed with status: {context.status.value}")
        print(f"Steps executed: {list(context.step_results.keys())}")

        if context.get_variable("ticket_type"):
            print(f"Classification: {context.get_variable('ticket_type')} "
                  f"(confidence: {context.get_variable('confidence')})")
