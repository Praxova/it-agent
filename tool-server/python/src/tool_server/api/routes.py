"""API routes for Tool Server."""

import logging

from fastapi import APIRouter, HTTPException, status

from tool_server.api.models import (
    ErrorResponse,
    GroupInfoResponse,
    GroupMembershipRequest,
    GroupMembershipResponse,
    HealthResponse,
    PasswordResetRequest,
    PasswordResetResponse,
    UserGroupsResponse,
)
from tool_server.config import Settings
from tool_server.services import (
    ADAuthenticationError,
    ADConnectionError,
    ADGroupNotFoundError,
    ADOperationError,
    ADPermissionDeniedError,
    ADService,
    ADUserNotFoundError,
)

logger = logging.getLogger(__name__)

# Create router
router = APIRouter()

# Global AD service instance (initialized on first use)
_ad_service = None


def get_ad_service() -> ADService:
    """Get or create AD service instance.

    Returns:
        ADService instance.
    """
    global _ad_service
    if _ad_service is None:
        settings = Settings()
        _ad_service = ADService(settings)
    return _ad_service


@router.post(
    "/password/reset",
    response_model=PasswordResetResponse,
    responses={
        200: {"description": "Password reset successful"},
        400: {"model": ErrorResponse, "description": "Invalid request"},
        404: {"model": ErrorResponse, "description": "User not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Reset user password",
    description="Reset a user's password in Active Directory",
)
async def reset_password(request: PasswordResetRequest) -> PasswordResetResponse:
    """Reset a user's password in Active Directory.

    Args:
        request: Password reset request with username and new password.

    Returns:
        PasswordResetResponse with operation result.

    Raises:
        HTTPException: If operation fails.
    """
    logger.info(f"Password reset request for user: {request.username}")

    try:
        result = await get_ad_service().reset_password(
            username=request.username,
            new_password=request.new_password,
        )

        return PasswordResetResponse(**result)

    except ADUserNotFoundError as e:
        logger.warning(f"User not found: {e}")
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"error": "UserNotFound", "message": str(e)},
        )

    except (ADConnectionError, ADAuthenticationError) as e:
        logger.error(f"LDAP connection/auth error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "LDAPConnectionError",
                "message": "Failed to connect to Active Directory",
                "detail": str(e),
            },
        )

    except ADOperationError as e:
        logger.error(f"AD operation failed: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "OperationFailed",
                "message": "Failed to reset password",
                "detail": str(e),
            },
        )

    except Exception as e:
        logger.exception(f"Unexpected error during password reset: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "InternalError",
                "message": "An unexpected error occurred",
                "detail": str(e),
            },
        )


@router.get(
    "/health",
    response_model=HealthResponse,
    summary="Health check",
    description="Check service health and LDAP connectivity",
)
async def health_check() -> HealthResponse:
    """Check service health and LDAP connectivity.

    Returns:
        HealthResponse with service status.
    """
    try:
        # Test LDAP connection
        result = await get_ad_service().test_connection()

        return HealthResponse(
            status="healthy",
            ldap_connected=result["connected"],
            message=result["message"],
        )

    except (ADConnectionError, ADAuthenticationError) as e:
        logger.warning(f"Health check failed: {e}")
        return HealthResponse(
            status="unhealthy",
            ldap_connected=False,
            message=f"LDAP connection failed: {str(e)}",
        )

    except Exception as e:
        logger.exception(f"Unexpected error during health check: {e}")
        return HealthResponse(
            status="unhealthy",
            ldap_connected=False,
            message=f"Unexpected error: {str(e)}",
        )


@router.post(
    "/groups/add-member",
    response_model=GroupMembershipResponse,
    responses={
        200: {"description": "User added to group successfully"},
        400: {"model": ErrorResponse, "description": "Invalid request"},
        403: {"model": ErrorResponse, "description": "Permission denied (protected group)"},
        404: {"model": ErrorResponse, "description": "User or group not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Add user to group",
    description="Add a user to an Active Directory group",
)
async def add_user_to_group(request: GroupMembershipRequest) -> GroupMembershipResponse:
    """Add a user to an Active Directory group.

    Args:
        request: Group membership request with username, group name, and ticket number.

    Returns:
        GroupMembershipResponse with operation result.

    Raises:
        HTTPException: If operation fails.
    """
    logger.info(
        f"Add user to group request: user={request.username}, "
        f"group={request.group_name}, ticket={request.ticket_number}"
    )

    try:
        result = await get_ad_service().add_user_to_group(
            username=request.username,
            group_name=request.group_name,
        )

        return GroupMembershipResponse(
            success=result["success"],
            message=result["message"],
            username=request.username,
            group_name=request.group_name,
            ticket_number=request.ticket_number,
        )

    except ADPermissionDeniedError as e:
        logger.warning(f"Permission denied: {e}")
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"error": "PermissionDenied", "message": str(e)},
        )

    except (ADUserNotFoundError, ADGroupNotFoundError) as e:
        logger.warning(f"User or group not found: {e}")
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"error": "NotFound", "message": str(e)},
        )

    except (ADConnectionError, ADAuthenticationError) as e:
        logger.error(f"LDAP connection/auth error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "LDAPConnectionError",
                "message": "Failed to connect to Active Directory",
                "detail": str(e),
            },
        )

    except ADOperationError as e:
        logger.error(f"AD operation failed: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "OperationFailed",
                "message": "Failed to add user to group",
                "detail": str(e),
            },
        )

    except Exception as e:
        logger.exception(f"Unexpected error during add user to group: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "InternalError",
                "message": "An unexpected error occurred",
                "detail": str(e),
            },
        )


@router.post(
    "/groups/remove-member",
    response_model=GroupMembershipResponse,
    responses={
        200: {"description": "User removed from group successfully"},
        400: {"model": ErrorResponse, "description": "Invalid request"},
        403: {"model": ErrorResponse, "description": "Permission denied (protected group)"},
        404: {"model": ErrorResponse, "description": "User or group not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Remove user from group",
    description="Remove a user from an Active Directory group",
)
async def remove_user_from_group(
    request: GroupMembershipRequest,
) -> GroupMembershipResponse:
    """Remove a user from an Active Directory group.

    Args:
        request: Group membership request with username, group name, and ticket number.

    Returns:
        GroupMembershipResponse with operation result.

    Raises:
        HTTPException: If operation fails.
    """
    logger.info(
        f"Remove user from group request: user={request.username}, "
        f"group={request.group_name}, ticket={request.ticket_number}"
    )

    try:
        result = await get_ad_service().remove_user_from_group(
            username=request.username,
            group_name=request.group_name,
        )

        return GroupMembershipResponse(
            success=result["success"],
            message=result["message"],
            username=request.username,
            group_name=request.group_name,
            ticket_number=request.ticket_number,
        )

    except ADPermissionDeniedError as e:
        logger.warning(f"Permission denied: {e}")
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"error": "PermissionDenied", "message": str(e)},
        )

    except (ADUserNotFoundError, ADGroupNotFoundError) as e:
        logger.warning(f"User or group not found: {e}")
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"error": "NotFound", "message": str(e)},
        )

    except (ADConnectionError, ADAuthenticationError) as e:
        logger.error(f"LDAP connection/auth error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "LDAPConnectionError",
                "message": "Failed to connect to Active Directory",
                "detail": str(e),
            },
        )

    except ADOperationError as e:
        logger.error(f"AD operation failed: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "OperationFailed",
                "message": "Failed to remove user from group",
                "detail": str(e),
            },
        )

    except Exception as e:
        logger.exception(f"Unexpected error during remove user from group: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "InternalError",
                "message": "An unexpected error occurred",
                "detail": str(e),
            },
        )


@router.get(
    "/groups/{group_name}",
    response_model=GroupInfoResponse,
    responses={
        200: {"description": "Group information retrieved successfully"},
        404: {"model": ErrorResponse, "description": "Group not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Get group information",
    description="Get group information including members",
)
async def get_group_info(group_name: str) -> GroupInfoResponse:
    """Get group information including members.

    Args:
        group_name: Name of the group (cn or sAMAccountName).

    Returns:
        GroupInfoResponse with group details and member list.

    Raises:
        HTTPException: If operation fails.
    """
    logger.info(f"Get group info request: group={group_name}")

    try:
        result = await get_ad_service().get_group(group_name=group_name)

        return GroupInfoResponse(
            success=True,
            group_name=result["name"],
            group_dn=result["dn"],
            description=result.get("description"),
            members=result.get("members", []),
        )

    except ADGroupNotFoundError as e:
        logger.warning(f"Group not found: {e}")
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"error": "GroupNotFound", "message": str(e)},
        )

    except (ADConnectionError, ADAuthenticationError) as e:
        logger.error(f"LDAP connection/auth error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "LDAPConnectionError",
                "message": "Failed to connect to Active Directory",
                "detail": str(e),
            },
        )

    except Exception as e:
        logger.exception(f"Unexpected error during get group info: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "InternalError",
                "message": "An unexpected error occurred",
                "detail": str(e),
            },
        )


@router.get(
    "/user/{username}/groups",
    response_model=UserGroupsResponse,
    responses={
        200: {"description": "User groups retrieved successfully"},
        404: {"model": ErrorResponse, "description": "User not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Get user's groups",
    description="Get list of groups a user belongs to",
)
async def get_user_groups(username: str) -> UserGroupsResponse:
    """Get list of groups a user belongs to.

    Args:
        username: Username (sAMAccountName) to query.

    Returns:
        UserGroupsResponse with list of groups.

    Raises:
        HTTPException: If operation fails.
    """
    logger.info(f"Get user groups request: user={username}")

    try:
        groups = await get_ad_service().get_user_groups(username=username)

        return UserGroupsResponse(
            success=True,
            username=username,
            groups=groups,
        )

    except ADUserNotFoundError as e:
        logger.warning(f"User not found: {e}")
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"error": "UserNotFound", "message": str(e)},
        )

    except (ADConnectionError, ADAuthenticationError) as e:
        logger.error(f"LDAP connection/auth error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "LDAPConnectionError",
                "message": "Failed to connect to Active Directory",
                "detail": str(e),
            },
        )

    except Exception as e:
        logger.exception(f"Unexpected error during get user groups: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "error": "InternalError",
                "message": "An unexpected error occurred",
                "detail": str(e),
            },
        )
