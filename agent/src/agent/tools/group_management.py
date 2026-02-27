"""Group management tool for Griptape Agent."""

import logging

from schema import Literal, Schema

from griptape.artifacts import BaseArtifact, ErrorArtifact, TextArtifact
from griptape.utils.decorators import activity

from agent.tools.base import BaseToolServerTool

logger = logging.getLogger(__name__)


class GroupManagementTool(BaseToolServerTool):
    """Tool for managing Active Directory group memberships.

    This tool communicates with the Tool Server API to perform group
    management operations on behalf of the agent.

    Example:
        tool = GroupManagementTool()
        result = tool.add_user_to_group(
            username="jsmith",
            group_name="IT-Helpdesk",
            ticket_number="INC0012345"
        )
    """

    @activity(
        config={
            "description": "Add a user to an Active Directory group",
            "schema": Schema(
                {
                    Literal(
                        "username",
                        description="Username (sAMAccountName) to add to the group",
                    ): str,
                    Literal(
                        "group_name",
                        description="Name of the AD group (cn or sAMAccountName)",
                    ): str,
                    Literal(
                        "ticket_number",
                        description="Associated ticket number for audit trail",
                    ): str,
                }
            ),
        }
    )
    async def add_user_to_group(self, params: dict) -> BaseArtifact:
        """Add a user to an Active Directory group.

        Args:
            params: Dictionary with:
                - username: Username (sAMAccountName) to add.
                - group_name: Name of the group.
                - ticket_number: Associated ticket number.

        Returns:
            TextArtifact with success message or ErrorArtifact on failure.
        """
        username = params["values"]["username"]
        group_name = params["values"]["group_name"]
        ticket_number = params["values"]["ticket_number"]

        logger.info(
            f"Adding user to group: user={username}, group={group_name}, ticket={ticket_number}"
        )

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="POST",
                endpoint="/groups/add-member",
                data={
                    "username": username,
                    "group_name": group_name,
                    "ticket_number": ticket_number,
                },
                target=username,
                target_type="user",
                ticket_number=ticket_number,
            )

            # Extract result
            success = response.get("success", False)
            message = response.get("message", "User added to group")

            if success:
                result_msg = (
                    f"Successfully added user '{username}' to group '{group_name}'\n"
                    f"Message: {message}\n"
                    f"Ticket: {ticket_number}"
                )
                logger.info(f"Add user to group successful: {username} -> {group_name}")
                return TextArtifact(result_msg)
            else:
                error_msg = f"Failed to add user to group: {message}"
                logger.error(error_msg)
                return ErrorArtifact(error_msg)

        except Exception as e:
            return self._handle_error("add user to group", e)

    @activity(
        config={
            "description": "Remove a user from an Active Directory group",
            "schema": Schema(
                {
                    Literal(
                        "username",
                        description="Username (sAMAccountName) to remove from the group",
                    ): str,
                    Literal(
                        "group_name",
                        description="Name of the AD group (cn or sAMAccountName)",
                    ): str,
                    Literal(
                        "ticket_number",
                        description="Associated ticket number for audit trail",
                    ): str,
                }
            ),
        }
    )
    async def remove_user_from_group(self, params: dict) -> BaseArtifact:
        """Remove a user from an Active Directory group.

        Args:
            params: Dictionary with:
                - username: Username (sAMAccountName) to remove.
                - group_name: Name of the group.
                - ticket_number: Associated ticket number.

        Returns:
            TextArtifact with success message or ErrorArtifact on failure.
        """
        username = params["values"]["username"]
        group_name = params["values"]["group_name"]
        ticket_number = params["values"]["ticket_number"]

        logger.info(
            f"Removing user from group: user={username}, group={group_name}, "
            f"ticket={ticket_number}"
        )

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="POST",
                endpoint="/groups/remove-member",
                data={
                    "username": username,
                    "group_name": group_name,
                    "ticket_number": ticket_number,
                },
                target=username,
                target_type="user",
                ticket_number=ticket_number,
            )

            # Extract result
            success = response.get("success", False)
            message = response.get("message", "User removed from group")

            if success:
                result_msg = (
                    f"Successfully removed user '{username}' from group '{group_name}'\n"
                    f"Message: {message}\n"
                    f"Ticket: {ticket_number}"
                )
                logger.info(f"Remove user from group successful: {username} <- {group_name}")
                return TextArtifact(result_msg)
            else:
                error_msg = f"Failed to remove user from group: {message}"
                logger.error(error_msg)
                return ErrorArtifact(error_msg)

        except Exception as e:
            return self._handle_error("remove user from group", e)

    @activity(
        config={
            "description": "Get information about an Active Directory group including members",
            "schema": Schema(
                {
                    Literal(
                        "group_name",
                        description="Name of the AD group to query (cn or sAMAccountName)",
                    ): str,
                }
            ),
        }
    )
    async def get_group_info(self, params: dict) -> BaseArtifact:
        """Get information about an Active Directory group.

        Args:
            params: Dictionary with:
                - group_name: Name of the group to query.

        Returns:
            TextArtifact with group information or ErrorArtifact on failure.
        """
        group_name = params["values"]["group_name"]

        logger.info(f"Getting group info: {group_name}")

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="GET",
                endpoint=f"/groups/{group_name}",
            )

            # Extract result
            group_dn = response.get("group_dn", "")
            description = response.get("description", "No description")
            members = response.get("members", [])

            result_msg = (
                f"Group: {group_name}\n"
                f"DN: {group_dn}\n"
                f"Description: {description}\n"
                f"Members ({len(members)}):\n"
            )

            if members:
                result_msg += "\n".join(f"  - {member}" for member in members)
            else:
                result_msg += "  (No members)"

            logger.info(f"Get group info successful: {group_name}")
            return TextArtifact(result_msg)

        except Exception as e:
            return self._handle_error("get group info", e)

    @activity(
        config={
            "description": "Get list of groups a user belongs to",
            "schema": Schema(
                {
                    Literal(
                        "username",
                        description="Username (sAMAccountName) to query",
                    ): str,
                }
            ),
        }
    )
    async def get_user_groups(self, params: dict) -> BaseArtifact:
        """Get list of groups a user belongs to.

        Args:
            params: Dictionary with:
                - username: Username (sAMAccountName) to query.

        Returns:
            TextArtifact with list of groups or ErrorArtifact on failure.
        """
        username = params["values"]["username"]

        logger.info(f"Getting user groups: {username}")

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="GET",
                endpoint=f"/user/{username}/groups",
            )

            # Extract result
            groups = response.get("groups", [])

            result_msg = f"Groups for user '{username}' ({len(groups)}):\n"

            if groups:
                result_msg += "\n".join(f"  - {group}" for group in groups)
            else:
                result_msg += "  (No groups)"

            logger.info(f"Get user groups successful: {username}")
            return TextArtifact(result_msg)

        except Exception as e:
            return self._handle_error("get user groups", e)
