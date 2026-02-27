"""Tests for mTLS client certificate configuration in BaseToolServerTool."""

import pytest
from unittest.mock import AsyncMock, MagicMock, patch

from agent.tools.base import BaseToolServerTool, ToolServerConfig


class TestMtlsCertConfiguration:
    """Test that mTLS cert tuple is correctly passed to httpx."""

    @pytest.mark.asyncio
    async def test_cert_tuple_passed_when_both_paths_configured(self):
        """When both client_cert_path and client_key_path are set, httpx gets the cert tuple."""
        config = ToolServerConfig(
            base_url="https://tool01:8443/api/v1",
            client_cert_path="/tmp/praxova/agent-client.crt",
            client_key_path="/tmp/praxova/agent-client.key",
        )

        tool = BaseToolServerTool(tool_server_config=config)

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"success": True}
        mock_response.raise_for_status = MagicMock()

        with patch("httpx.AsyncClient") as mock_client_cls:
            mock_client = AsyncMock()
            mock_client.get = AsyncMock(return_value=mock_response)
            mock_client.__aenter__ = AsyncMock(return_value=mock_client)
            mock_client.__aexit__ = AsyncMock(return_value=False)
            mock_client_cls.return_value = mock_client

            result = await tool._make_request("GET", "/health")

            # Verify httpx.AsyncClient was called with the cert tuple
            mock_client_cls.assert_called_once()
            call_kwargs = mock_client_cls.call_args.kwargs
            assert call_kwargs["cert"] == (
                "/tmp/praxova/agent-client.crt",
                "/tmp/praxova/agent-client.key",
            )

    @pytest.mark.asyncio
    async def test_cert_none_when_paths_absent(self):
        """When no client cert paths are set, httpx gets cert=None."""
        config = ToolServerConfig(
            base_url="https://tool01:8443/api/v1",
        )

        tool = BaseToolServerTool(tool_server_config=config)

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"success": True}
        mock_response.raise_for_status = MagicMock()

        with patch("httpx.AsyncClient") as mock_client_cls:
            mock_client = AsyncMock()
            mock_client.get = AsyncMock(return_value=mock_response)
            mock_client.__aenter__ = AsyncMock(return_value=mock_client)
            mock_client.__aexit__ = AsyncMock(return_value=False)
            mock_client_cls.return_value = mock_client

            result = await tool._make_request("GET", "/health")

            # Verify httpx.AsyncClient was called with cert=None
            mock_client_cls.assert_called_once()
            call_kwargs = mock_client_cls.call_args.kwargs
            assert call_kwargs["cert"] is None

    @pytest.mark.asyncio
    async def test_cert_none_when_only_cert_path_set(self):
        """When only client_cert_path is set (no key), cert should be None."""
        config = ToolServerConfig(
            base_url="https://tool01:8443/api/v1",
            client_cert_path="/tmp/praxova/agent-client.crt",
        )

        tool = BaseToolServerTool(tool_server_config=config)

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"success": True}
        mock_response.raise_for_status = MagicMock()

        with patch("httpx.AsyncClient") as mock_client_cls:
            mock_client = AsyncMock()
            mock_client.get = AsyncMock(return_value=mock_response)
            mock_client.__aenter__ = AsyncMock(return_value=mock_client)
            mock_client.__aexit__ = AsyncMock(return_value=False)
            mock_client_cls.return_value = mock_client

            result = await tool._make_request("GET", "/health")

            call_kwargs = mock_client_cls.call_args.kwargs
            assert call_kwargs["cert"] is None

    @pytest.mark.asyncio
    async def test_403_raises_operation_token_error(self):
        """403 from tool server raises OperationTokenError (regression test from Phase 3)."""
        from agent.config.admin_client import OperationTokenError

        config = ToolServerConfig(
            base_url="https://tool01:8443/api/v1",
            client_cert_path="/tmp/praxova/agent-client.crt",
            client_key_path="/tmp/praxova/agent-client.key",
        )

        tool = BaseToolServerTool(tool_server_config=config)

        mock_response = MagicMock()
        mock_response.status_code = 403
        mock_response.json.return_value = {
            "error": "client_cert_required",
            "detail": "A valid Praxova agent client certificate is required",
        }

        with patch("httpx.AsyncClient") as mock_client_cls:
            mock_client = AsyncMock()
            mock_client.post = AsyncMock(return_value=mock_response)
            mock_client.__aenter__ = AsyncMock(return_value=mock_client)
            mock_client.__aexit__ = AsyncMock(return_value=False)
            mock_client_cls.return_value = mock_client

            with pytest.raises(OperationTokenError) as exc_info:
                await tool._make_request("POST", "/password/reset", data={"username": "test"})

            assert exc_info.value.error_code == "client_cert_required"
