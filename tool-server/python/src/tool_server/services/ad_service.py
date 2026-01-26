"""Active Directory service for LDAP operations."""

import logging
from typing import Any

from ldap3 import ALL, Connection, Server, Tls
from ldap3.core.exceptions import (
    LDAPException,
    LDAPInvalidCredentialsResult,
    LDAPSocketOpenError,
)

from tool_server.config import Settings

logger = logging.getLogger(__name__)


class ADServiceError(Exception):
    """Base exception for AD service errors."""

    pass


class ADConnectionError(ADServiceError):
    """Failed to connect to Active Directory."""

    pass


class ADAuthenticationError(ADServiceError):
    """Failed to authenticate with Active Directory."""

    pass


class ADUserNotFoundError(ADServiceError):
    """User not found in Active Directory."""

    pass


class ADOperationError(ADServiceError):
    """Failed to perform AD operation."""

    pass


class ADGroupNotFoundError(ADServiceError):
    """Group not found in Active Directory."""

    pass


class ADPermissionDeniedError(ADServiceError):
    """Operation not allowed (e.g., protected group)."""

    pass


class ADService:
    """Service for Active Directory operations using ldap3.

    Handles connection management, authentication, and password reset operations.

    Attributes:
        settings: Configuration settings for LDAP connection.
        server: LDAP server object.
    """

    def __init__(self, settings: Settings) -> None:
        """Initialize AD service.

        Args:
            settings: Configuration settings with LDAP parameters.
        """
        self.settings = settings

        # Configure TLS if using SSL
        tls_config = None
        if settings.ldap_use_ssl:
            tls_config = Tls(
                ca_certs_file=settings.ldap_ca_cert_file,
                validate=settings.ldap_validate_cert,
            )

        # Create server object
        self.server = Server(
            host=settings.ldap_server,
            port=settings.ldap_port,
            use_ssl=settings.ldap_use_ssl,
            tls=tls_config,
            get_info=ALL,
        )

        logger.info(
            f"AD Service initialized for {settings.ldap_server}:{settings.ldap_port}"
        )

    def _get_connection(self) -> Connection:
        """Create and bind LDAP connection.

        Returns:
            Bound LDAP connection.

        Raises:
            ADConnectionError: Failed to connect to LDAP server.
            ADAuthenticationError: Failed to authenticate with LDAP server.
        """
        try:
            conn = Connection(
                self.server,
                user=self.settings.ldap_bind_user,
                password=self.settings.ldap_bind_password,
                auto_bind=True,
                receive_timeout=self.settings.timeout,
            )

            logger.debug(f"Connected to LDAP as {self.settings.ldap_bind_user}")
            return conn

        except LDAPSocketOpenError as e:
            logger.error(f"Failed to connect to LDAP server: {e}")
            raise ADConnectionError(f"Cannot reach LDAP server: {e}")

        except LDAPInvalidCredentialsResult as e:
            logger.error(f"LDAP authentication failed: {e}")
            raise ADAuthenticationError(f"Invalid LDAP credentials: {e}")

        except LDAPException as e:
            logger.error(f"LDAP connection error: {e}")
            raise ADConnectionError(f"LDAP connection failed: {e}")

    def _find_user(self, conn: Connection, username: str) -> str:
        """Find user DN by username (sAMAccountName).

        Args:
            conn: Active LDAP connection.
            username: Username to search for.

        Returns:
            Distinguished name of the user.

        Raises:
            ADUserNotFoundError: User not found.
            ADOperationError: Search operation failed.
        """
        search_filter = f"(sAMAccountName={username})"

        try:
            success = conn.search(
                search_base=self.settings.ldap_base_dn,
                search_filter=search_filter,
                attributes=["distinguishedName", "sAMAccountName"],
            )

            if not success:
                logger.warning(f"User search failed: {conn.result}")
                raise ADOperationError(f"Failed to search for user: {conn.result}")

            if not conn.entries:
                logger.warning(f"User not found: {username}")
                raise ADUserNotFoundError(f"User '{username}' not found in AD")

            if len(conn.entries) > 1:
                logger.warning(f"Multiple users found for: {username}")
                raise ADOperationError(
                    f"Multiple users found with username '{username}'"
                )

            user_dn = str(conn.entries[0].entry_dn)
            logger.info(f"Found user: {username} -> {user_dn}")
            return user_dn

        except (ADUserNotFoundError, ADOperationError):
            raise
        except LDAPException as e:
            logger.error(f"LDAP search error: {e}")
            raise ADOperationError(f"Failed to search for user: {e}")

    async def reset_password(self, username: str, new_password: str) -> dict[str, Any]:
        """Reset a user's password.

        Args:
            username: Username (sAMAccountName) of the user.
            new_password: New password to set.

        Returns:
            Dictionary with result information:
                - success: bool
                - message: str
                - username: str
                - user_dn: str

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADUserNotFoundError: User not found.
            ADOperationError: Password reset operation failed.
        """
        logger.info(f"Starting password reset for user: {username}")

        conn = None
        try:
            # Get connection
            conn = self._get_connection()

            # Find user DN
            user_dn = self._find_user(conn, username)

            # Reset password using modify operation
            # Active Directory requires the unicodePwd attribute to be set
            # with the password enclosed in quotes and encoded as UTF-16-LE
            password_value = f'"{new_password}"'.encode("utf-16-le")

            success = conn.modify(
                user_dn,
                {"unicodePwd": [(3, [password_value])]},  # MODIFY_REPLACE = 3
            )

            if not success:
                error_msg = f"Failed to reset password: {conn.result}"
                logger.error(error_msg)
                raise ADOperationError(error_msg)

            logger.info(f"Successfully reset password for user: {username}")

            return {
                "success": True,
                "message": f"Password reset successful for user '{username}'",
                "username": username,
                "user_dn": user_dn,
            }

        finally:
            # Always close connection
            if conn:
                conn.unbind()
                logger.debug("LDAP connection closed")

    async def test_connection(self) -> dict[str, Any]:
        """Test LDAP connection and authentication.

        Returns:
            Dictionary with connection test result:
                - connected: bool
                - message: str

        Raises:
            ADConnectionError: Failed to connect.
            ADAuthenticationError: Failed to authenticate.
        """
        logger.info("Testing LDAP connection")

        conn = None
        try:
            conn = self._get_connection()

            return {
                "connected": True,
                "message": f"Successfully connected to {self.settings.ldap_server}",
            }

        finally:
            if conn:
                conn.unbind()

    def _find_group(self, conn: Connection, group_name: str) -> str:
        """Find group DN by name (cn or sAMAccountName).

        Args:
            conn: Active LDAP connection.
            group_name: Group name to search for.

        Returns:
            Distinguished name of the group.

        Raises:
            ADGroupNotFoundError: Group not found.
            ADOperationError: Search operation failed.
        """
        # Search by both cn and sAMAccountName
        search_filter = f"(&(objectClass=group)(|(cn={group_name})(sAMAccountName={group_name})))"

        try:
            success = conn.search(
                search_base=self.settings.ldap_base_dn,
                search_filter=search_filter,
                attributes=["distinguishedName", "cn", "sAMAccountName"],
            )

            if not success:
                logger.warning(f"Group search failed: {conn.result}")
                raise ADOperationError(f"Failed to search for group: {conn.result}")

            if not conn.entries:
                logger.warning(f"Group not found: {group_name}")
                raise ADGroupNotFoundError(f"Group '{group_name}' not found in AD")

            if len(conn.entries) > 1:
                logger.warning(f"Multiple groups found for: {group_name}")
                raise ADOperationError(
                    f"Multiple groups found with name '{group_name}'"
                )

            group_dn = str(conn.entries[0].entry_dn)
            logger.info(f"Found group: {group_name} -> {group_dn}")
            return group_dn

        except (ADGroupNotFoundError, ADOperationError):
            raise
        except LDAPException as e:
            logger.error(f"LDAP search error: {e}")
            raise ADOperationError(f"Failed to search for group: {e}")

    def is_protected_group(self, group_name: str) -> bool:
        """Check if group is in protected deny list.

        Args:
            group_name: Name of the group to check.

        Returns:
            True if group is protected, False otherwise.
        """
        return group_name in self.settings.protected_groups_deny_list

    async def get_group(self, group_name: str) -> dict[str, Any]:
        """Get group information including members.

        Args:
            group_name: Name of the group (cn or sAMAccountName).

        Returns:
            Dictionary with group information:
                - dn: str
                - name: str
                - description: str | None
                - members: list[str] (usernames)

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADGroupNotFoundError: Group not found.
            ADOperationError: Search operation failed.
        """
        logger.info(f"Getting group info for: {group_name}")

        conn = None
        try:
            conn = self._get_connection()
            group_dn = self._find_group(conn, group_name)

            # Get group details including members
            success = conn.search(
                search_base=group_dn,
                search_filter="(objectClass=group)",
                attributes=["cn", "description", "member"],
            )

            if not success or not conn.entries:
                raise ADOperationError(f"Failed to retrieve group details: {conn.result}")

            entry = conn.entries[0]

            # Extract member DNs and convert to usernames
            member_dns = entry.member.values if hasattr(entry, 'member') else []
            members = []

            for member_dn in member_dns:
                # Extract username from DN (CN=username,...)
                try:
                    # Search for the user by DN to get sAMAccountName
                    user_success = conn.search(
                        search_base=str(member_dn),
                        search_filter="(objectClass=user)",
                        attributes=["sAMAccountName"],
                    )
                    if user_success and conn.entries:
                        username = str(conn.entries[0].sAMAccountName)
                        members.append(username)
                except Exception as e:
                    logger.warning(f"Failed to resolve member DN {member_dn}: {e}")
                    continue

            group_info = {
                "dn": group_dn,
                "name": str(entry.cn),
                "description": str(entry.description) if hasattr(entry, 'description') else None,
                "members": members,
            }

            logger.info(f"Retrieved group info for {group_name}: {len(members)} members")
            return group_info

        finally:
            if conn:
                conn.unbind()

    async def add_user_to_group(self, username: str, group_name: str) -> dict[str, Any]:
        """Add user to AD group.

        Args:
            username: sAMAccountName of user.
            group_name: Name of group.

        Returns:
            Dictionary with result information:
                - success: bool
                - message: str
                - username: str
                - group_name: str

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADUserNotFoundError: User not found.
            ADGroupNotFoundError: Group not found.
            ADPermissionDeniedError: Group is protected.
            ADOperationError: Add operation failed.
        """
        logger.info(f"Adding user {username} to group {group_name}")

        # Check if group is protected
        if self.is_protected_group(group_name):
            logger.warning(f"Attempt to modify protected group: {group_name}")
            raise ADPermissionDeniedError(
                f"Cannot modify protected group '{group_name}'"
            )

        conn = None
        try:
            conn = self._get_connection()

            # Find user and group
            user_dn = self._find_user(conn, username)
            group_dn = self._find_group(conn, group_name)

            # Add user to group (modify group's member attribute)
            success = conn.modify(
                group_dn,
                {"member": [(0, [user_dn])]},  # MODIFY_ADD = 0
            )

            if not success:
                # Check if user is already a member
                if "ENTRY_ALREADY_EXISTS" in str(conn.result).upper() or "ALREADY_EXISTS" in str(conn.result).upper():
                    logger.info(f"User {username} is already a member of {group_name}")
                    return {
                        "success": True,
                        "message": f"User '{username}' is already a member of '{group_name}'",
                        "username": username,
                        "group_name": group_name,
                    }

                error_msg = f"Failed to add user to group: {conn.result}"
                logger.error(error_msg)
                raise ADOperationError(error_msg)

            logger.info(f"Successfully added {username} to {group_name}")

            return {
                "success": True,
                "message": f"User '{username}' added to group '{group_name}'",
                "username": username,
                "group_name": group_name,
            }

        finally:
            if conn:
                conn.unbind()

    async def remove_user_from_group(self, username: str, group_name: str) -> dict[str, Any]:
        """Remove user from AD group.

        Args:
            username: sAMAccountName of user.
            group_name: Name of group.

        Returns:
            Dictionary with result information:
                - success: bool
                - message: str
                - username: str
                - group_name: str

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADUserNotFoundError: User not found.
            ADGroupNotFoundError: Group not found.
            ADPermissionDeniedError: Group is protected.
            ADOperationError: Remove operation failed.
        """
        logger.info(f"Removing user {username} from group {group_name}")

        # Check if group is protected
        if self.is_protected_group(group_name):
            logger.warning(f"Attempt to modify protected group: {group_name}")
            raise ADPermissionDeniedError(
                f"Cannot modify protected group '{group_name}'"
            )

        conn = None
        try:
            conn = self._get_connection()

            # Find user and group
            user_dn = self._find_user(conn, username)
            group_dn = self._find_group(conn, group_name)

            # Remove user from group (modify group's member attribute)
            success = conn.modify(
                group_dn,
                {"member": [(1, [user_dn])]},  # MODIFY_DELETE = 1
            )

            if not success:
                # Check if user is not a member
                if "NO_SUCH_ATTRIBUTE" in str(conn.result).upper() or "UNWILLING_TO_PERFORM" in str(conn.result).upper():
                    logger.info(f"User {username} is not a member of {group_name}")
                    return {
                        "success": True,
                        "message": f"User '{username}' is not a member of '{group_name}'",
                        "username": username,
                        "group_name": group_name,
                    }

                error_msg = f"Failed to remove user from group: {conn.result}"
                logger.error(error_msg)
                raise ADOperationError(error_msg)

            logger.info(f"Successfully removed {username} from {group_name}")

            return {
                "success": True,
                "message": f"User '{username}' removed from group '{group_name}'",
                "username": username,
                "group_name": group_name,
            }

        finally:
            if conn:
                conn.unbind()

    async def get_group_members(self, group_name: str) -> list[str]:
        """Get list of usernames in a group.

        Args:
            group_name: Name of the group.

        Returns:
            List of sAMAccountNames.

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADGroupNotFoundError: Group not found.
            ADOperationError: Search operation failed.
        """
        group_info = await self.get_group(group_name)
        return group_info["members"]

    async def is_user_member_of(self, username: str, group_name: str) -> bool:
        """Check if user is member of group.

        Args:
            username: sAMAccountName of user.
            group_name: Name of group.

        Returns:
            True if user is a member, False otherwise.

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADUserNotFoundError: User not found.
            ADGroupNotFoundError: Group not found.
        """
        try:
            members = await self.get_group_members(group_name)
            return username in members
        except Exception as e:
            logger.error(f"Failed to check group membership: {e}")
            raise

    async def get_user_groups(self, username: str) -> list[str]:
        """Get list of groups a user belongs to.

        Args:
            username: sAMAccountName of user.

        Returns:
            List of group names (cn).

        Raises:
            ADConnectionError: Failed to connect to LDAP.
            ADAuthenticationError: Failed to authenticate with LDAP.
            ADUserNotFoundError: User not found.
            ADOperationError: Search operation failed.
        """
        logger.info(f"Getting groups for user: {username}")

        conn = None
        try:
            conn = self._get_connection()
            user_dn = self._find_user(conn, username)

            # Search for groups that have this user as a member
            search_filter = f"(&(objectClass=group)(member={user_dn}))"

            success = conn.search(
                search_base=self.settings.ldap_base_dn,
                search_filter=search_filter,
                attributes=["cn"],
            )

            if not success:
                logger.warning(f"Group search failed: {conn.result}")
                raise ADOperationError(f"Failed to search for user groups: {conn.result}")

            groups = [str(entry.cn) for entry in conn.entries]
            logger.info(f"Found {len(groups)} groups for user {username}")

            return groups

        finally:
            if conn:
                conn.unbind()
