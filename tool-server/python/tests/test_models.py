"""Tests for API models."""

import pytest
from pydantic import ValidationError

from tool_server.api.models import (
    ErrorResponse,
    HealthResponse,
    PasswordResetRequest,
    PasswordResetResponse,
)


class TestPasswordResetRequest:
    """Test cases for PasswordResetRequest model."""

    def test_valid_request(self) -> None:
        """Test valid password reset request."""
        request = PasswordResetRequest(
            username="testuser", new_password="NewSecurePass123!"
        )

        assert request.username == "testuser"
        assert request.new_password == "NewSecurePass123!"

    def test_empty_username(self) -> None:
        """Test that empty username is rejected."""
        with pytest.raises(ValidationError):
            PasswordResetRequest(username="", new_password="password123")

    def test_short_password(self) -> None:
        """Test that short password is rejected."""
        with pytest.raises(ValidationError):
            PasswordResetRequest(username="testuser", new_password="short")

    def test_long_username(self) -> None:
        """Test that very long username is rejected."""
        with pytest.raises(ValidationError):
            PasswordResetRequest(username="a" * 300, new_password="password123")


class TestPasswordResetResponse:
    """Test cases for PasswordResetResponse model."""

    def test_success_response(self) -> None:
        """Test successful password reset response."""
        response = PasswordResetResponse(
            success=True,
            message="Password reset successful",
            username="testuser",
            user_dn="CN=Test User,OU=Users,DC=example,DC=com",
        )

        assert response.success is True
        assert response.message == "Password reset successful"
        assert response.username == "testuser"
        assert response.user_dn == "CN=Test User,OU=Users,DC=example,DC=com"

    def test_failure_response(self) -> None:
        """Test failed password reset response."""
        response = PasswordResetResponse(
            success=False,
            message="User not found",
            username="nonexistent",
            user_dn=None,
        )

        assert response.success is False
        assert response.message == "User not found"
        assert response.username == "nonexistent"
        assert response.user_dn is None

    def test_optional_user_dn(self) -> None:
        """Test that user_dn is optional."""
        response = PasswordResetResponse(
            success=True, message="Success", username="testuser"
        )

        assert response.user_dn is None


class TestErrorResponse:
    """Test cases for ErrorResponse model."""

    def test_error_response(self) -> None:
        """Test error response model."""
        error = ErrorResponse(
            error="UserNotFound",
            message="User 'testuser' not found",
            detail="No matching user in DC=example,DC=com",
        )

        assert error.error == "UserNotFound"
        assert error.message == "User 'testuser' not found"
        assert error.detail == "No matching user in DC=example,DC=com"

    def test_optional_detail(self) -> None:
        """Test that detail field is optional."""
        error = ErrorResponse(error="InternalError", message="An error occurred")

        assert error.detail is None


class TestHealthResponse:
    """Test cases for HealthResponse model."""

    def test_healthy_status(self) -> None:
        """Test healthy status response."""
        health = HealthResponse(
            status="healthy",
            ldap_connected=True,
            message="All systems operational",
        )

        assert health.status == "healthy"
        assert health.ldap_connected is True
        assert health.message == "All systems operational"

    def test_unhealthy_status(self) -> None:
        """Test unhealthy status response."""
        health = HealthResponse(
            status="unhealthy",
            ldap_connected=False,
            message="LDAP connection failed",
        )

        assert health.status == "unhealthy"
        assert health.ldap_connected is False
        assert health.message == "LDAP connection failed"

    def test_optional_message(self) -> None:
        """Test that message is optional."""
        health = HealthResponse(status="healthy", ldap_connected=True)

        assert health.message is None
