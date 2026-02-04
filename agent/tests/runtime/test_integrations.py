"""Tests for integration modules."""
import pytest
from unittest.mock import AsyncMock, MagicMock, patch
import os

from agent.runtime.integrations.servicenow_client import (
    ServiceNowClient,
    ServiceNowCredentials,
    Ticket,
)
from agent.runtime.integrations.driver_factory import (
    create_prompt_driver,
    resolve_credential,
    DriverFactoryError,
)
from agent.runtime.integrations.capability_router import (
    CapabilityRouter,
    NoCapableServerError,
)
from agent.runtime.models import ProviderExportInfo, CredentialReference


class TestServiceNowClient:
    def test_ticket_from_dict(self):
        data = {
            "sys_id": "abc123",
            "number": "INC0001234",
            "short_description": "Password reset",
            "description": "User forgot password",
            "caller_id": {"value": "user123"},
            "state": "1",
            "assignment_group": {"value": "group123"},
        }

        ticket = Ticket.from_dict(data)

        assert ticket.sys_id == "abc123"
        assert ticket.number == "INC0001234"
        assert ticket.caller_id == "user123"

    def test_ticket_to_ticket_data(self):
        ticket = Ticket(
            sys_id="abc123",
            number="INC0001234",
            short_description="Test",
            description="Test desc",
            caller_id="user123",
            state="1",
            assignment_group="group123",
        )

        data = ticket.to_ticket_data()

        assert data["number"] == "INC0001234"
        assert data["short_description"] == "Test"


class TestDriverFactory:
    def test_resolve_credential_environment(self):
        ref = CredentialReference(
            storage="environment",
            username_key="TEST_USER",
            password_key="TEST_PASS",
        )

        with patch.dict(os.environ, {"TEST_USER": "myuser", "TEST_PASS": "mypass"}):
            creds = resolve_credential(ref)

        assert creds["username"] == "myuser"
        assert creds["password"] == "mypass"

    def test_resolve_credential_none(self):
        ref = CredentialReference(storage="none")
        creds = resolve_credential(ref)
        assert creds == {}

    def test_create_ollama_driver(self):
        provider = ProviderExportInfo(
            provider_type="llm-ollama",
            config={
                "model": "llama3.1",
                "endpoint": "http://localhost:11434",
            },
        )

        # This will fail if Ollama driver not installed, which is OK for test
        try:
            driver = create_prompt_driver(provider)
            assert driver is not None
        except (ImportError, DriverFactoryError):
            pytest.skip("Ollama driver not installed")

    def test_create_unknown_driver_raises(self):
        provider = ProviderExportInfo(
            provider_type="llm-unknown",
            config={},
        )

        with pytest.raises(DriverFactoryError) as exc_info:
            create_prompt_driver(provider)

        assert "Unsupported" in str(exc_info.value)


class TestCapabilityRouter:
    @pytest.mark.asyncio
    async def test_no_servers_raises(self):
        router = CapabilityRouter("http://localhost:5000")

        with patch("httpx.AsyncClient") as mock_client:
            mock_response = MagicMock()
            mock_response.status_code = 404
            mock_get = AsyncMock(return_value=mock_response)
            mock_client.return_value.__aenter__.return_value.get = mock_get

            with pytest.raises(NoCapableServerError):
                await router.get_server_for_capability("nonexistent")

    def test_cache_clear(self):
        router = CapabilityRouter("http://localhost:5000")
        router._cache["test"] = MagicMock()

        router.clear_cache()

        assert len(router._cache) == 0
