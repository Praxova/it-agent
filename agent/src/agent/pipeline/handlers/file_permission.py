"""File permission handler for ticket processing."""

import logging
import os

from connectors import Ticket

from agent.classifier import ClassificationResult, TicketType
from agent.tools.base import ToolServerConfig
from agent.tools.file_permissions import FilePermissionsTool

from .base import BaseHandler, HandlerResult

logger = logging.getLogger(__name__)


class FilePermissionHandler(BaseHandler):
    """Handler for file permission tickets."""

    def __init__(self, tool_server_url: str = "http://127.0.0.1:8100"):
        """Initialize the file permission handler.

        Args:
            tool_server_url: URL of the tool server (without /api/v1).
        """
        config = ToolServerConfig(
            base_url=f"{tool_server_url}/api/v1",
            client_cert_path=os.environ.get("AGENT_CLIENT_CERT"),
            client_key_path=os.environ.get("AGENT_CLIENT_KEY"),
        )
        self._tool = FilePermissionsTool(tool_server_config=config)

    @property
    def handles_ticket_types(self) -> list[str]:
        """Return list of handled ticket types."""
        return [TicketType.FILE_PERMISSION]

    async def validate(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
    ) -> tuple[bool, str | None]:
        """Validate we have required information.

        Args:
            ticket: The ticket to validate.
            classification: The classification result.

        Returns:
            Tuple of (is_valid, error_message).
        """
        if not classification.affected_user:
            return False, "Could not determine affected user from ticket"
        if not classification.target_resource:
            return False, "Could not determine target file/folder path from ticket"
        return True, None

    async def handle(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
    ) -> HandlerResult:
        """Grant file permission to user.

        Args:
            ticket: The ticket to handle.
            classification: The classification result.

        Returns:
            HandlerResult with operation outcome.
        """
        username = classification.affected_user
        path = classification.target_resource

        # Default to Read permission if not specified
        # In future, could extract permission level from ticket text
        permission = "Read"

        # Check if ticket mentions write/modify/edit
        ticket_text = f"{ticket.short_description} {ticket.description}".lower()
        if any(
            word in ticket_text
            for word in ["write", "modify", "edit", "change", "update", "create", "delete"]
        ):
            permission = "Write"

        logger.info(
            f"Granting {permission} permission to {username} on {path} "
            f"(ticket: {ticket.number})"
        )

        try:
            result = await self._tool.grant_permission(
                {
                    "values": {
                        "username": username,
                        "path": path,
                        "permission": permission,
                        "ticket_number": ticket.number,
                    }
                }
            )

            result_text = result.value if hasattr(result, "value") else str(result)

            if "success" in result_text.lower():
                return HandlerResult(
                    success=True,
                    message=f"Granted {permission} permission to {username} on {path}",
                    customer_message=self._build_customer_message(username, path, permission),
                    work_notes=f"Praxova Agent granted {permission} permission to {username} on {path}",
                    should_close=True,
                )
            else:
                return HandlerResult(
                    success=False,
                    message=f"Permission grant failed: {result_text}",
                    customer_message=None,
                    work_notes=f"Praxova Agent attempted to grant {permission} to {username} on {path} but failed: {result_text}",
                    should_close=False,
                    error=result_text,
                )

        except Exception as e:
            logger.exception(f"Error handling file permission for {username}")
            return HandlerResult(
                success=False,
                message=f"Exception during permission grant: {e}",
                customer_message=None,
                work_notes=f"Praxova Agent encountered an error: {e}",
                should_close=False,
                error=str(e),
            )

    def _build_customer_message(self, username: str, path: str, permission: str) -> str:
        """Build customer-facing message.

        Args:
            username: Username affected.
            path: Path affected.
            permission: Permission level granted.

        Returns:
            Customer-facing message.
        """
        return f"""Your file access request has been completed.

User {username} has been granted {permission} access to:
{path}

The new permissions should be active within a few minutes. You may need to log out and back in, or disconnect and reconnect any mapped drives for the changes to take effect.

If you have any issues accessing the requested location, please reply to this ticket.

— Praxova IT Agent"""
