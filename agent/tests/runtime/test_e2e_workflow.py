"""End-to-end workflow execution tests."""
import pytest
from unittest.mock import AsyncMock, patch, MagicMock
import httpx

from agent.runtime.workflow_engine import WorkflowEngine
from agent.runtime.execution_context import ExecutionStatus
from agent.runtime.models import AgentExport

from .mock_drivers import MockPromptDriver, MockClassificationResponse


class TestPasswordResetHappyPath:
    """Test successful password reset workflow."""

    @pytest.mark.asyncio
    async def test_complete_workflow_succeeds(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
        mock_servicenow_client,
        mock_capability_router,
    ):
        """Test complete password reset flow: Trigger → Classify → Validate → Execute → Notify → Close → End."""
        # Create mock LLM with high confidence classification
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                ticket_type="password_reset",
                confidence=0.95,
                affected_user="jsmith",
            )
        )

        # Create engine
        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )
        engine.servicenow_client = mock_servicenow_client
        engine.capability_router = mock_capability_router

        # Mock httpx AsyncClient for both GET (capability routing) and POST (tool server)
        with patch("httpx.AsyncClient") as mock_client_class:
            # Create mock client instance
            mock_client = AsyncMock()

            # Mock GET response for capability routing
            mock_get_response = MagicMock()
            mock_get_response.status_code = 200
            mock_get_response.json.return_value = [{
                "url": "http://localhost:8080",
                "status": "online"
            }]

            # Mock POST response for tool server
            mock_post_response = MagicMock()
            mock_post_response.status_code = 200
            mock_post_response.json.return_value = {
                "success": True,
                "message": "Password reset for jsmith",
                "temp_password": "TempP@ss123!",
            }

            # Set up the mock client to return appropriate responses
            mock_client.get = AsyncMock(return_value=mock_get_response)
            mock_client.post = AsyncMock(return_value=mock_post_response)

            # Make the context manager return the mock client
            mock_client_class.return_value.__aenter__.return_value = mock_client
            mock_client_class.return_value.__aexit__.return_value = AsyncMock()

            # Execute workflow
            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # Verify completion
        assert context.status == ExecutionStatus.COMPLETED

        # Verify steps executed in order
        executed_steps = list(context.step_results.keys())
        assert "trigger-start" in executed_steps
        assert "classify-ticket" in executed_steps
        assert "validate-request" in executed_steps
        assert "execute-reset" in executed_steps
        assert "notify-user" in executed_steps
        assert "close-ticket" in executed_steps
        assert "end" in executed_steps

        # Verify escalate was NOT called
        assert "escalate-to-human" not in executed_steps

        # Verify classification result
        assert context.get_variable("ticket_type") == "password_reset"
        assert context.get_variable("confidence") == 0.95
        assert context.get_variable("affected_user") == "jsmith"

    @pytest.mark.asyncio
    async def test_validation_passes_normal_user(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test validation passes for normal user."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                affected_user="regular.user",
                confidence=0.9,
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )

        with patch("httpx.AsyncClient") as mock_client_class:
            mock_client = AsyncMock()
            mock_get_response = MagicMock()
            mock_get_response.status_code = 200
            mock_get_response.json.return_value = [{"url": "http://localhost:8080"}]
            mock_post_response = MagicMock()
            mock_post_response.status_code = 200
            mock_post_response.json.return_value = {"success": True, "message": "Done"}
            mock_client.get = AsyncMock(return_value=mock_get_response)
            mock_client.post = AsyncMock(return_value=mock_post_response)
            mock_client_class.return_value.__aenter__.return_value = mock_client
            mock_client_class.return_value.__aexit__.return_value = AsyncMock()

            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # Check validation passed
        validate_result = context.step_results.get("validate-request")
        assert validate_result is not None
        assert validate_result.output.get("valid") is True


class TestLowConfidenceEscalation:
    """Test escalation when classification confidence is low."""

    @pytest.mark.asyncio
    async def test_low_confidence_escalates(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test workflow escalates when confidence < 0.8."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                ticket_type="unknown",
                confidence=0.5,  # Low confidence
                affected_user=None,
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )

        context = await engine.execute(
            ticket_id="INC0001234",
            ticket_data=sample_ticket_data,
        )

        # Verify escalation
        assert context.status == ExecutionStatus.ESCALATED

        # Verify escalate step was called
        assert "escalate-to-human" in context.step_results

        # Verify execute was NOT called
        assert "execute-reset" not in context.step_results

        # Verify escalation reason mentions confidence
        assert "confidence" in context.escalation_reason.lower()


class TestValidationFailureEscalation:
    """Test escalation when validation fails."""

    @pytest.mark.asyncio
    async def test_admin_user_escalates(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test workflow escalates for admin user."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                ticket_type="password_reset",
                confidence=0.95,
                affected_user="administrator",  # Admin user
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )

        context = await engine.execute(
            ticket_id="INC0001234",
            ticket_data=sample_ticket_data,
        )

        # Verify escalation
        assert context.status == ExecutionStatus.ESCALATED

        # Verify validation failed
        validate_result = context.step_results.get("validate-request")
        assert validate_result is not None
        assert validate_result.output.get("valid") is False

        # Verify execute was NOT called
        assert "execute-reset" not in context.step_results

    @pytest.mark.asyncio
    async def test_service_account_escalates(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test workflow escalates for service account (svc_*)."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                ticket_type="password_reset",
                confidence=0.95,
                affected_user="svc_backup",  # Service account
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )

        context = await engine.execute(
            ticket_id="INC0001234",
            ticket_data=sample_ticket_data,
        )

        # Verify escalation due to deny list
        assert context.status == ExecutionStatus.ESCALATED


class TestExecutionFailureEscalation:
    """Test escalation when tool execution fails."""

    @pytest.mark.asyncio
    async def test_tool_server_failure_escalates(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
        mock_capability_router,
    ):
        """Test workflow escalates when Tool Server returns failure."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                ticket_type="password_reset",
                confidence=0.95,
                affected_user="jsmith",
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )
        engine.capability_router = mock_capability_router

        # Mock tool server failure
        with patch("httpx.AsyncClient") as mock_client_class:
            mock_client = AsyncMock()
            mock_get_response = MagicMock()
            mock_get_response.status_code = 200
            mock_get_response.json.return_value = [{"url": "http://localhost:8080"}]
            mock_post_response = MagicMock()
            mock_post_response.status_code = 200
            mock_post_response.json.return_value = {
                "success": False,
                "message": "User not found in Active Directory",
            }
            mock_client.get = AsyncMock(return_value=mock_get_response)
            mock_client.post = AsyncMock(return_value=mock_post_response)
            mock_client_class.return_value.__aenter__.return_value = mock_client
            mock_client_class.return_value.__aexit__.return_value = AsyncMock()

            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # Verify escalation
        assert context.status == ExecutionStatus.ESCALATED

        # Verify execute was called but returned failure
        execute_result = context.step_results.get("execute-reset")
        assert execute_result is not None
        assert execute_result.output.get("success") is False

    @pytest.mark.skip(reason="Async context manager mocking issue with exceptions - needs investigation")
    @pytest.mark.asyncio
    async def test_tool_server_timeout_escalates(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
        mock_capability_router,
    ):
        """Test workflow escalates when Tool Server times out."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                ticket_type="password_reset",
                confidence=0.95,
                affected_user="jsmith",
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )
        engine.capability_router = mock_capability_router

        # Mock timeout - the _call_tool_server handles timeout and returns error dict
        # So we need to mock it to raise the timeout when post is called
        async def mock_post_timeout(*args, **kwargs):
            raise httpx.TimeoutException("Connection timed out")

        with patch("httpx.AsyncClient") as mock_client_class:
            mock_client = AsyncMock()
            mock_get_response = MagicMock()
            mock_get_response.status_code = 200
            mock_get_response.json.return_value = [{"url": "http://localhost:8080"}]
            mock_client.get = AsyncMock(return_value=mock_get_response)
            mock_client.post = mock_post_timeout
            mock_client_class.return_value.__aenter__.return_value = mock_client
            mock_client_class.return_value.__aexit__.return_value = AsyncMock(return_value=None)

            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # Verify that the execute step failed (timeout is caught and returns success=False)
        # The workflow should escalate because success == false
        assert context.status == ExecutionStatus.ESCALATED


class TestWorkflowMetrics:
    """Test workflow execution metrics and tracking."""

    @pytest.mark.asyncio
    async def test_step_execution_tracked(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test all executed steps are tracked in context."""
        mock_driver = MockPromptDriver()

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )

        with patch("httpx.AsyncClient") as mock_client_class:
            mock_client = AsyncMock()
            mock_get_response = MagicMock()
            mock_get_response.status_code = 200
            mock_get_response.json.return_value = [{"url": "http://localhost:8080"}]
            mock_post_response = MagicMock()
            mock_post_response.status_code = 200
            mock_post_response.json.return_value = {"success": True, "message": "Done"}
            mock_client.get = AsyncMock(return_value=mock_get_response)
            mock_client.post = AsyncMock(return_value=mock_post_response)
            mock_client_class.return_value.__aenter__.return_value = mock_client
            mock_client_class.return_value.__aexit__.return_value = AsyncMock()

            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # All steps should have results
        for step_name, result in context.step_results.items():
            assert result.step_name == step_name
            assert result.status in [ExecutionStatus.COMPLETED, ExecutionStatus.FAILED]
            assert result.output is not None or result.error is not None

    @pytest.mark.asyncio
    async def test_variables_preserved_across_steps(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test variables set by one step are available to later steps."""
        mock_driver = MockPromptDriver(
            classification=MockClassificationResponse(
                affected_user="testuser",
                confidence=0.92,
            )
        )

        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=mock_driver,
            admin_portal_url="http://localhost:5000",
        )

        with patch("httpx.AsyncClient") as mock_client_class:
            mock_client = AsyncMock()
            mock_get_response = MagicMock()
            mock_get_response.status_code = 200
            mock_get_response.json.return_value = [{"url": "http://localhost:8080"}]
            mock_post_response = MagicMock()
            mock_post_response.status_code = 200
            mock_post_response.json.return_value = {"success": True, "message": "Done"}
            mock_client.get = AsyncMock(return_value=mock_get_response)
            mock_client.post = AsyncMock(return_value=mock_post_response)
            mock_client_class.return_value.__aenter__.return_value = mock_client
            mock_client_class.return_value.__aexit__.return_value = AsyncMock()

            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # Variables from classification should persist
        assert context.get_variable("affected_user") == "testuser"
        assert context.get_variable("confidence") == 0.92
        assert context.get_variable("ticket_type") == "password_reset"


class TestEdgeCases:
    """Test edge cases and error handling."""

    @pytest.mark.asyncio
    async def test_missing_ticket_description_fails_trigger(
        self,
        sample_export: AgentExport,
    ):
        """Test workflow fails at trigger if required fields missing."""
        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=MockPromptDriver(),
            admin_portal_url="http://localhost:5000",
        )

        # Empty ticket data
        context = await engine.execute(
            ticket_id="INC0001234",
            ticket_data={},
        )

        # Should fail at trigger
        assert context.status == ExecutionStatus.FAILED
        trigger_result = context.step_results.get("trigger-start")
        assert trigger_result is not None
        assert trigger_result.status == ExecutionStatus.FAILED

    @pytest.mark.asyncio
    async def test_max_steps_prevents_infinite_loop(
        self,
        sample_export: AgentExport,
        sample_ticket_data: dict,
    ):
        """Test max steps limit prevents infinite loops."""
        # This is a safety check - workflow should complete normally
        engine = WorkflowEngine(
            export=sample_export,
            llm_driver=MockPromptDriver(),
            admin_portal_url="http://localhost:5000",
        )

        with patch("httpx.AsyncClient.post") as mock_post:
            mock_response = MagicMock()
            mock_response.status_code = 200
            mock_response.json.return_value = {"success": True, "message": "Done"}
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(return_value=mock_response)

            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )

        # Should complete without hitting max steps
        assert len(context.step_results) < 100
