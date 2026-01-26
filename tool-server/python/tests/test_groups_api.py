"""Tests for group management API routes."""

from unittest.mock import AsyncMock, Mock, patch

import pytest
from fastapi import status
from httpx import ASGITransport, AsyncClient

from tool_server.main import app
from tool_server.services import (
    ADAuthenticationError,
    ADGroupNotFoundError,
    ADPermissionDeniedError,
    ADUserNotFoundError,
)


@pytest.fixture
async def client() -> AsyncClient:
    """Create test client."""
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as ac:
        yield ac


class TestAddUserToGroupRoute:
    """Test cases for add user to group endpoint."""

    @pytest.mark.asyncio
    async def test_add_user_to_group_success(self, client: AsyncClient) -> None:
        """Test successfully adding user to group."""
        mock_result = {
            "success": True,
            "message": "User added to group successfully",
            "username": "jsmith",
            "group_name": "IT-Helpdesk",
        }

        mock_ad_service = Mock()
        mock_ad_service.add_user_to_group = AsyncMock(return_value=mock_result)

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.post(
                "/api/v1/groups/add-member",
                json={
                    "username": "jsmith",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                },
            )

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["username"] == "jsmith"
            assert data["group_name"] == "IT-Helpdesk"
            assert data["ticket_number"] == "INC0012345"

    @pytest.mark.asyncio
    async def test_add_user_to_group_protected(self, client: AsyncClient) -> None:
        """Test adding user to protected group is denied."""
        mock_ad_service = Mock()
        mock_ad_service.add_user_to_group = AsyncMock(
            side_effect=ADPermissionDeniedError("Cannot modify protected group")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.post(
                "/api/v1/groups/add-member",
                json={
                    "username": "jsmith",
                    "group_name": "Domain Admins",
                    "ticket_number": "INC0012345",
                },
            )

            assert response.status_code == status.HTTP_403_FORBIDDEN
            data = response.json()
            assert "PermissionDenied" in str(data)

    @pytest.mark.asyncio
    async def test_add_user_to_group_user_not_found(self, client: AsyncClient) -> None:
        """Test adding non-existent user to group."""
        mock_ad_service = Mock()
        mock_ad_service.add_user_to_group = AsyncMock(
            side_effect=ADUserNotFoundError("User not found")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.post(
                "/api/v1/groups/add-member",
                json={
                    "username": "nonexistent",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                },
            )

            assert response.status_code == status.HTTP_404_NOT_FOUND
            data = response.json()
            assert "NotFound" in str(data)

    @pytest.mark.asyncio
    async def test_add_user_to_group_group_not_found(self, client: AsyncClient) -> None:
        """Test adding user to non-existent group."""
        mock_ad_service = Mock()
        mock_ad_service.add_user_to_group = AsyncMock(
            side_effect=ADGroupNotFoundError("Group not found")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.post(
                "/api/v1/groups/add-member",
                json={
                    "username": "jsmith",
                    "group_name": "NonExistent",
                    "ticket_number": "INC0012345",
                },
            )

            assert response.status_code == status.HTTP_404_NOT_FOUND
            data = response.json()
            assert "NotFound" in str(data)


class TestRemoveUserFromGroupRoute:
    """Test cases for remove user from group endpoint."""

    @pytest.mark.asyncio
    async def test_remove_user_from_group_success(self, client: AsyncClient) -> None:
        """Test successfully removing user from group."""
        mock_result = {
            "success": True,
            "message": "User removed from group successfully",
            "username": "jsmith",
            "group_name": "IT-Helpdesk",
        }

        mock_ad_service = Mock()
        mock_ad_service.remove_user_from_group = AsyncMock(return_value=mock_result)

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.post(
                "/api/v1/groups/remove-member",
                json={
                    "username": "jsmith",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                },
            )

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["username"] == "jsmith"
            assert data["group_name"] == "IT-Helpdesk"
            assert data["ticket_number"] == "INC0012345"

    @pytest.mark.asyncio
    async def test_remove_user_from_group_protected(self, client: AsyncClient) -> None:
        """Test removing user from protected group is denied."""
        mock_ad_service = Mock()
        mock_ad_service.remove_user_from_group = AsyncMock(
            side_effect=ADPermissionDeniedError("Cannot modify protected group")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.post(
                "/api/v1/groups/remove-member",
                json={
                    "username": "jsmith",
                    "group_name": "Enterprise Admins",
                    "ticket_number": "INC0012345",
                },
            )

            assert response.status_code == status.HTTP_403_FORBIDDEN
            data = response.json()
            assert "PermissionDenied" in str(data)


class TestGetGroupInfoRoute:
    """Test cases for get group info endpoint."""

    @pytest.mark.asyncio
    async def test_get_group_info_success(self, client: AsyncClient) -> None:
        """Test successfully getting group information."""
        mock_result = {
            "name": "IT-Helpdesk",
            "dn": "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com",
            "description": "IT Helpdesk Team",
            "members": ["jsmith", "jdoe", "aanderson"],
        }

        mock_ad_service = Mock()
        mock_ad_service.get_group = AsyncMock(return_value=mock_result)

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/groups/IT-Helpdesk")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["group_name"] == "IT-Helpdesk"
            assert data["description"] == "IT Helpdesk Team"
            assert len(data["members"]) == 3
            assert "jsmith" in data["members"]

    @pytest.mark.asyncio
    async def test_get_group_info_not_found(self, client: AsyncClient) -> None:
        """Test getting non-existent group info."""
        mock_ad_service = Mock()
        mock_ad_service.get_group = AsyncMock(
            side_effect=ADGroupNotFoundError("Group not found")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/groups/NonExistent")

            assert response.status_code == status.HTTP_404_NOT_FOUND
            data = response.json()
            assert "GroupNotFound" in str(data)

    @pytest.mark.asyncio
    async def test_get_group_info_empty_members(self, client: AsyncClient) -> None:
        """Test getting group with no members."""
        mock_result = {
            "name": "EmptyGroup",
            "dn": "CN=EmptyGroup,OU=Groups,DC=example,DC=com",
            "description": None,
            "members": [],
        }

        mock_ad_service = Mock()
        mock_ad_service.get_group = AsyncMock(return_value=mock_result)

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/groups/EmptyGroup")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["group_name"] == "EmptyGroup"
            assert data["members"] == []


class TestGetUserGroupsRoute:
    """Test cases for get user groups endpoint."""

    @pytest.mark.asyncio
    async def test_get_user_groups_success(self, client: AsyncClient) -> None:
        """Test successfully getting user's groups."""
        mock_groups = ["IT-Helpdesk", "Sales", "VPN-Users"]

        mock_ad_service = Mock()
        mock_ad_service.get_user_groups = AsyncMock(return_value=mock_groups)

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/user/jsmith/groups")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["username"] == "jsmith"
            assert len(data["groups"]) == 3
            assert "IT-Helpdesk" in data["groups"]
            assert "Sales" in data["groups"]

    @pytest.mark.asyncio
    async def test_get_user_groups_user_not_found(self, client: AsyncClient) -> None:
        """Test getting groups for non-existent user."""
        mock_ad_service = Mock()
        mock_ad_service.get_user_groups = AsyncMock(
            side_effect=ADUserNotFoundError("User not found")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/user/nonexistent/groups")

            assert response.status_code == status.HTTP_404_NOT_FOUND
            data = response.json()
            assert "UserNotFound" in str(data)

    @pytest.mark.asyncio
    async def test_get_user_groups_empty(self, client: AsyncClient) -> None:
        """Test getting groups for user with no group memberships."""
        mock_ad_service = Mock()
        mock_ad_service.get_user_groups = AsyncMock(return_value=[])

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/user/newuser/groups")

            assert response.status_code == status.HTTP_200_OK
            data = response.json()
            assert data["success"] is True
            assert data["username"] == "newuser"
            assert data["groups"] == []

    @pytest.mark.asyncio
    async def test_get_user_groups_connection_error(self, client: AsyncClient) -> None:
        """Test handling connection error when getting user groups."""
        mock_ad_service = Mock()
        mock_ad_service.get_user_groups = AsyncMock(
            side_effect=ADAuthenticationError("Authentication failed")
        )

        with patch("tool_server.api.routes.get_ad_service", return_value=mock_ad_service):
            response = await client.get("/api/v1/user/jsmith/groups")

            assert response.status_code == status.HTTP_500_INTERNAL_SERVER_ERROR
            data = response.json()
            assert "LDAPConnectionError" in str(data)
