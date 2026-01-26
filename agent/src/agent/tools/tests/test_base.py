"""Tests for base tool server tool."""

from unittest.mock import AsyncMock, Mock, patch

import httpx
import pytest
from griptape.artifacts import ErrorArtifact

from agent.tools.base import BaseToolServerTool, ToolServerConfig


class TestToolServerConfig:
    """Test cases for ToolServerConfig model."""

    def test_default_values(self) -> None:
        """Test default configuration values."""
        config = ToolServerConfig()

        assert config.base_url == "http://localhost:8000/api/v1"
        assert config.timeout == 30.0
        assert config.verify_ssl is True

    def test_custom_values(self) -> None:
        """Test custom configuration values."""
        config = ToolServerConfig(
            base_url="http://custom:9000/api/v2",
            timeout=60.0,
            verify_ssl=False,
        )

        assert config.base_url == "http://custom:9000/api/v2"
        assert config.timeout == 60.0
        assert config.verify_ssl is False


class TestBaseToolServerTool:
    """Test cases for BaseToolServerTool."""

    @pytest.fixture
    def tool(self) -> BaseToolServerTool:
        """Create base tool instance."""
        return BaseToolServerTool()

    @pytest.mark.asyncio
    async def test_make_request_get_success(self, tool: BaseToolServerTool) -> None:
        """Test successful GET request."""
        mock_response = Mock()
        mock_response.json.return_value = {"status": "ok"}
        mock_response.raise_for_status = Mock()

        mock_client = AsyncMock()
        mock_client.__aenter__.return_value.get.return_value = mock_response

        with patch("httpx.AsyncClient", return_value=mock_client):
            result = await tool._make_request("GET", "/health")

            assert result == {"status": "ok"}

    @pytest.mark.asyncio
    async def test_make_request_post_success(self, tool: BaseToolServerTool) -> None:
        """Test successful POST request."""
        mock_response = Mock()
        mock_response.json.return_value = {"success": True}
        mock_response.raise_for_status = Mock()

        mock_client = AsyncMock()
        mock_client.__aenter__.return_value.post.return_value = mock_response

        with patch("httpx.AsyncClient", return_value=mock_client):
            result = await tool._make_request(
                "POST", "/password/reset", data={"username": "test"}
            )

            assert result == {"success": True}

    @pytest.mark.asyncio
    async def test_make_request_http_error(self, tool: BaseToolServerTool) -> None:
        """Test request with HTTP error."""
        mock_response = Mock()
        mock_response.status_code = 404
        mock_response.text = "Not found"

        mock_client = AsyncMock()
        mock_client.__aenter__.return_value.get.return_value = mock_response
        mock_client.__aenter__.return_value.get.return_value.raise_for_status.side_effect = httpx.HTTPStatusError(
            "Not found", request=Mock(), response=mock_response
        )

        with patch("httpx.AsyncClient", return_value=mock_client):
            with pytest.raises(Exception, match="HTTP 404"):
                await tool._make_request("GET", "/nonexistent")

    @pytest.mark.asyncio
    async def test_make_request_connection_error(
        self, tool: BaseToolServerTool
    ) -> None:
        """Test request with connection error."""
        mock_client = AsyncMock()
        mock_client.__aenter__.return_value.get.side_effect = httpx.ConnectError(
            "Connection refused"
        )

        with patch("httpx.AsyncClient", return_value=mock_client):
            with pytest.raises(Exception, match="Request error"):
                await tool._make_request("GET", "/health")

    @pytest.mark.asyncio
    async def test_make_request_unsupported_method(
        self, tool: BaseToolServerTool
    ) -> None:
        """Test request with unsupported HTTP method."""
        with pytest.raises(ValueError, match="Unsupported HTTP method"):
            await tool._make_request("DELETE", "/endpoint")

    def test_handle_error(self, tool: BaseToolServerTool) -> None:
        """Test error artifact creation."""
        error = Exception("Something went wrong")
        artifact = tool._handle_error("reset password", error)

        assert isinstance(artifact, ErrorArtifact)
        assert "Failed to reset password" in str(artifact.value)
        assert "Something went wrong" in str(artifact.value)

    def test_custom_config(self) -> None:
        """Test tool with custom configuration."""
        config = ToolServerConfig(
            base_url="http://custom:9000/api/v1",
            timeout=60.0,
        )
        tool = BaseToolServerTool(tool_server_config=config)

        assert tool.tool_server_config.base_url == "http://custom:9000/api/v1"
        assert tool.tool_server_config.timeout == 60.0
