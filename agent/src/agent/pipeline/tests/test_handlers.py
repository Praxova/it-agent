"""Tests for ticket handlers."""

from unittest.mock import AsyncMock, Mock, patch

import pytest

from griptape.artifacts import ErrorArtifact, TextArtifact

from agent.classifier import TicketType
from agent.pipeline.handlers.base import HandlerResult
from agent.pipeline.handlers.group_access import GroupAccessHandler
from agent.pipeline.handlers.password_reset import PasswordResetHandler


class TestPasswordResetHandler:
    """Tests for PasswordResetHandler."""

    @pytest.fixture
    def handler(self):
        """Create a password reset handler instance."""
        return PasswordResetHandler(tool_server_url="http://test:8100")

    def test_handles_ticket_types(self, handler):
        """Test that handler reports correct ticket types."""
        assert handler.handles_ticket_types == [TicketType.PASSWORD_RESET]

    @pytest.mark.asyncio
    async def test_validate_success(
        self, handler, sample_ticket, sample_classification_password_reset
    ):
        """Test validation succeeds with affected user."""
        is_valid, error = await handler.validate(sample_ticket, sample_classification_password_reset)

        assert is_valid is True
        assert error is None

    @pytest.mark.asyncio
    async def test_validate_missing_user(self, handler, sample_ticket):
        """Test validation fails without affected user."""
        classification = Mock(affected_user=None)

        is_valid, error = await handler.validate(sample_ticket, classification)

        assert is_valid is False
        assert "affected user" in error.lower()

    @pytest.mark.asyncio
    async def test_handle_success(
        self, handler, sample_ticket, sample_classification_password_reset
    ):
        """Test successful password reset."""
        # Mock the tool
        mock_result = TextArtifact("Password reset successful for luke.skywalker")
        handler._tool.reset_password = AsyncMock(return_value=mock_result)

        result = await handler.handle(sample_ticket, sample_classification_password_reset)

        assert isinstance(result, HandlerResult)
        assert result.success is True
        assert result.should_close is True
        assert result.customer_message is not None
        assert result.work_notes is not None
        assert "luke.skywalker" in result.message

    @pytest.mark.asyncio
    async def test_handle_failure(
        self, handler, sample_ticket, sample_classification_password_reset
    ):
        """Test password reset failure."""
        # Mock the tool to return error
        mock_result = ErrorArtifact("User not found")
        handler._tool.reset_password = AsyncMock(return_value=mock_result)

        result = await handler.handle(sample_ticket, sample_classification_password_reset)

        assert isinstance(result, HandlerResult)
        assert result.success is False
        assert result.should_close is False
        assert result.error is not None

    @pytest.mark.asyncio
    async def test_handle_exception(
        self, handler, sample_ticket, sample_classification_password_reset
    ):
        """Test exception during password reset."""
        # Mock the tool to raise exception
        handler._tool.reset_password = AsyncMock(side_effect=Exception("Connection error"))

        result = await handler.handle(sample_ticket, sample_classification_password_reset)

        assert isinstance(result, HandlerResult)
        assert result.success is False
        assert result.should_close is False
        assert "Connection error" in result.error

    def test_generate_temp_password(self, handler):
        """Test temporary password generation."""
        password = handler._generate_temp_password()

        # Should be 16 characters
        assert len(password) == 16

        # Should have at least one uppercase, lowercase, digit, and special char
        assert any(c.isupper() for c in password)
        assert any(c.islower() for c in password)
        assert any(c.isdigit() for c in password)
        assert any(c in "!@#$%^&*" for c in password)

    def test_build_customer_message(self, handler):
        """Test customer message generation."""
        message = handler._build_customer_message("testuser", "TempPass123!")

        assert "testuser" not in message  # Username not in message for security
        assert "TempPass123!" in message
        assert "Lucid IT Agent" in message


class TestGroupAccessHandler:
    """Tests for GroupAccessHandler."""

    @pytest.fixture
    def handler(self):
        """Create a group access handler instance."""
        return GroupAccessHandler(tool_server_url="http://test:8100")

    def test_handles_ticket_types(self, handler):
        """Test that handler reports correct ticket types."""
        assert TicketType.GROUP_ACCESS_ADD in handler.handles_ticket_types
        assert TicketType.GROUP_ACCESS_REMOVE in handler.handles_ticket_types

    @pytest.mark.asyncio
    async def test_validate_success(self, handler, sample_ticket, sample_classification_group_add):
        """Test validation succeeds with user and group."""
        is_valid, error = await handler.validate(sample_ticket, sample_classification_group_add)

        assert is_valid is True
        assert error is None

    @pytest.mark.asyncio
    async def test_validate_missing_user(self, handler, sample_ticket):
        """Test validation fails without affected user."""
        classification = Mock(affected_user=None, target_group="Test")

        is_valid, error = await handler.validate(sample_ticket, classification)

        assert is_valid is False
        assert "affected user" in error.lower()

    @pytest.mark.asyncio
    async def test_validate_missing_group(self, handler, sample_ticket):
        """Test validation fails without target group."""
        classification = Mock(affected_user="testuser", target_group=None)

        is_valid, error = await handler.validate(sample_ticket, classification)

        assert is_valid is False
        assert "target group" in error.lower()

    @pytest.mark.asyncio
    async def test_handle_add_success(
        self, handler, sample_ticket, sample_classification_group_add
    ):
        """Test successful user add to group."""
        # Mock the tool
        mock_result = TextArtifact("Successfully added han.solo to Contributors")
        handler._tool.add_user_to_group = AsyncMock(return_value=mock_result)

        result = await handler.handle(sample_ticket, sample_classification_group_add)

        assert isinstance(result, HandlerResult)
        assert result.success is True
        assert result.should_close is True
        assert result.customer_message is not None
        assert "han.solo" in result.message
        assert "Contributors" in result.message

    @pytest.mark.asyncio
    async def test_handle_remove_success(self, handler, sample_ticket):
        """Test successful user remove from group."""
        # Create remove classification
        classification = Mock(
            ticket_type=TicketType.GROUP_ACCESS_REMOVE,
            affected_user="han.solo",
            target_group="Contributors",
        )

        # Mock the tool
        mock_result = TextArtifact("Successfully removed han.solo from Contributors")
        handler._tool.remove_user_from_group = AsyncMock(return_value=mock_result)

        result = await handler.handle(sample_ticket, classification)

        assert isinstance(result, HandlerResult)
        assert result.success is True
        assert result.should_close is True
        assert "removed" in result.message.lower()

    @pytest.mark.asyncio
    async def test_handle_failure(self, handler, sample_ticket, sample_classification_group_add):
        """Test group access failure."""
        # Mock the tool to return error
        mock_result = ErrorArtifact("Group not found")
        handler._tool.add_user_to_group = AsyncMock(return_value=mock_result)

        result = await handler.handle(sample_ticket, sample_classification_group_add)

        assert isinstance(result, HandlerResult)
        assert result.success is False
        assert result.should_close is False
        assert result.error is not None

    @pytest.mark.asyncio
    async def test_handle_exception(self, handler, sample_ticket, sample_classification_group_add):
        """Test exception during group access."""
        # Mock the tool to raise exception
        handler._tool.add_user_to_group = AsyncMock(side_effect=Exception("Connection error"))

        result = await handler.handle(sample_ticket, sample_classification_group_add)

        assert isinstance(result, HandlerResult)
        assert result.success is False
        assert result.should_close is False
        assert "Connection error" in result.error

    def test_build_customer_message_add(self, handler):
        """Test customer message for adding to group."""
        message = handler._build_customer_message("testuser", "TestGroup", is_add=True)

        assert "testuser" in message
        assert "TestGroup" in message
        assert "added" in message.lower()
        assert "Lucid IT Agent" in message

    def test_build_customer_message_remove(self, handler):
        """Test customer message for removing from group."""
        message = handler._build_customer_message("testuser", "TestGroup", is_add=False)

        assert "testuser" in message
        assert "TestGroup" in message
        assert "removed" in message.lower()
        assert "Lucid IT Agent" in message
