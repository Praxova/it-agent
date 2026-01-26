"""API request and response models."""

from pydantic import BaseModel, Field


class PasswordResetRequest(BaseModel):
    """Request to reset a user's password.

    Attributes:
        username: The username (sAMAccountName) of the user.
        new_password: The new password to set. Must meet domain complexity requirements.
    """

    username: str = Field(
        ...,
        min_length=1,
        max_length=256,
        description="Username (sAMAccountName) of the user",
    )
    new_password: str = Field(
        ...,
        min_length=8,
        max_length=256,
        description="New password (must meet domain complexity requirements)",
    )


class PasswordResetResponse(BaseModel):
    """Response from password reset operation.

    Attributes:
        success: Whether the operation succeeded.
        message: Human-readable message describing the result.
        username: The username that was reset.
        user_dn: The distinguished name of the user (for verification).
    """

    success: bool = Field(..., description="Whether the operation succeeded")
    message: str = Field(..., description="Human-readable result message")
    username: str = Field(..., description="Username that was reset")
    user_dn: str | None = Field(
        None, description="Distinguished name of the user (for verification)"
    )


class ErrorResponse(BaseModel):
    """Error response from API.

    Attributes:
        error: Error type or category.
        message: Human-readable error message.
        detail: Optional additional error details.
    """

    error: str = Field(..., description="Error type or category")
    message: str = Field(..., description="Human-readable error message")
    detail: str | None = Field(None, description="Optional additional error details")


class HealthResponse(BaseModel):
    """Health check response.

    Attributes:
        status: Health status (healthy, degraded, unhealthy).
        ldap_connected: Whether LDAP connection is working.
        winrm_connected: Whether WinRM connection is working.
        message: Optional status message.
    """

    status: str = Field(..., description="Health status")
    ldap_connected: bool = Field(..., description="Whether LDAP connection is working")
    winrm_connected: bool = Field(True, description="Whether WinRM connection is working")
    message: str | None = Field(None, description="Optional status message")


class GroupMembershipRequest(BaseModel):
    """Request to add or remove user from group.

    Attributes:
        username: The username (sAMAccountName) of the user.
        group_name: The name of the AD group (cn or sAMAccountName).
        ticket_number: Associated ticket number for audit trail.
    """

    username: str = Field(
        ...,
        min_length=1,
        max_length=256,
        description="Username (sAMAccountName) of the user",
    )
    group_name: str = Field(
        ...,
        min_length=1,
        max_length=256,
        description="Name of the AD group (cn or sAMAccountName)",
    )
    ticket_number: str = Field(
        ...,
        min_length=1,
        max_length=64,
        description="Associated ticket number for audit",
    )


class GroupMembershipResponse(BaseModel):
    """Response from group membership operation.

    Attributes:
        success: Whether the operation succeeded.
        message: Human-readable message describing the result.
        username: The username that was added/removed.
        group_name: The group that was modified.
        ticket_number: The associated ticket number.
    """

    success: bool = Field(..., description="Whether the operation succeeded")
    message: str = Field(..., description="Human-readable result message")
    username: str = Field(..., description="Username that was added/removed")
    group_name: str = Field(..., description="Group that was modified")
    ticket_number: str = Field(..., description="Associated ticket number")


class GroupInfoResponse(BaseModel):
    """Response with group information and members.

    Attributes:
        success: Whether the operation succeeded.
        group_name: The name of the group.
        group_dn: The distinguished name of the group.
        description: Group description if available.
        members: List of member usernames (sAMAccountNames).
    """

    success: bool = Field(..., description="Whether the operation succeeded")
    group_name: str = Field(..., description="Name of the group")
    group_dn: str = Field(..., description="Distinguished name of the group")
    description: str | None = Field(None, description="Group description")
    members: list[str] = Field(default_factory=list, description="List of member usernames")


class UserGroupsResponse(BaseModel):
    """Response with list of groups user belongs to.

    Attributes:
        success: Whether the operation succeeded.
        username: The username queried.
        groups: List of group names the user belongs to.
    """

    success: bool = Field(..., description="Whether the operation succeeded")
    username: str = Field(..., description="Username that was queried")
    groups: list[str] = Field(default_factory=list, description="List of groups user belongs to")


class FilePermissionRequest(BaseModel):
    """Request to grant file permission.

    Attributes:
        username: sAMAccountName of user.
        path: UNC path to file or folder.
        permission: Permission level: Read or Write.
        ticket_number: Associated ticket number.
    """

    username: str = Field(..., description="sAMAccountName of user")
    path: str = Field(..., description="UNC path to file or folder")
    permission: str = Field(..., description="Permission level: Read or Write")
    ticket_number: str = Field(..., description="Associated ticket number")


class FilePermissionRevokeRequest(BaseModel):
    """Request to revoke file permission.

    Attributes:
        username: sAMAccountName of user.
        path: UNC path to file or folder.
        ticket_number: Associated ticket number.
    """

    username: str = Field(..., description="sAMAccountName of user")
    path: str = Field(..., description="UNC path to file or folder")
    ticket_number: str = Field(..., description="Associated ticket number")


class FilePermissionResponse(BaseModel):
    """Response from file permission operation.

    Attributes:
        success: Whether the operation succeeded.
        username: The username affected.
        path: The path affected.
        action: "granted" or "revoked".
        permission: Permission level if granted.
        message: Human-readable result message.
    """

    success: bool
    username: str
    path: str
    action: str  # "granted" or "revoked"
    permission: str | None = None
    message: str


class FilePermissionListResponse(BaseModel):
    """List of permissions on a path.

    Attributes:
        path: The path queried.
        permissions: List of permission dicts.
    """

    path: str
    permissions: list[dict]
