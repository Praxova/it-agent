"""File permissions tool for Griptape Agent."""

import logging

from schema import Literal, Schema

from griptape.artifacts import BaseArtifact, ErrorArtifact, TextArtifact
from griptape.utils.decorators import activity

from agent.tools.base import BaseToolServerTool

logger = logging.getLogger(__name__)


class FilePermissionsTool(BaseToolServerTool):
    """Tool for managing file and folder permissions.

    This tool communicates with the Tool Server API to manage NTFS permissions
    on Windows file servers via WinRM.

    Example:
        tool = FilePermissionsTool()
        result = tool.grant_permission(
            username="jsmith",
            path=r"\\server\share\folder",
            permission="Read",
            ticket_number="INC0000001"
        )
    """

    @activity(
        config={
            "description": "Grant a user Read or Write permission to a file or folder. Use this when a user needs access to files or network shares.",
            "schema": Schema(
                {
                    Literal(
                        "username",
                        description="The username (sAMAccountName) to grant access to",
                    ): str,
                    Literal(
                        "path",
                        description="The UNC path to the file or folder (e.g., \\\\server\\share\\folder)",
                    ): str,
                    Literal(
                        "permission",
                        description="Permission level: 'Read' for read-only access, 'Write' for modify access",
                    ): str,
                    Literal(
                        "ticket_number",
                        description="The ticket number for audit purposes",
                    ): str,
                }
            ),
        }
    )
    async def grant_permission(self, params: dict) -> BaseArtifact:
        """Grant file/folder permission to a user.

        Args:
            params: Dictionary with:
                - username: Username (sAMAccountName) to grant access to.
                - path: UNC path to file or folder.
                - permission: 'Read' or 'Write'.
                - ticket_number: Associated ticket number.

        Returns:
            TextArtifact with success message or ErrorArtifact on failure.
        """
        username = params["values"]["username"]
        path = params["values"]["path"]
        permission = params["values"]["permission"]
        ticket_number = params["values"]["ticket_number"]

        # Validate permission level
        if permission not in ("Read", "Write"):
            return ErrorArtifact(
                f"Invalid permission level: {permission}. Must be 'Read' or 'Write'"
            )

        logger.info(f"Granting {permission} permission to {username} on {path}")

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="POST",
                endpoint="/permissions/grant",
                data={
                    "username": username,
                    "path": path,
                    "permission": permission,
                    "ticket_number": ticket_number,
                },
            )

            # Extract result
            success = response.get("success", False)
            message = response.get("message", "Permission granted")

            if success:
                result_msg = f"Successfully granted {permission} permission to {username} on {path}"
                logger.info(f"Permission grant successful: {username} on {path}")
                return TextArtifact(result_msg)
            else:
                error_msg = f"Permission grant failed: {message}"
                logger.error(error_msg)
                return ErrorArtifact(error_msg)

        except Exception as e:
            error_str = str(e)
            # Check for specific error types in the error message
            if "PathNotAllowed" in error_str or "403" in error_str:
                return ErrorArtifact(
                    f"Path '{path}' is not allowed for modification (security restriction)"
                )
            elif "PathNotFound" in error_str or "404" in error_str:
                return ErrorArtifact(f"Path '{path}' does not exist")
            else:
                return self._handle_error("grant permission", e)

    @activity(
        config={
            "description": "Revoke a user's permissions from a file or folder. Use this when a user no longer needs access.",
            "schema": Schema(
                {
                    Literal(
                        "username",
                        description="The username (sAMAccountName) to revoke access from",
                    ): str,
                    Literal(
                        "path",
                        description="The UNC path to the file or folder",
                    ): str,
                    Literal(
                        "ticket_number",
                        description="The ticket number for audit purposes",
                    ): str,
                }
            ),
        }
    )
    async def revoke_permission(self, params: dict) -> BaseArtifact:
        """Revoke file/folder permission from a user.

        Args:
            params: Dictionary with:
                - username: Username (sAMAccountName) to revoke access from.
                - path: UNC path to file or folder.
                - ticket_number: Associated ticket number.

        Returns:
            TextArtifact with success message or ErrorArtifact on failure.
        """
        username = params["values"]["username"]
        path = params["values"]["path"]
        ticket_number = params["values"]["ticket_number"]

        logger.info(f"Revoking permissions for {username} on {path}")

        try:
            # Make request to Tool Server
            response = await self._make_request(
                method="POST",
                endpoint="/permissions/revoke",
                data={
                    "username": username,
                    "path": path,
                    "ticket_number": ticket_number,
                },
            )

            # Extract result
            success = response.get("success", False)
            message = response.get("message", "Permissions revoked")

            if success:
                result_msg = f"Successfully revoked permissions for {username} on {path}"
                logger.info(f"Permission revoke successful: {username} on {path}")
                return TextArtifact(result_msg)
            else:
                error_msg = f"Permission revoke failed: {message}"
                logger.error(error_msg)
                return ErrorArtifact(error_msg)

        except Exception as e:
            error_str = str(e)
            # Check for specific error types in the error message
            if "PathNotAllowed" in error_str or "403" in error_str:
                return ErrorArtifact(f"Path '{path}' is not allowed for modification")
            elif "PathNotFound" in error_str or "404" in error_str:
                return ErrorArtifact(f"Path '{path}' does not exist")
            else:
                return self._handle_error("revoke permission", e)

    @activity(
        config={
            "description": "List current permissions on a file or folder. Use this to check who has access.",
            "schema": Schema(
                {
                    Literal(
                        "path",
                        description="The UNC path to check",
                    ): str,
                }
            ),
        }
    )
    async def list_permissions(self, params: dict) -> BaseArtifact:
        """List permissions on a path.

        Args:
            params: Dictionary with:
                - path: UNC path to check.

        Returns:
            TextArtifact with permission list or ErrorArtifact on failure.
        """
        path = params["values"]["path"]

        logger.info(f"Listing permissions for {path}")

        try:
            # Convert UNC path for URL
            # \\server\share\folder -> server/share/folder
            url_path = path.lstrip("\\").replace("\\", "/")

            # Make request to Tool Server
            response = await self._make_request(
                method="GET",
                endpoint=f"/permissions/{url_path}",
            )

            # Extract result
            permissions = response.get("permissions", [])

            if not permissions:
                return TextArtifact(f"No explicit permissions found on {path}")

            # Format permission list
            lines = [f"Permissions on {path}:", ""]
            for perm in permissions:
                lines.append(f"  {perm['user']}: {perm['rights']} ({perm['type']})")

            result_msg = "\n".join(lines)
            return TextArtifact(result_msg)

        except Exception as e:
            error_str = str(e)
            if "PathNotFound" in error_str or "404" in error_str:
                return ErrorArtifact(f"Path '{path}' does not exist")
            else:
                return self._handle_error("list permissions", e)
