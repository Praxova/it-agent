"""Tests for AdminPortalClient.fetch_agent_client_cert()."""

import pytest
from unittest.mock import AsyncMock, MagicMock, patch

from agent.config.admin_client import AdminPortalClient


class TestFetchAgentClientCert:
    """Test that fetch_agent_client_cert() correctly parses the portal response."""

    @pytest.mark.asyncio
    async def test_fetch_returns_cert_and_key(self):
        """Successful response returns (cert_pem, key_pem) tuple."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.raise_for_status = MagicMock()
        mock_response.json.return_value = {
            "agentName": "test-agent",
            "certificatePem": "-----BEGIN CERTIFICATE-----\nMIIB...\n-----END CERTIFICATE-----",
            "privateKeyPem": "-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n-----END RSA PRIVATE KEY-----",
            "caCertificatePem": "-----BEGIN CERTIFICATE-----\nMIIC...\n-----END CERTIFICATE-----",
            "expiresAt": "2026-05-28T00:00:00Z",
        }

        with patch.object(AdminPortalClient, "__init__", lambda self, **kwargs: None):
            client = AdminPortalClient.__new__(AdminPortalClient)
            client.base_url = "https://admin-portal:5001"
            client.agent_name = "test-agent"
            client.api_key = "prx_test"
            client._client = AsyncMock()
            client._client.get = AsyncMock(return_value=mock_response)

            cert_pem, key_pem = await client.fetch_agent_client_cert("test-agent")

            assert cert_pem == "-----BEGIN CERTIFICATE-----\nMIIB...\n-----END CERTIFICATE-----"
            assert key_pem == "-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n-----END RSA PRIVATE KEY-----"
            client._client.get.assert_called_once_with("/api/pki/certificates/agent/test-agent")

    @pytest.mark.asyncio
    async def test_fetch_raises_on_http_error(self):
        """HTTP errors are propagated."""
        import httpx

        mock_response = MagicMock()
        mock_response.status_code = 403
        mock_response.raise_for_status.side_effect = httpx.HTTPStatusError(
            "Forbidden", request=MagicMock(), response=mock_response
        )

        with patch.object(AdminPortalClient, "__init__", lambda self, **kwargs: None):
            client = AdminPortalClient.__new__(AdminPortalClient)
            client.base_url = "https://admin-portal:5001"
            client.agent_name = "test-agent"
            client.api_key = "prx_test"
            client._client = AsyncMock()
            client._client.get = AsyncMock(return_value=mock_response)

            with pytest.raises(httpx.HTTPStatusError):
                await client.fetch_agent_client_cert("test-agent")

    @pytest.mark.asyncio
    async def test_fetch_raises_on_missing_key_in_response(self):
        """Missing privateKeyPem in response raises KeyError."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.raise_for_status = MagicMock()
        mock_response.json.return_value = {
            "agentName": "test-agent",
            "certificatePem": "-----BEGIN CERTIFICATE-----\nMIIB...\n-----END CERTIFICATE-----",
            # privateKeyPem intentionally missing
        }

        with patch.object(AdminPortalClient, "__init__", lambda self, **kwargs: None):
            client = AdminPortalClient.__new__(AdminPortalClient)
            client.base_url = "https://admin-portal:5001"
            client.agent_name = "test-agent"
            client.api_key = "prx_test"
            client._client = AsyncMock()
            client._client.get = AsyncMock(return_value=mock_response)

            with pytest.raises(KeyError):
                await client.fetch_agent_client_cert("test-agent")
