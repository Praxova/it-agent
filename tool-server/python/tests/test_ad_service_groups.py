"""Tests for Active Directory service group management."""

from unittest.mock import Mock, PropertyMock, patch

import pytest

from tool_server.config import Settings
from tool_server.services.ad_service import (
    ADConnectionError,
    ADGroupNotFoundError,
    ADOperationError,
    ADPermissionDeniedError,
    ADService,
    ADUserNotFoundError,
)


@pytest.fixture
def mock_settings() -> Settings:
    """Create mock settings for testing."""
    return Settings(
        ldap_server="dc.example.com",
        ldap_port=389,
        ldap_use_ssl=False,
        ldap_base_dn="DC=example,DC=com",
        ldap_bind_user="CN=admin,DC=example,DC=com",
        ldap_bind_password="password",
        protected_groups_deny_list=["Domain Admins", "Enterprise Admins"],
    )


@pytest.fixture
def ad_service(mock_settings: Settings) -> ADService:
    """Create AD service instance with mock settings."""
    with patch("tool_server.services.ad_service.Server"):
        return ADService(mock_settings)


def create_mock_attribute(value):
    """Helper to create mock ldap3 attribute with values property."""
    mock_attr = Mock()
    mock_attr.values = value if isinstance(value, list) else [value]
    return mock_attr


class TestGroupManagement:
    """Test cases for group management operations."""

    def test_is_protected_group_true(self, ad_service: ADService) -> None:
        """Test protected group check returns True for protected groups."""
        assert ad_service.is_protected_group("Domain Admins") is True
        assert ad_service.is_protected_group("Enterprise Admins") is True

    def test_is_protected_group_false(self, ad_service: ADService) -> None:
        """Test protected group check returns False for non-protected groups."""
        assert ad_service.is_protected_group("IT-Helpdesk") is False
        assert ad_service.is_protected_group("Sales") is False

    def test_find_group_by_cn(self, ad_service: ADService) -> None:
        """Test finding group by common name."""
        mock_conn = Mock()
        mock_conn.search.return_value = True

        mock_entry = Mock()
        mock_entry.entry_dn = "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
        mock_conn.entries = [mock_entry]

        group_dn = ad_service._find_group(mock_conn, "IT-Helpdesk")

        assert group_dn == "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
        mock_conn.search.assert_called_once()

    def test_find_group_not_found(self, ad_service: ADService) -> None:
        """Test finding non-existent group raises error."""
        mock_conn = Mock()
        mock_conn.search.return_value = True
        mock_conn.entries = []

        with pytest.raises(ADGroupNotFoundError, match="Group 'NonExistent' not found"):
            ad_service._find_group(mock_conn, "NonExistent")

    @pytest.mark.asyncio
    async def test_get_group_success(self, ad_service: ADService) -> None:
        """Test getting group information successfully."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True

        # Search results for sequential calls
        search_call_count = [0]

        def search_side_effect(*args, **kwargs):
            call_num = search_call_count[0]
            search_call_count[0] += 1

            if call_num == 0:  # Find group by name
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
                mock_conn.entries = [mock_entry]
            elif call_num == 1:  # Get group details with members
                mock_entry = Mock()
                mock_entry.cn = "IT-Helpdesk"
                mock_entry.description = "IT Helpdesk Team"
                mock_entry.member = create_mock_attribute([
                    "CN=John Smith,OU=Users,DC=example,DC=com",
                    "CN=Jane Doe,OU=Users,DC=example,DC=com",
                ])
                mock_conn.entries = [mock_entry]
            elif call_num == 2:  # Resolve first member
                mock_entry = Mock()
                mock_entry.sAMAccountName = "jsmith"
                mock_conn.entries = [mock_entry]
            else:  # Resolve second member
                mock_entry = Mock()
                mock_entry.sAMAccountName = "jdoe"
                mock_conn.entries = [mock_entry]

            return True

        mock_conn.search.side_effect = search_side_effect

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.get_group(group_name="IT-Helpdesk")

        assert result["dn"] == "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
        assert result["name"] == "IT-Helpdesk"
        assert result["description"] == "IT Helpdesk Team"
        assert "jsmith" in result["members"]
        assert "jdoe" in result["members"]
        mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_get_group_not_found(self, ad_service: ADService) -> None:
        """Test getting non-existent group."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True
        mock_conn.search.return_value = True
        mock_conn.entries = []

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            with pytest.raises(ADGroupNotFoundError):
                await ad_service.get_group(group_name="NonExistent")

        mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_add_user_to_group_success(self, ad_service: ADService) -> None:
        """Test adding user to group successfully."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True
        mock_conn.modify.return_value = True
        mock_conn.result = {"description": "success"}

        search_call_count = [0]

        def search_side_effect(*args, **kwargs):
            call_num = search_call_count[0]
            search_call_count[0] += 1

            if call_num == 0:  # Find user
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=John Smith,OU=Users,DC=example,DC=com"
                mock_conn.entries = [mock_entry]
            else:  # Find group
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
                mock_conn.entries = [mock_entry]

            return True

        mock_conn.search.side_effect = search_side_effect

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.add_user_to_group(
                username="jsmith", group_name="IT-Helpdesk"
            )

        assert result["success"] is True
        assert "jsmith" in result["message"]
        assert "IT-Helpdesk" in result["message"]
        mock_conn.modify.assert_called_once()
        mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_add_user_to_group_protected(self, ad_service: ADService) -> None:
        """Test adding user to protected group is denied."""
        with pytest.raises(ADPermissionDeniedError, match="protected group"):
            await ad_service.add_user_to_group(
                username="jsmith", group_name="Domain Admins"
            )

    @pytest.mark.asyncio
    async def test_add_user_to_group_already_member(self, ad_service: ADService) -> None:
        """Test adding user who is already a member."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True
        mock_conn.modify.return_value = False
        mock_conn.result = {"description": "entry_already_exists"}

        search_call_count = [0]

        def search_side_effect(*args, **kwargs):
            call_num = search_call_count[0]
            search_call_count[0] += 1

            if call_num == 0:  # Find user
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=John Smith,OU=Users,DC=example,DC=com"
                mock_conn.entries = [mock_entry]
            else:  # Find group
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
                mock_conn.entries = [mock_entry]

            return True

        mock_conn.search.side_effect = search_side_effect

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.add_user_to_group(
                username="jsmith", group_name="IT-Helpdesk"
            )

        # Should succeed with message indicating already a member
        assert result["success"] is True
        assert "already" in result["message"].lower()
        mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_remove_user_from_group_success(self, ad_service: ADService) -> None:
        """Test removing user from group successfully."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True
        mock_conn.modify.return_value = True
        mock_conn.result = {"description": "success"}

        search_call_count = [0]

        def search_side_effect(*args, **kwargs):
            call_num = search_call_count[0]
            search_call_count[0] += 1

            if call_num == 0:  # Find user
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=John Smith,OU=Users,DC=example,DC=com"
                mock_conn.entries = [mock_entry]
            else:  # Find group
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
                mock_conn.entries = [mock_entry]

            return True

        mock_conn.search.side_effect = search_side_effect

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.remove_user_from_group(
                username="jsmith", group_name="IT-Helpdesk"
            )

        assert result["success"] is True
        assert "jsmith" in result["message"]
        mock_conn.modify.assert_called_once()
        mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_remove_user_from_group_protected(self, ad_service: ADService) -> None:
        """Test removing user from protected group is denied."""
        with pytest.raises(ADPermissionDeniedError, match="protected group"):
            await ad_service.remove_user_from_group(
                username="jsmith", group_name="Enterprise Admins"
            )

    @pytest.mark.asyncio
    async def test_remove_user_from_group_not_member(self, ad_service: ADService) -> None:
        """Test removing user who is not a member."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True
        mock_conn.modify.return_value = False
        mock_conn.result = {"description": "no_such_attribute"}

        search_call_count = [0]

        def search_side_effect(*args, **kwargs):
            call_num = search_call_count[0]
            search_call_count[0] += 1

            if call_num == 0:  # Find user
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=John Smith,OU=Users,DC=example,DC=com"
                mock_conn.entries = [mock_entry]
            else:  # Find group
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=IT-Helpdesk,OU=Groups,DC=example,DC=com"
                mock_conn.entries = [mock_entry]

            return True

        mock_conn.search.side_effect = search_side_effect

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.remove_user_from_group(
                username="jsmith", group_name="IT-Helpdesk"
            )

        # Should succeed with message indicating not a member
        assert result["success"] is True
        assert "not a member" in result["message"].lower()
        mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_get_user_groups_success(self, ad_service: ADService) -> None:
        """Test getting user's groups successfully."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True

        search_call_count = [0]

        def search_side_effect(*args, **kwargs):
            call_num = search_call_count[0]
            search_call_count[0] += 1

            if call_num == 0:  # Find user
                mock_entry = Mock()
                mock_entry.entry_dn = "CN=John Smith,OU=Users,DC=example,DC=com"
                mock_conn.entries = [mock_entry]
            else:  # Search for groups where user is a member
                mock_entry1 = Mock()
                mock_entry1.cn = "IT-Helpdesk"
                mock_entry2 = Mock()
                mock_entry2.cn = "Sales"
                mock_conn.entries = [mock_entry1, mock_entry2]

            return True

        mock_conn.search.side_effect = search_side_effect

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            groups = await ad_service.get_user_groups(username="jsmith")

        assert "IT-Helpdesk" in groups
        assert "Sales" in groups
        assert len(groups) == 2
        mock_conn.unbind.assert_called_once()
