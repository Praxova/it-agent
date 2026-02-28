"""Group access handler for ticket processing."""

import logging
import os

from connectors import Ticket


from agent.tools.base import ToolServerConfig
from agent.tools.group_management import GroupManagementTool

from .base import BaseHandler, HandlerResult

logger = logging.getLogger(__name__)


class GroupAccessHandler(BaseHandler):
    """Handler for group access add/remove tickets."""

    def __init__(self, tool_server_url: str = "http://127.0.0.1:8100"):
        """Initialize the group access handler.

        Args:
            tool_server_url: URL of the tool server (without /api/v1).
        """
        config = ToolServerConfig(
            base_url=f"{tool_server_url}/api/v1",
            client_cert_path=os.environ.get("AGENT_CLIENT_CERT"),
            client_key_path=os.environ.get("AGENT_CLIENT_KEY"),
        )
        self._tool = GroupManagementTool(tool_server_config=config)

    @property
    def handles_ticket_types(self) -> list[str]:
        """Return list of handled ticket types."""
        return [TicketType.GROUP_ACCESS_ADD, TicketType.GROUP_ACCESS_REMOVE]

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
        if not classification.target_group:
            return False, "Could not determine target group from ticket"
        return True, None

    async def handle(
        self,
        ticket: Ticket,
        classification: ClassificationResult,
    ) -> HandlerResult:
        """Add or remove user from group.

        Args:
            ticket: The ticket to handle.
            classification: The classification result.

        Returns:
            HandlerResult with operation outcome.
        """
        username = classification.affected_user
        group_name = classification.target_group
        is_add = classification.ticket_type == TicketType.GROUP_ACCESS_ADD
        action = "add" if is_add else "remove"
        past_tense = "added" if is_add else "removed"

        logger.info(
            f"{action.title()}ing {username} {'to' if is_add else 'from'} {group_name} "
            f"(ticket: {ticket.number})"
        )

        try:
            if is_add:
                result = await self._tool.add_user_to_group(
                    {
                        "values": {
                            "username": username,
                            "group_name": group_name,
                            "ticket_number": ticket.number,
                        }
                    }
                )
            else:
                result = await self._tool.remove_user_from_group(
                    {
                        "values": {
                            "username": username,
                            "group_name": group_name,
                            "ticket_number": ticket.number,
                        }
                    }
                )

            # Check result
            result_text = result.value if hasattr(result, "value") else str(result)

            if (
                "success" in result_text.lower()
                or "added" in result_text.lower()
                or "removed" in result_text.lower()
            ):
                return HandlerResult(
                    success=True,
                    message=f"Successfully {past_tense} {username} {'to' if is_add else 'from'} {group_name}",
                    customer_message=self._build_customer_message(username, group_name, is_add),
                    work_notes=f"Praxova Agent {past_tense} {username} {'to' if is_add else 'from'} group {group_name}",
                    should_close=True,
                )
            else:
                return HandlerResult(
                    success=False,
                    message=f"Group {action} failed: {result_text}",
                    customer_message=None,
                    work_notes=f"Praxova Agent attempted to {action} {username} {'to' if is_add else 'from'} {group_name} but failed: {result_text}",
                    should_close=False,
                    error=result_text,
                )

        except Exception as e:
            logger.exception(f"Error handling group access for {username}")
            return HandlerResult(
                success=False,
                message=f"Exception during group {action}: {e}",
                customer_message=None,
                work_notes=f"Praxova Agent encountered an error: {e}",
                should_close=False,
                error=str(e),
            )

    def _build_customer_message(self, username: str, group_name: str, is_add: bool) -> str:
        """Build customer-facing message.

        Args:
            username: Username affected.
            group_name: Group name.
            is_add: True for add, False for remove.

        Returns:
            Customer-facing message.
        """
        if is_add:
            return f"""Your access request has been completed.

User {username} has been added to the {group_name} group. The new permissions should be active within a few minutes. You may need to log out and back in for the changes to take effect.

If you have any issues accessing the requested resources, please reply to this ticket.

— Praxova IT Agent"""
        else:
            return f"""Your access removal request has been completed.

User {username} has been removed from the {group_name} group. The change should take effect within a few minutes.

— Praxova IT Agent"""
