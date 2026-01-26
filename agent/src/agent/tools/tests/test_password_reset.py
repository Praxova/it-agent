"""Tests for password reset tool."""

from unittest.mock import AsyncMock, patch

import pytest
from griptape.artifacts import ErrorArtifact, TextArtifact

from agent.tools.password_reset import PasswordResetTool


class TestPasswordResetTool:
    """Test cases for PasswordResetTool."""

    @pytest.fixture
    def tool(self) -> PasswordResetTool:
        """Create password reset tool instance."""
        return PasswordResetTool()

    @pytest.mark.asyncio
    async def test_reset_password_success(self, tool: PasswordResetTool) -> None:
        """Test successful password reset."""
        mock_response = {
            "success": True,
            "message": "Password reset successful",
            "username": "testuser",
            "user_dn": "CN=Test User,OU=Users,DC=example,DC=com",
        }

        with patch.object(
            tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {"username": "testuser", "new_password": "NewPass123!"}}
            result = await tool.reset_password(params)

            assert isinstance(result, TextArtifact)
            assert "Password reset successful" in result.value
            assert "testuser" in result.value

    @pytest.mark.asyncio
    async def test_reset_password_failure(self, tool: PasswordResetTool) -> None:
        """Test password reset failure."""
        mock_response = {
            "success": False,
            "message": "User not found",
            "username": "nonexistent",
        }

        with patch.object(
            tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {
                "values": {"username": "nonexistent", "new_password": "password123"}
            }
            result = await tool.reset_password(params)

            assert isinstance(result, ErrorArtifact)
            assert "Password reset failed" in result.value

    @pytest.mark.asyncio
    async def test_reset_password_request_error(
        self, tool: PasswordResetTool
    ) -> None:
        """Test password reset with request error."""
        with patch.object(
            tool,
            "_make_request",
            new_callable=AsyncMock,
            side_effect=Exception("Connection failed"),
        ):
            params = {"values": {"username": "testuser", "new_password": "password123"}}
            result = await tool.reset_password(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to reset password" in result.value
            assert "Connection failed" in result.value

    @pytest.mark.asyncio
    async def test_reset_password_calls_correct_endpoint(
        self, tool: PasswordResetTool
    ) -> None:
        """Test that reset_password calls the correct API endpoint."""
        mock_response = {
            "success": True,
            "message": "Success",
            "username": "testuser",
        }

        with patch.object(
            tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ) as mock_request:
            params = {"values": {"username": "testuser", "new_password": "password123"}}
            await tool.reset_password(params)

            # Verify correct endpoint was called
            mock_request.assert_called_once_with(
                method="POST",
                endpoint="/password/reset",
                data={"username": "testuser", "new_password": "password123"},
            )

    @pytest.mark.asyncio
    async def test_check_health_success(self, tool: PasswordResetTool) -> None:
        """Test successful health check."""
        mock_response = {
            "status": "healthy",
            "ldap_connected": True,
            "message": "All systems operational",
        }

        with patch.object(
            tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {}}
            result = await tool.check_health(params)

            assert isinstance(result, TextArtifact)
            assert "healthy" in result.value
            assert "True" in result.value

    @pytest.mark.asyncio
    async def test_check_health_unhealthy(self, tool: PasswordResetTool) -> None:
        """Test health check with unhealthy status."""
        mock_response = {
            "status": "unhealthy",
            "ldap_connected": False,
            "message": "LDAP connection failed",
        }

        with patch.object(
            tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {}}
            result = await tool.check_health(params)

            assert isinstance(result, TextArtifact)
            assert "unhealthy" in result.value
            assert "False" in result.value

    @pytest.mark.asyncio
    async def test_check_health_request_error(self, tool: PasswordResetTool) -> None:
        """Test health check with request error."""
        with patch.object(
            tool,
            "_make_request",
            new_callable=AsyncMock,
            side_effect=Exception("Connection refused"),
        ):
            params = {"values": {}}
            result = await tool.check_health(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to check health" in result.value

    @pytest.mark.asyncio
    async def test_check_health_calls_correct_endpoint(
        self, tool: PasswordResetTool
    ) -> None:
        """Test that check_health calls the correct API endpoint."""
        mock_response = {"status": "healthy", "ldap_connected": True}

        with patch.object(
            tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ) as mock_request:
            params = {"values": {}}
            await tool.check_health(params)

            # Verify correct endpoint was called
            mock_request.assert_called_once_with(method="GET", endpoint="/health")
