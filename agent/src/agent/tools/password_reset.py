"""Password reset tool for Griptape Agent."""

import logging

from schema import Literal, Schema

from griptape.artifacts import BaseArtifact, ErrorArtifact, TextArtifact
from griptape.utils.decorators import activity

from agent.tools.base import BaseToolServerTool

logger = logging.getLogger(__name__)


class PasswordResetTool(BaseToolServerTool):
    """Tool for resetting user passwords in Active Directory.

    This tool communicates with the Tool Server API to perform password
    resets on behalf of the agent.

    Example:
        tool = PasswordResetTool()
        result = tool.reset_password(
            username="jsmith",
            new_password="NewSecurePass123!"
        )
    """

    @activity(
        config={
            "description": "Reset a user's password in Active Directory",
            "schema": Schema(
                {
                    Literal(
                        "username",
                        description="Username (sAMAccountName) of the user to reset",
                    ): str,
                    Literal(
                        "new_password",
                        description="New password to set (must meet domain complexity requirements)",
                    ): str,
                }
            ),
        }
    )
    async def reset_password(self, params: dict) -> BaseArtifact:
        """Reset a user's password.

        Args:
            params: Dictionary with:
                - username: Username (sAMAccountName) to reset.
                - new_password: New password to set.

        Returns:
            TextArtifact with success message or ErrorArtifact on failure.
        """
        username = params["values"]["username"]
        new_password = params["values"]["new_password"]

        logger.info(f"Resetting password for user: {username}")

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="POST",
                endpoint="/password/reset",
                data={"username": username, "new_password": new_password},
            )

            # Extract result
            success = response.get("success", False)
            message = response.get("message", "Password reset completed")
            user_dn = response.get("user_dn", "")

            if success:
                result_msg = (
                    f"✓ Password reset successful for user '{username}'\n"
                    f"Message: {message}\n"
                    f"User DN: {user_dn}"
                )
                logger.info(f"Password reset successful: {username}")
                return TextArtifact(result_msg)
            else:
                error_msg = f"Password reset failed: {message}"
                logger.error(error_msg)
                return ErrorArtifact(error_msg)

        except Exception as e:
            return self._handle_error("reset password", e)

    @activity(
        config={
            "description": "Check if the Tool Server is accessible and healthy",
            "schema": Schema({}),
        }
    )
    async def check_health(self, params: dict) -> BaseArtifact:
        """Check Tool Server health and connectivity.

        Args:
            params: Empty dictionary (no parameters needed).

        Returns:
            TextArtifact with health status or ErrorArtifact on failure.
        """
        logger.info("Checking Tool Server health")

        try:
            response = await self._make_request(method="GET", endpoint="/health")

            status = response.get("status", "unknown")
            ldap_connected = response.get("ldap_connected", False)
            message = response.get("message", "")

            result_msg = (
                f"Tool Server Status: {status}\n"
                f"LDAP Connected: {ldap_connected}\n"
                f"Message: {message}"
            )

            return TextArtifact(result_msg)

        except Exception as e:
            return self._handle_error("check health", e)
