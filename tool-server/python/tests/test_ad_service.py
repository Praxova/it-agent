"""Tests for Active Directory service."""

from unittest.mock import MagicMock, Mock, patch

import pytest
from ldap3.core.exceptions import (
    LDAPInvalidCredentialsResult,
    LDAPSocketOpenError,
)

from tool_server.config import Settings
from tool_server.services.ad_service import (
    ADAuthenticationError,
    ADConnectionError,
    ADOperationError,
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
    )


@pytest.fixture
def ad_service(mock_settings: Settings) -> ADService:
    """Create AD service instance with mock settings."""
    with patch("tool_server.services.ad_service.Server"):
        return ADService(mock_settings)


class TestADService:
    """Test cases for ADService."""

    def test_initialization(self, mock_settings: Settings) -> None:
        """Test AD service initialization."""
        with patch("tool_server.services.ad_service.Server") as MockServer:
            service = ADService(mock_settings)

            # Should create server with correct parameters
            MockServer.assert_called_once()
            call_kwargs = MockServer.call_args.kwargs
            assert call_kwargs["host"] == "dc.example.com"
            assert call_kwargs["port"] == 389
            assert call_kwargs["use_ssl"] is False

    def test_initialization_with_ssl(self) -> None:
        """Test AD service initialization with SSL."""
        settings = Settings(
            ldap_server="dc.example.com",
            ldap_port=636,
            ldap_use_ssl=True,
            ldap_base_dn="DC=example,DC=com",
            ldap_bind_user="CN=admin,DC=example,DC=com",
            ldap_bind_password="password",
            ldap_ca_cert_file="/etc/ssl/certs/ca-bundle.crt",
        )

        with patch("tool_server.services.ad_service.Server") as MockServer:
            with patch("tool_server.services.ad_service.Tls") as MockTls:
                service = ADService(settings)

                # Should create TLS config
                MockTls.assert_called_once()

    @pytest.mark.asyncio
    async def test_get_connection_success(self, ad_service: ADService) -> None:
        """Test successful LDAP connection."""
        mock_conn = Mock()
        mock_conn.bind.return_value = True

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            conn = ad_service._get_connection()

            assert conn is not None

    @pytest.mark.asyncio
    async def test_get_connection_socket_error(self, ad_service: ADService) -> None:
        """Test connection failure due to socket error."""
        with patch(
            "tool_server.services.ad_service.Connection",
            side_effect=LDAPSocketOpenError("Connection refused"),
        ):
            with pytest.raises(ADConnectionError, match="Cannot reach LDAP server"):
                ad_service._get_connection()

    @pytest.mark.asyncio
    async def test_get_connection_auth_error(self, ad_service: ADService) -> None:
        """Test connection failure due to invalid credentials."""
        with patch(
            "tool_server.services.ad_service.Connection",
            side_effect=LDAPInvalidCredentialsResult("Invalid credentials"),
        ):
            with pytest.raises(ADAuthenticationError, match="Invalid LDAP credentials"):
                ad_service._get_connection()

    @pytest.mark.asyncio
    async def test_find_user_success(self, ad_service: ADService) -> None:
        """Test successful user search."""
        mock_conn = Mock()
        mock_conn.search.return_value = True

        # Create mock entry
        mock_entry = Mock()
        mock_entry.entry_dn = "CN=Test User,OU=Users,DC=example,DC=com"
        mock_conn.entries = [mock_entry]

        user_dn = ad_service._find_user(mock_conn, "testuser")

        assert user_dn == "CN=Test User,OU=Users,DC=example,DC=com"
        mock_conn.search.assert_called_once()

    @pytest.mark.asyncio
    async def test_find_user_not_found(self, ad_service: ADService) -> None:
        """Test user search when user doesn't exist."""
        mock_conn = Mock()
        mock_conn.search.return_value = True
        mock_conn.entries = []

        with pytest.raises(ADUserNotFoundError, match="not found in AD"):
            ad_service._find_user(mock_conn, "nonexistent")

    @pytest.mark.asyncio
    async def test_find_user_multiple_results(self, ad_service: ADService) -> None:
        """Test user search with multiple results."""
        mock_conn = Mock()
        mock_conn.search.return_value = True

        # Create multiple mock entries
        mock_entry1 = Mock()
        mock_entry1.entry_dn = "CN=User1,DC=example,DC=com"
        mock_entry2 = Mock()
        mock_entry2.entry_dn = "CN=User2,DC=example,DC=com"
        mock_conn.entries = [mock_entry1, mock_entry2]

        with pytest.raises(ADOperationError, match="Multiple users found"):
            ad_service._find_user(mock_conn, "duplicate")

    @pytest.mark.asyncio
    async def test_reset_password_success(self, ad_service: ADService) -> None:
        """Test successful password reset."""
        mock_conn = Mock()
        mock_conn.search.return_value = True
        mock_conn.modify.return_value = True

        # Mock user entry
        mock_entry = Mock()
        mock_entry.entry_dn = "CN=Test User,OU=Users,DC=example,DC=com"
        mock_conn.entries = [mock_entry]

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.reset_password("testuser", "NewPassword123!")

            assert result["success"] is True
            assert result["username"] == "testuser"
            assert result["user_dn"] == "CN=Test User,OU=Users,DC=example,DC=com"
            mock_conn.modify.assert_called_once()
            mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_reset_password_user_not_found(self, ad_service: ADService) -> None:
        """Test password reset for non-existent user."""
        mock_conn = Mock()
        mock_conn.search.return_value = True
        mock_conn.entries = []

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            with pytest.raises(ADUserNotFoundError):
                await ad_service.reset_password("nonexistent", "password123")

            mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_reset_password_modify_failed(self, ad_service: ADService) -> None:
        """Test password reset when modify operation fails."""
        mock_conn = Mock()
        mock_conn.search.return_value = True
        mock_conn.modify.return_value = False
        mock_conn.result = {"description": "Insufficient access rights"}

        # Mock user entry
        mock_entry = Mock()
        mock_entry.entry_dn = "CN=Test User,OU=Users,DC=example,DC=com"
        mock_conn.entries = [mock_entry]

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            with pytest.raises(ADOperationError, match="Failed to reset password"):
                await ad_service.reset_password("testuser", "password123")

            mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_test_connection_success(self, ad_service: ADService) -> None:
        """Test connection test success."""
        mock_conn = Mock()

        with patch("tool_server.services.ad_service.Connection", return_value=mock_conn):
            result = await ad_service.test_connection()

            assert result["connected"] is True
            assert "Successfully connected" in result["message"]
            mock_conn.unbind.assert_called_once()

    @pytest.mark.asyncio
    async def test_test_connection_failure(self, ad_service: ADService) -> None:
        """Test connection test failure."""
        with patch(
            "tool_server.services.ad_service.Connection",
            side_effect=LDAPSocketOpenError("Connection refused"),
        ):
            with pytest.raises(ADConnectionError):
                await ad_service.test_connection()
