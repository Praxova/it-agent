"""Password reset handler for ticket processing."""

import logging
import os
import secrets
import string

from connectors import Ticket

from agent.classifier import ClassificationResult, TicketType
from agent.tools.base import ToolServerConfig
from agent.tools.password_reset import PasswordResetTool

from .base import BaseHandler, HandlerResult

logger = logging.getLogger(__name__)


class PasswordResetHandler(BaseHandler):
    """Handler for password reset tickets."""

    def __init__(self, tool_server_url: str = "http://127.0.0.1:8100"):
        """Initialize the password reset handler.

        Args:
            tool_server_url: URL of the tool server (without /api/v1).
        """
        config = ToolServerConfig(
            base_url=f"{tool_server_url}/api/v1",
            client_cert_path=os.environ.get("AGENT_CLIENT_CERT"),
            client_key_path=os.environ.get("AGENT_CLIENT_KEY"),
        )
        self._tool = PasswordResetTool(tool_server_config=config)

    @property
    def handles_ticket_types(self) -> list[str]:
        """Return list of handled ticket types."""
        return [TicketType.PASSWORD_RESET]

    async def validate(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
    ) -> tuple[bool, str | None]:
        """Validate we have the required information.

        Args:
            ticket: The ticket to validate.
            classification: The classification result.

        Returns:
            Tuple of (is_valid, error_message).
        """
        if not classification.affected_user:
            return False, "Could not determine affected user from ticket"
        return True, None

    async def handle(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
    ) -> HandlerResult:
        """Reset the user's password.

        Args:
            ticket: The ticket to handle.
            classification: The classification result.

        Returns:
            HandlerResult with operation outcome.
        """
        username = classification.affected_user

        logger.info(f"Resetting password for {username} (ticket: {ticket.number})")

        # Generate a temporary password
        temp_password = self._generate_temp_password()

        try:
            # Call the tool
            result = await self._tool.reset_password(
                {"values": {"username": username, "new_password": temp_password}}
            )

            # Check result (TextArtifact = success, ErrorArtifact = failure)
            if hasattr(result, "value") and "successful" in result.value.lower():
                return HandlerResult(
                    success=True,
                    message=f"Password reset completed for {username}",
                    customer_message=self._build_customer_message(username, temp_password),
                    work_notes=f"Praxova Agent reset password for {username}. Temporary password provided to user.",
                    should_close=True,
                )
            else:
                error_msg = result.value if hasattr(result, "value") else str(result)
                return HandlerResult(
                    success=False,
                    message=f"Password reset failed: {error_msg}",
                    customer_message=None,
                    work_notes=f"Praxova Agent attempted password reset but failed: {error_msg}",
                    should_close=False,
                    error=error_msg,
                )

        except Exception as e:
            logger.exception(f"Error handling password reset for {username}")
            return HandlerResult(
                success=False,
                message=f"Exception during password reset: {e}",
                customer_message=None,
                work_notes=f"Praxova Agent encountered an error: {e}",
                should_close=False,
                error=str(e),
            )

    def _generate_temp_password(self, length: int = 16) -> str:
        """Generate a secure temporary password.

        Args:
            length: Length of the password.

        Returns:
            Randomly generated password meeting complexity requirements.
        """
        # Ensure complexity requirements: uppercase, lowercase, digit, special char
        chars = string.ascii_letters + string.digits + "!@#$%^&*"
        password = "".join(secrets.choice(chars) for _ in range(length))

        # Ensure at least one of each required character type
        if not any(c.isupper() for c in password):
            password = secrets.choice(string.ascii_uppercase) + password[1:]
        if not any(c.islower() for c in password):
            password = password[0] + secrets.choice(string.ascii_lowercase) + password[2:]
        if not any(c.isdigit() for c in password):
            password = password[:2] + secrets.choice(string.digits) + password[3:]
        if not any(c in "!@#$%^&*" for c in password):
            password = password[:3] + secrets.choice("!@#$%^&*") + password[4:]

        return password

    def _build_customer_message(self, username: str, temp_password: str) -> str:
        """Build customer-facing message.

        Args:
            username: Username that was reset.
            temp_password: Temporary password that was set.

        Returns:
            Customer-facing message.
        """
        return f"""Your password has been reset.

Your temporary password is: {temp_password}

Please log in and change your password immediately. If you have any issues, reply to this ticket.

— Praxova IT Agent"""
