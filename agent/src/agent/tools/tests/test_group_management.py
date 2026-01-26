"""Tests for GroupManagementTool."""

from unittest.mock import AsyncMock, Mock, patch

import pytest
from httpx import Response

from agent.tools import GroupManagementTool
from griptape.artifacts import ErrorArtifact, TextArtifact


@pytest.fixture
def group_tool() -> GroupManagementTool:
    """Create GroupManagementTool instance for testing."""
    return GroupManagementTool()


class TestGroupManagementTool:
    """Test cases for GroupManagementTool."""

    @pytest.mark.asyncio
    async def test_add_user_to_group_success(self, group_tool: GroupManagementTool) -> None:
        """Test successfully adding user to group."""
        mock_response = {
            "success": True,
            "message": "User added successfully",
            "username": "jsmith",
            "group_name": "IT-Helpdesk",
            "ticket_number": "INC0012345",
        }

        # Mock the _make_request method
        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {
                "values": {
                    "username": "jsmith",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                }
            }

            result = await group_tool.add_user_to_group(params)

            assert isinstance(result, TextArtifact)
            assert "jsmith" in result.value
            assert "IT-Helpdesk" in result.value
            assert "INC0012345" in result.value

    @pytest.mark.asyncio
    async def test_add_user_to_group_failure(self, group_tool: GroupManagementTool) -> None:
        """Test adding user to group with failure response."""
        mock_response = {
            "success": False,
            "message": "User not found",
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {
                "values": {
                    "username": "nonexistent",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                }
            }

            result = await group_tool.add_user_to_group(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to add user to group" in result.value

    @pytest.mark.asyncio
    async def test_add_user_to_group_exception(
        self, group_tool: GroupManagementTool
    ) -> None:
        """Test adding user to group with exception."""
        with patch.object(
            group_tool,
            "_make_request",
            new_callable=AsyncMock,
            side_effect=Exception("Connection error"),
        ):
            params = {
                "values": {
                    "username": "jsmith",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                }
            }

            result = await group_tool.add_user_to_group(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to add user to group" in result.value

    @pytest.mark.asyncio
    async def test_remove_user_from_group_success(
        self, group_tool: GroupManagementTool
    ) -> None:
        """Test successfully removing user from group."""
        mock_response = {
            "success": True,
            "message": "User removed successfully",
            "username": "jsmith",
            "group_name": "IT-Helpdesk",
            "ticket_number": "INC0012345",
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {
                "values": {
                    "username": "jsmith",
                    "group_name": "IT-Helpdesk",
                    "ticket_number": "INC0012345",
                }
            }

            result = await group_tool.remove_user_from_group(params)

            assert isinstance(result, TextArtifact)
            assert "jsmith" in result.value
            assert "IT-Helpdesk" in result.value
            assert "removed" in result.value.lower()

    @pytest.mark.asyncio
    async def test_remove_user_from_group_failure(
        self, group_tool: GroupManagementTool
    ) -> None:
        """Test removing user from group with failure response."""
        mock_response = {
            "success": False,
            "message": "Protected group",
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {
                "values": {
                    "username": "jsmith",
                    "group_name": "Domain Admins",
                    "ticket_number": "INC0012345",
                }
            }

            result = await group_tool.remove_user_from_group(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to remove user from group" in result.value

    @pytest.mark.asyncio
    async def test_get_group_info_success(self, group_tool: GroupManagementTool) -> None:
        """Test successfully getting group information."""
        mock_response = {
            "success": True,
            "group_name": "IT-Helpdesk",
            "group_dn": "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com",
            "description": "IT Helpdesk Team",
            "members": ["jsmith", "jdoe", "aanderson"],
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {"group_name": "IT-Helpdesk"}}

            result = await group_tool.get_group_info(params)

            assert isinstance(result, TextArtifact)
            assert "IT-Helpdesk" in result.value
            assert "jsmith" in result.value
            assert "jdoe" in result.value
            assert "3" in result.value  # Member count

    @pytest.mark.asyncio
    async def test_get_group_info_empty_members(
        self, group_tool: GroupManagementTool
    ) -> None:
        """Test getting group information with no members."""
        mock_response = {
            "success": True,
            "group_name": "EmptyGroup",
            "group_dn": "CN=EmptyGroup,OU=Groups,DC=example,DC=com",
            "description": "Empty test group",
            "members": [],
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {"group_name": "EmptyGroup"}}

            result = await group_tool.get_group_info(params)

            assert isinstance(result, TextArtifact)
            assert "EmptyGroup" in result.value
            assert "No members" in result.value

    @pytest.mark.asyncio
    async def test_get_group_info_exception(self, group_tool: GroupManagementTool) -> None:
        """Test getting group info with exception."""
        with patch.object(
            group_tool,
            "_make_request",
            new_callable=AsyncMock,
            side_effect=Exception("Group not found"),
        ):
            params = {"values": {"group_name": "NonExistent"}}

            result = await group_tool.get_group_info(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to get group info" in result.value

    @pytest.mark.asyncio
    async def test_get_user_groups_success(self, group_tool: GroupManagementTool) -> None:
        """Test successfully getting user's groups."""
        mock_response = {
            "success": True,
            "username": "jsmith",
            "groups": ["IT-Helpdesk", "Sales", "VPN-Users"],
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {"username": "jsmith"}}

            result = await group_tool.get_user_groups(params)

            assert isinstance(result, TextArtifact)
            assert "jsmith" in result.value
            assert "IT-Helpdesk" in result.value
            assert "Sales" in result.value
            assert "VPN-Users" in result.value
            assert "3" in result.value  # Group count

    @pytest.mark.asyncio
    async def test_get_user_groups_empty(self, group_tool: GroupManagementTool) -> None:
        """Test getting groups for user with no memberships."""
        mock_response = {
            "success": True,
            "username": "newuser",
            "groups": [],
        }

        with patch.object(
            group_tool, "_make_request", new_callable=AsyncMock, return_value=mock_response
        ):
            params = {"values": {"username": "newuser"}}

            result = await group_tool.get_user_groups(params)

            assert isinstance(result, TextArtifact)
            assert "newuser" in result.value
            assert "No groups" in result.value

    @pytest.mark.asyncio
    async def test_get_user_groups_exception(self, group_tool: GroupManagementTool) -> None:
        """Test getting user groups with exception."""
        with patch.object(
            group_tool,
            "_make_request",
            new_callable=AsyncMock,
            side_effect=Exception("User not found"),
        ):
            params = {"values": {"username": "nonexistent"}}

            result = await group_tool.get_user_groups(params)

            assert isinstance(result, ErrorArtifact)
            assert "Failed to get user groups" in result.value

    def test_tool_name(self, group_tool: GroupManagementTool) -> None:
        """Test tool has correct name."""
        assert group_tool.name == "GroupManagementTool"

    def test_tool_activities(self, group_tool: GroupManagementTool) -> None:
        """Test tool has all expected activities."""
        activity_names = [activity.name for activity in group_tool.activities()]

        assert "add_user_to_group" in activity_names
        assert "remove_user_from_group" in activity_names
        assert "get_group_info" in activity_names
        assert "get_user_groups" in activity_names
