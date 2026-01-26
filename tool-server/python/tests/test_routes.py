"""Tests for API routes."""

from unittest.mock import AsyncMock, Mock, patch

import pytest
from fastapi import status
from httpx import ASGITransport, AsyncClient

from tool_server.main import app
from tool_server.services import (
    ADAuthenticationError,
    ADConnectionError,
    ADOperationError,
    ADUserNotFoundError,
)


@pytest.fixture
async def client() -> AsyncClient:
    """Create test client."""
    async with AsyncClient(
        transport=ASGITransport(app=app), base_url="http://test"
    ) as ac:
        yield ac


class TestPasswordResetRoute:
    """Test cases for password reset endpoint."""

    @pytest.mark.asyncio
    async def test_reset_password_success(self, client: AsyncClient) -> None:
        """Test successful password reset."""
        mock_result = {
            "success": True,
            "message": "Password reset successful",
            "username": "testuser",
            "user_dn": "CN=Test User,OU=Users,DC=example,DC=com",
        }

        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.reset_password = AsyncMock(return_value=mock_result)

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.post(
                "/api/v1/password/reset",
                json={"username": "testuser", "new_password": "NewPassword123!"},
            )

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["username"] == "testuser"

    @pytest.mark.asyncio
    async def test_reset_password_user_not_found(self, client: AsyncClient) -> None:
        """Test password reset for non-existent user."""
        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.reset_password = AsyncMock(
            side_effect=ADUserNotFoundError("User not found")
        )

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.post(
                "/api/v1/password/reset",
                json={"username": "nonexistent", "new_password": "password123"},
            )

            assert response.status_code == status.HTTP_404_NOT_FOUND
            data = response.json()
            assert "UserNotFound" in str(data)

    @pytest.mark.asyncio
    async def test_reset_password_connection_error(self, client: AsyncClient) -> None:
        """Test password reset with LDAP connection error."""
        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.reset_password = AsyncMock(
            side_effect=ADConnectionError("Connection failed")
        )

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.post(
                "/api/v1/password/reset",
                json={"username": "testuser", "new_password": "password123"},
            )

            assert response.status_code == status.HTTP_500_INTERNAL_SERVER_ERROR
            data = response.json()
            assert "LDAPConnectionError" in str(data)

    @pytest.mark.asyncio
    async def test_reset_password_auth_error(self, client: AsyncClient) -> None:
        """Test password reset with authentication error."""
        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.reset_password = AsyncMock(
            side_effect=ADAuthenticationError("Invalid credentials")
        )

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.post(
                "/api/v1/password/reset",
                json={"username": "testuser", "new_password": "password123"},
            )

            assert response.status_code == status.HTTP_500_INTERNAL_SERVER_ERROR

    @pytest.mark.asyncio
    async def test_reset_password_operation_error(self, client: AsyncClient) -> None:
        """Test password reset with operation error."""
        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.reset_password = AsyncMock(
            side_effect=ADOperationError("Modify failed")
        )

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.post(
                "/api/v1/password/reset",
                json={"username": "testuser", "new_password": "password123"},
            )

            assert response.status_code == status.HTTP_500_INTERNAL_SERVER_ERROR

    @pytest.mark.asyncio
    async def test_reset_password_invalid_request(self, client: AsyncClient) -> None:
        """Test password reset with invalid request data."""
        # Missing required fields
        response = await client.post(
            "/api/v1/password/reset",
            json={"username": "testuser"},  # Missing new_password
        )

        assert response.status_code == status.HTTP_422_UNPROCESSABLE_ENTITY

    @pytest.mark.asyncio
    async def test_reset_password_short_password(self, client: AsyncClient) -> None:
        """Test password reset with too short password."""
        response = await client.post(
            "/api/v1/password/reset",
            json={"username": "testuser", "new_password": "short"},
        )

        assert response.status_code == status.HTTP_422_UNPROCESSABLE_ENTITY


class TestHealthRoute:
    """Test cases for health check endpoint."""

    @pytest.mark.asyncio
    async def test_health_check_success(self, client: AsyncClient) -> None:
        """Test successful health check."""
        mock_result = {
            "connected": True,
            "message": "Successfully connected to dc.example.com",
        }

        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.test_connection = AsyncMock(return_value=mock_result)

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.get("/api/v1/health")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["status"] == "healthy"
            assert data["ldap_connected"] is True

    @pytest.mark.asyncio
    async def test_health_check_connection_failed(self, client: AsyncClient) -> None:
        """Test health check with LDAP connection failure."""
        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.test_connection = AsyncMock(
            side_effect=ADConnectionError("Connection failed")
        )

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.get("/api/v1/health")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["status"] == "unhealthy"
            assert data["ldap_connected"] is False

    @pytest.mark.asyncio
    async def test_health_check_auth_failed(self, client: AsyncClient) -> None:
        """Test health check with authentication failure."""
        # Create mock AD service
        mock_ad_service = Mock()
        mock_ad_service.test_connection = AsyncMock(
            side_effect=ADAuthenticationError("Invalid credentials")
        )

        with patch(
            "tool_server.api.routes.get_ad_service",
            return_value=mock_ad_service,
        ):
            response = await client.get("/api/v1/health")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["status"] == "unhealthy"
            assert data["ldap_connected"] is False


class TestRootRoute:
    """Test cases for root endpoint."""

    @pytest.mark.asyncio
    async def test_root_endpoint(self, client: AsyncClient) -> None:
        """Test root endpoint returns service info."""
        response = await client.get("/")

        assert response.status_code == status.HTTP_200_OK
        data = response.json()
        assert "service" in data
        assert "version" in data
        assert "docs" in data
