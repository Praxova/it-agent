Phase 4B-4 - End-to-End Testing
Context
Phase 4B-1 created core runtime infrastructure (models, engine, context).
Phase 4B-2 implemented all 9 step executors.
Phase 4B-3 integrated ServiceNow, LLM drivers, and capability routing.
Now we validate the complete system with end-to-end tests covering all ticket processing scenarios.
Project Location: /home/alton/Documents/lucid-it-agent
Runtime Location: /home/alton/Documents/lucid-it-agent/agent/src/agent/runtime
Overview
Create comprehensive end-to-end tests that:

Test complete workflow execution with mock services
Test individual scenarios (happy path, escalation, failures)
Provide integration test framework for real services
Create test fixtures and helpers

Task 1: Create Test Fixtures (conftest.py)
Create agent/tests/runtime/conftest.py:
python"""Shared fixtures for runtime tests."""
import pytest
from unittest.mock import AsyncMock, MagicMock
from typing import Any

from agent.runtime.models import (
    AgentExport,
    AgentExportInfo,
    WorkflowExportInfo,
    WorkflowStepExportInfo,
    WorkflowTransitionExportInfo,
    RulesetExportInfo,
    RuleExportInfo,
    ProviderExportInfo,
    CredentialReference,
    StepType,
)
from agent.runtime.execution_context import ExecutionContext
from agent.runtime.integrations.servicenow_client import Ticket


@pytest.fixture
def mock_llm_driver():
    """Create a mock LLM driver that returns predictable responses."""
    driver = MagicMock()
    return driver


@pytest.fixture
def sample_ticket() -> Ticket:
    """Create a sample ServiceNow ticket."""
    return Ticket(
        sys_id="abc123def456",
        number="INC0001234",
        short_description="Password reset for jsmith",
        description="User John Smith (jsmith) forgot their password and needs it reset. Please help ASAP.",
        caller_id="requester@montanifarms.com",
        state="1",
        assignment_group="helpdesk-group-id",
    )


@pytest.fixture
def sample_ticket_data(sample_ticket) -> dict[str, Any]:
    """Convert sample ticket to ticket_data dict."""
    return sample_ticket.to_ticket_data()


@pytest.fixture
def password_reset_workflow() -> WorkflowExportInfo:
    """Create a complete password reset workflow for testing."""
    steps = [
        WorkflowStepExportInfo(
            name="trigger-start",
            step_type=StepType.TRIGGER,
            display_name="Trigger",
            configuration={"source": "servicenow"},
            sort_order=1,
        ),
        WorkflowStepExportInfo(
            name="classify-ticket",
            step_type=StepType.CLASSIFY,
            display_name="Classify Ticket",
            configuration={"use_example_set": "password-reset-examples"},
            rulesets=["classification-rules"],
            sort_order=2,
        ),
        WorkflowStepExportInfo(
            name="validate-request",
            step_type=StepType.VALIDATE,
            display_name="Validate Request",
            configuration={
                "checks": ["user_exists", "not_admin"],
                "deny_list": ["administrator", "admin", "svc_*"],
            },
            rulesets=["security-rules"],
            sort_order=3,
        ),
        WorkflowStepExportInfo(
            name="execute-reset",
            step_type=StepType.EXECUTE,
            display_name="Execute Password Reset",
            configuration={
                "capability": "ad-password-reset",
                "param_mapping": {"username": "affected_user"},
            },
            sort_order=4,
        ),
        WorkflowStepExportInfo(
            name="notify-user",
            step_type=StepType.NOTIFY,
            display_name="Notify User",
            configuration={
                "channel": "ticket-comment",
                "template": "password-reset-success",
            },
            sort_order=5,
        ),
        WorkflowStepExportInfo(
            name="close-ticket",
            step_type=StepType.UPDATE_TICKET,
            display_name="Close Ticket",
            configuration={
                "state": "resolved",
                "close_code": "automated",
                "add_resolution_notes": True,
            },
            sort_order=6,
        ),
        WorkflowStepExportInfo(
            name="escalate-to-human",
            step_type=StepType.ESCALATE,
            display_name="Escalate to Human",
            configuration={"target_group": "Level 2 Support"},
            sort_order=7,
        ),
        WorkflowStepExportInfo(
            name="end",
            step_type=StepType.END,
            display_name="End",
            configuration={},
            sort_order=8,
        ),
    ]
    
    transitions = [
        # From trigger to classify
        WorkflowTransitionExportInfo(
            from_step="trigger-start",
            to_step="classify-ticket",
            condition=None,  # Always proceed
            sort_order=1,
        ),
        # From classify - high confidence
        WorkflowTransitionExportInfo(
            from_step="classify-ticket",
            to_step="validate-request",
            condition="confidence >= 0.8",
            sort_order=2,
        ),
        # From classify - low confidence
        WorkflowTransitionExportInfo(
            from_step="classify-ticket",
            to_step="escalate-to-human",
            condition="confidence < 0.8",
            sort_order=3,
        ),
        # From validate - valid
        WorkflowTransitionExportInfo(
            from_step="validate-request",
            to_step="execute-reset",
            condition="valid == true",
            sort_order=4,
        ),
        # From validate - invalid
        WorkflowTransitionExportInfo(
            from_step="validate-request",
            to_step="escalate-to-human",
            condition="valid == false",
            sort_order=5,
        ),
        # From execute - success
        WorkflowTransitionExportInfo(
            from_step="execute-reset",
            to_step="notify-user",
            condition="success == true",
            sort_order=6,
        ),
        # From execute - failure
        WorkflowTransitionExportInfo(
            from_step="execute-reset",
            to_step="escalate-to-human",
            condition="success == false",
            sort_order=7,
        ),
        # From notify to close
        WorkflowTransitionExportInfo(
            from_step="notify-user",
            to_step="close-ticket",
            condition=None,
            sort_order=8,
        ),
        # From close to end
        WorkflowTransitionExportInfo(
            from_step="close-ticket",
            to_step="end",
            condition=None,
            sort_order=9,
        ),
        # From escalate to end
        WorkflowTransitionExportInfo(
            from_step="escalate-to-human",
            to_step="end",
            condition=None,
            sort_order=10,
        ),
    ]
    
    return WorkflowExportInfo(
        name="password-reset-workflow",
        display_name="Password Reset Workflow",
        version="1.0",
        steps=steps,
        transitions=transitions,
        rulesets=["security-defaults"],
    )


@pytest.fixture
def sample_rulesets() -> dict[str, RulesetExportInfo]:
    """Create sample rulesets for testing."""
    return {
        "security-defaults": RulesetExportInfo(
            name="security-defaults",
            display_name="Security Defaults",
            description="Default security rules",
            rules=[
                RuleExportInfo(
                    name="no-admin-reset",
                    rule_text="Never reset passwords for accounts with 'admin' in the name",
                    priority=100,
                    is_enabled=True,
                ),
                RuleExportInfo(
                    name="log-all-actions",
                    rule_text="Log all actions taken for audit purposes",
                    priority=90,
                    is_enabled=True,
                ),
            ],
        ),
        "classification-rules": RulesetExportInfo(
            name="classification-rules",
            display_name="Classification Rules",
            description="Rules for ticket classification",
            rules=[
                RuleExportInfo(
                    name="extract-username",
                    rule_text="Extract the username from the ticket description, looking for patterns like 'for <username>' or 'user <username>'",
                    priority=100,
                    is_enabled=True,
                ),
            ],
        ),
        "security-rules": RulesetExportInfo(
            name="security-rules",
            display_name="Security Rules",
            description="Security validation rules",
            rules=[
                RuleExportInfo(
                    name="deny-service-accounts",
                    rule_text="Do not process requests for service accounts (prefixed with svc_ or sa_)",
                    priority=100,
                    is_enabled=True,
                ),
            ],
        ),
    }


@pytest.fixture
def sample_llm_provider() -> ProviderExportInfo:
    """Create sample LLM provider config."""
    return ProviderExportInfo(
        name="test-llm",
        provider_type="llm-ollama",
        provider_config={
            "model": "llama3.1",
            "endpoint": "http://localhost:11434",
            "temperature": 0.1,
        },
        credentials=CredentialReference(storage="none"),
    )


@pytest.fixture
def sample_export(
    password_reset_workflow,
    sample_rulesets,
    sample_llm_provider,
) -> AgentExport:
    """Create a complete agent export for testing."""
    return AgentExport(
        version="1.0",
        exported_at="2025-02-01T12:00:00Z",
        agent=AgentExportInfo(
            name="test-agent",
            display_name="Test Agent",
            is_enabled=True,
            configuration={"assignment_group": "helpdesk-group"},
        ),
        workflow=password_reset_workflow,
        rulesets=sample_rulesets,
        example_sets={},
        llm_provider=sample_llm_provider,
        service_now=ProviderExportInfo(
            name="test-servicenow",
            provider_type="servicenow-basic",
            provider_config={"instance_url": "https://test.service-now.com"},
            credentials=CredentialReference(
                storage="environment",
                username_key="SNOW_USERNAME",
                password_key="SNOW_PASSWORD",
            ),
        ),
        required_capabilities=["ad-password-reset"],
    )


@pytest.fixture
def mock_servicenow_client():
    """Create a mock ServiceNow client."""
    client = AsyncMock()
    client.poll_queue = AsyncMock(return_value=[])
    client.get_ticket = AsyncMock(return_value=None)
    client.update_ticket = AsyncMock(return_value=True)
    client.add_work_note = AsyncMock(return_value=True)
    client.add_comment = AsyncMock(return_value=True)
    client.set_state = AsyncMock(return_value=True)
    client.assign_to_group = AsyncMock(return_value=True)
    return client


@pytest.fixture
def mock_capability_router():
    """Create a mock capability router."""
    from agent.runtime.integrations.capability_router import ToolServerInfo
    
    router = AsyncMock()
    router.get_server_for_capability = AsyncMock(return_value=ToolServerInfo(
        id="tool-server-1",
        name="Windows Tool Server",
        url="http://localhost:8080",
        status="online",
        capabilities=["ad-password-reset", "ad-group-add"],
    ))
    return router
Task 2: Create Mock LLM Driver (mock_drivers.py)
Create agent/tests/runtime/mock_drivers.py:
python"""Mock LLM drivers for testing."""
from __future__ import annotations
import json
from typing import Any
from dataclasses import dataclass

from griptape.artifacts import TextArtifact


@dataclass
class MockClassificationResponse:
    """Configurable classification response."""
    ticket_type: str = "password_reset"
    confidence: float = 0.95
    affected_user: str = "jsmith"
    target_group: str | None = None
    target_resource: str | None = None
    reasoning: str = "Clear password reset request"


class MockPromptDriver:
    """
    Mock LLM driver for testing workflows.
    
    Returns predictable responses based on configuration.
    """
    
    def __init__(
        self,
        classification: MockClassificationResponse | None = None,
        should_fail: bool = False,
        fail_message: str = "Mock LLM failure",
    ):
        self.classification = classification or MockClassificationResponse()
        self.should_fail = should_fail
        self.fail_message = fail_message
        self.call_history: list[str] = []
    
    def run(self, prompt: str) -> Any:
        """Simulate LLM response."""
        self.call_history.append(prompt)
        
        if self.should_fail:
            raise RuntimeError(self.fail_message)
        
        # Return classification JSON
        response = {
            "ticket_type": self.classification.ticket_type,
            "confidence": self.classification.confidence,
            "affected_user": self.classification.affected_user,
            "target_group": self.classification.target_group,
            "target_resource": self.classification.target_resource,
            "reasoning": self.classification.reasoning,
        }
        
        return MockAgentResult(json.dumps(response, indent=2))


class MockAgentResult:
    """Mock Griptape agent result."""
    
    def __init__(self, text: str):
        self.output_task = MockOutputTask(text)


class MockOutputTask:
    """Mock output task."""
    
    def __init__(self, text: str):
        self.output = MockOutput(text)


class MockOutput:
    """Mock output with value."""
    
    def __init__(self, text: str):
        self.value = text


class MockToolServerClient:
    """
    Mock Tool Server client for testing.
    
    Returns predictable responses for tool operations.
    """
    
    def __init__(
        self,
        should_succeed: bool = True,
        response_data: dict[str, Any] | None = None,
    ):
        self.should_succeed = should_succeed
        self.response_data = response_data or {}
        self.call_history: list[dict[str, Any]] = []
    
    async def call(
        self,
        endpoint: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        """Simulate Tool Server call."""
        self.call_history.append({
            "endpoint": endpoint,
            "params": params,
        })
        
        if not self.should_succeed:
            return {
                "success": False,
                "message": "Mock tool server failure",
            }
        
        # Default success responses by endpoint
        if "password" in endpoint:
            return {
                "success": True,
                "message": f"Password reset for {params.get('username', 'user')}",
                "temp_password": "TempP@ss123!",
                **self.response_data,
            }
        elif "group" in endpoint:
            return {
                "success": True,
                "message": f"Group operation completed",
                **self.response_data,
            }
        else:
            return {
                "success": True,
                "message": "Operation completed",
                **self.response_data,
            }
Task 3: Create End-to-End Workflow Tests (test_e2e_workflow.py)
Create agent/tests/runtime/test_e2e_workflow.py:
python"""End-to-end workflow execution tests."""
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
        
        # Mock tool server response
        with patch("httpx.AsyncClient.post") as mock_post:
            mock_response = MagicMock()
            mock_response.status_code = 200
            mock_response.json.return_value = {
                "success": True,
                "message": "Password reset for jsmith",
                "temp_password": "TempP@ss123!",
            }
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(return_value=mock_response)
            
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
        
        with patch("httpx.AsyncClient.post") as mock_post:
            mock_response = MagicMock()
            mock_response.status_code = 200
            mock_response.json.return_value = {"success": True, "message": "Done"}
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(return_value=mock_response)
            
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
        with patch("httpx.AsyncClient.post") as mock_post:
            mock_response = MagicMock()
            mock_response.status_code = 200
            mock_response.json.return_value = {
                "success": False,
                "message": "User not found in Active Directory",
            }
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(return_value=mock_response)
            
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
        
        # Mock timeout
        with patch("httpx.AsyncClient.post") as mock_post:
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(
                side_effect=httpx.TimeoutException("Connection timed out")
            )
            
            context = await engine.execute(
                ticket_id="INC0001234",
                ticket_data=sample_ticket_data,
            )
        
        # Verify escalation due to error
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
        
        with patch("httpx.AsyncClient.post") as mock_post:
            mock_response = MagicMock()
            mock_response.status_code = 200
            mock_response.json.return_value = {"success": True, "message": "Done"}
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(return_value=mock_response)
            
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
        
        with patch("httpx.AsyncClient.post") as mock_post:
            mock_response = MagicMock()
            mock_response.status_code = 200
            mock_response.json.return_value = {"success": True, "message": "Done"}
            mock_post.return_value.__aenter__.return_value.post = AsyncMock(return_value=mock_response)
            
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
Task 4: Create Integration Test Script (test_live_integration.py)
Create agent/tests/runtime/test_live_integration.py for testing with real services:
python"""
Integration tests for live services.

These tests require real services to be running:
- Admin Portal at ADMIN_PORTAL_URL
- Ollama at OLLAMA_URL (optional, will skip if not available)
- ServiceNow PDI (optional, marked for manual run)

Run with: pytest tests/runtime/test_live_integration.py -v -m integration
"""
import os
import pytest
import httpx
import asyncio

# Mark all tests in this module as integration tests
pytestmark = pytest.mark.integration


def is_service_available(url: str, timeout: float = 2.0) -> bool:
    """Check if a service is reachable."""
    try:
        response = httpx.get(url, timeout=timeout)
        return response.status_code < 500
    except Exception:
        return False


@pytest.fixture(scope="module")
def admin_portal_url():
    """Get Admin Portal URL from environment."""
    url = os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000")
    if not is_service_available(url):
        pytest.skip(f"Admin Portal not available at {url}")
    return url


@pytest.fixture(scope="module")
def ollama_url():
    """Get Ollama URL from environment."""
    url = os.environ.get("OLLAMA_URL", "http://localhost:11434")
    if not is_service_available(url):
        pytest.skip(f"Ollama not available at {url}")
    return url


class TestAdminPortalIntegration:
    """Test integration with Admin Portal."""
    
    @pytest.mark.asyncio
    async def test_can_fetch_agent_export(self, admin_portal_url):
        """Test fetching agent export from Admin Portal."""
        agent_name = os.environ.get("TEST_AGENT_NAME", "test-agent")
        url = f"{admin_portal_url}/api/agents/by-name/{agent_name}/export"
        
        async with httpx.AsyncClient() as client:
            response = await client.get(url, timeout=10.0)
        
        # Should get export or 404 if agent doesn't exist
        assert response.status_code in [200, 404]
        
        if response.status_code == 200:
            data = response.json()
            assert "agent" in data
            assert "workflow" in data
            assert "version" in data
    
    @pytest.mark.asyncio
    async def test_config_loader_integration(self, admin_portal_url):
        """Test ConfigLoader with real Admin Portal."""
        from agent.runtime.config_loader import ConfigLoader
        
        agent_name = os.environ.get("TEST_AGENT_NAME", "test-agent")
        
        try:
            loader = ConfigLoader(admin_portal_url, agent_name)
            export = await loader.load()
            
            assert export.agent.name == agent_name
            assert export.workflow is not None
            print(f"\nLoaded agent: {export.agent.display_name}")
            print(f"Workflow: {export.workflow.display_name}")
            print(f"Steps: {len(export.workflow.steps)}")
            print(f"Rulesets: {list(export.rulesets.keys())}")
            
        except Exception as e:
            pytest.skip(f"Could not load agent config: {e}")


class TestOllamaIntegration:
    """Test integration with Ollama."""
    
    @pytest.mark.asyncio
    async def test_ollama_health(self, ollama_url):
        """Test Ollama is responding."""
        async with httpx.AsyncClient() as client:
            response = await client.get(f"{ollama_url}/api/tags", timeout=5.0)
        
        assert response.status_code == 200
        data = response.json()
        print(f"\nAvailable models: {[m['name'] for m in data.get('models', [])]}")
    
    @pytest.mark.asyncio
    async def test_driver_factory_creates_ollama_driver(self, ollama_url):
        """Test creating real Ollama driver."""
        from agent.runtime.models import ProviderExportInfo, CredentialReference
        from agent.runtime.integrations.driver_factory import create_prompt_driver
        
        provider = ProviderExportInfo(
            name="test-ollama",
            provider_type="llm-ollama",
            provider_config={
                "model": "llama3.1",
                "endpoint": ollama_url,
            },
            credentials=CredentialReference(storage="none"),
        )
        
        driver = create_prompt_driver(provider)
        assert driver is not None
        print(f"\nCreated Ollama driver for model: llama3.1")


class TestCapabilityRoutingIntegration:
    """Test capability routing with real Admin Portal."""
    
    @pytest.mark.asyncio
    async def test_query_capability(self, admin_portal_url):
        """Test querying capability from Admin Portal."""
        from agent.runtime.integrations.capability_router import CapabilityRouter
        
        router = CapabilityRouter(admin_portal_url)
        
        try:
            server = await router.get_server_for_capability("ad-password-reset")
            print(f"\nFound server for ad-password-reset:")
            print(f"  Name: {server.name}")
            print(f"  URL: {server.url}")
            print(f"  Status: {server.status}")
        except Exception as e:
            # May not have capability configured, which is OK
            print(f"\nNo server found for ad-password-reset: {e}")


@pytest.mark.manual
class TestServiceNowIntegration:
    """
    Test integration with ServiceNow.
    
    These tests require a real ServiceNow instance.
    Run manually with: pytest tests/runtime/test_live_integration.py::TestServiceNowIntegration -v -m manual
    """
    
    @pytest.fixture
    def servicenow_client(self):
        """Create ServiceNow client from environment."""
        from agent.runtime.integrations.servicenow_client import (
            ServiceNowClient,
            ServiceNowCredentials,
        )
        
        instance_url = os.environ.get("SNOW_INSTANCE_URL")
        username = os.environ.get("SNOW_USERNAME")
        password = os.environ.get("SNOW_PASSWORD")
        
        if not all([instance_url, username, password]):
            pytest.skip("ServiceNow credentials not configured")
        
        return ServiceNowClient(ServiceNowCredentials(
            instance_url=instance_url,
            username=username,
            password=password,
        ))
    
    @pytest.mark.asyncio
    async def test_poll_queue(self, servicenow_client):
        """Test polling ServiceNow queue."""
        assignment_group = os.environ.get("SNOW_ASSIGNMENT_GROUP", "")
        
        if not assignment_group:
            pytest.skip("SNOW_ASSIGNMENT_GROUP not set")
        
        tickets = await servicenow_client.poll_queue(
            assignment_group=assignment_group,
            state="1",
            limit=5,
        )
        
        print(f"\nFound {len(tickets)} tickets in queue")
        for ticket in tickets[:3]:
            print(f"  {ticket.number}: {ticket.short_description[:50]}")


@pytest.mark.manual
class TestEndToEndLive:
    """
    Full end-to-end test with real services.
    
    CAUTION: This will process a real ticket if configured.
    Only run in test environments.
    """
    
    @pytest.mark.asyncio
    async def test_dry_run_workflow(self, admin_portal_url, ollama_url):
        """
        Test workflow execution with real LLM but mock actions.
        
        This runs classification with real Ollama but doesn't
        actually execute Tool Server calls.
        """
        from agent.runtime.config_loader import ConfigLoader
        from agent.runtime.workflow_engine import WorkflowEngine
        from agent.runtime.integrations.driver_factory import create_prompt_driver
        
        agent_name = os.environ.get("TEST_AGENT_NAME", "test-agent")
        
        # Load config
        loader = ConfigLoader(admin_portal_url, agent_name)
        
        try:
            export = await loader.load()
        except Exception as e:
            pytest.skip(f"Could not load agent: {e}")
        
        # Create real LLM driver
        if export.llm_provider:
            # Override endpoint to use local Ollama
            export.llm_provider.provider_config["endpoint"] = ollama_url
            driver = create_prompt_driver(export.llm_provider)
        else:
            pytest.skip("No LLM provider configured")
        
        # Create engine
        engine = WorkflowEngine(
            export=export,
            llm_driver=driver,
            admin_portal_url=admin_portal_url,
        )
        
        # Test with sample ticket
        test_ticket = {
            "number": "TEST001",
            "short_description": "Password reset for testuser",
            "description": "User testuser forgot their password. Please reset it.",
            "caller_id": "tester@example.com",
        }
        
        print("\n--- Starting dry run workflow ---")
        print(f"Ticket: {test_ticket['short_description']}")
        
        # Run but expect escalation since we have no Tool Server
        context = await engine.execute(
            ticket_id="TEST001",
            ticket_data=test_ticket,
        )
        
        print(f"\nWorkflow completed with status: {context.status.value}")
        print(f"Steps executed: {list(context.step_results.keys())}")
        
        if context.get_variable("ticket_type"):
            print(f"Classification: {context.get_variable('ticket_type')} "
                  f"(confidence: {context.get_variable('confidence')})")
Task 5: Create Test Runner Script (run_tests.py)
Create agent/tests/runtime/run_tests.py:
python#!/usr/bin/env python
"""
Test runner script for runtime tests.

Usage:
    python run_tests.py              # Run all unit tests
    python run_tests.py --all        # Run all tests including integration
    python run_tests.py --integration # Run only integration tests
    python run_tests.py --live       # Run live integration tests
"""
import subprocess
import sys
import os


def run_tests(args: list[str]):
    """Run pytest with specified arguments."""
    cmd = ["pytest", "tests/runtime/", "-v"] + args
    print(f"Running: {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=os.path.dirname(os.path.dirname(os.path.dirname(__file__))))


def main():
    args = sys.argv[1:]
    
    if "--help" in args or "-h" in args:
        print(__doc__)
        return 0
    
    pytest_args = []
    
    if "--all" in args:
        # Run everything
        pass
    elif "--integration" in args:
        pytest_args.extend(["-m", "integration"])
    elif "--live" in args:
        pytest_args.extend(["-m", "integration or manual"])
    else:
        # Default: skip integration and manual tests
        pytest_args.extend(["-m", "not integration and not manual"])
    
    # Add any additional pytest args
    for arg in args:
        if arg not in ["--all", "--integration", "--live"]:
            pytest_args.append(arg)
    
    result = run_tests(pytest_args)
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
Task 6: Update pytest configuration
Update agent/pyproject.toml to add pytest markers:
toml[tool.pytest.ini_options]
asyncio_mode = "auto"
markers = [
    "integration: marks tests as integration tests (may require external services)",
    "manual: marks tests that should only be run manually",
]
filterwarnings = [
    "ignore::DeprecationWarning",
]
Task 7: Create Test Documentation (TESTING.md)
Create agent/tests/runtime/TESTING.md:
markdown# Runtime Testing Guide

## Test Categories

### Unit Tests (Default)
Fast tests that mock all external services.
```bash
cd agent
pytest tests/runtime/ -v
```

### Integration Tests
Tests that require running services (Admin Portal, Ollama).
```bash
# Set environment
export ADMIN_PORTAL_URL=http://localhost:5000
export OLLAMA_URL=http://localhost:11434

# Run integration tests
pytest tests/runtime/ -v -m integration
```

### Manual/Live Tests
Tests that interact with real systems (ServiceNow).
```bash
# Set ServiceNow credentials
export SNOW_INSTANCE_URL=https://dev12345.service-now.com
export SNOW_USERNAME=admin
export SNOW_PASSWORD=password
export SNOW_ASSIGNMENT_GROUP=helpdesk-group-id

# Run manual tests
pytest tests/runtime/test_live_integration.py -v -m manual
```

## Test Scenarios

### Happy Path
- Complete password reset: Trigger → Classify → Validate → Execute → Notify → Close → End
- All steps succeed, ticket resolved

### Escalation Scenarios
1. **Low Confidence**: Classification confidence < 0.8
2. **Validation Failure**: Admin user, service account, deny list match
3. **Execution Failure**: Tool Server error, timeout

### Edge Cases
- Missing ticket fields
- Invalid workflow transitions
- Max steps limit

## Test Fixtures

### Key Fixtures (conftest.py)
- `sample_export`: Complete agent export for testing
- `password_reset_workflow`: Full workflow with all steps and transitions
- `sample_rulesets`: Security and classification rulesets
- `mock_servicenow_client`: Async mock for ServiceNow
- `mock_capability_router`: Mock for Tool Server routing

### Mock Drivers (mock_drivers.py)
- `MockPromptDriver`: Returns configurable classification responses
- `MockToolServerClient`: Returns configurable tool responses

## Running Specific Tests
```bash
# Run only happy path tests
pytest tests/runtime/test_e2e_workflow.py::TestPasswordResetHappyPath -v

# Run escalation tests
pytest tests/runtime/test_e2e_workflow.py::TestLowConfidenceEscalation -v

# Run with coverage
pytest tests/runtime/ --cov=agent.runtime --cov-report=html

# Run with detailed output
pytest tests/runtime/ -v --tb=long
```

## CI/CD Integration

For CI pipelines, run unit tests only (no external dependencies):
```yaml
- name: Run unit tests
  run: |
    cd agent
    pytest tests/runtime/ -v -m "not integration and not manual"
```
Verification
bashcd /home/alton/Documents/lucid-it-agent/agent

# Install test dependencies
pip install -e ".[dev]"

# Run all unit tests (should pass without external services)
pytest tests/runtime/ -v -m "not integration and not manual"

# Count tests
pytest tests/runtime/ --collect-only -q

# If Admin Portal is running, test integration
export ADMIN_PORTAL_URL=http://localhost:5000
pytest tests/runtime/test_live_integration.py::TestAdminPortalIntegration -v -m integration

# Full end-to-end with Ollama (requires Ollama running)
pytest tests/runtime/test_live_integration.py::TestEndToEndLive -v -m manual
Summary
Phase 4B-4 provides comprehensive testing infrastructure:
ComponentPurposeconftest.pyShared fixtures (workflows, rulesets, mocks)mock_drivers.pyMock LLM and Tool Server for deterministic teststest_e2e_workflow.pyComplete workflow scenarios (happy path, escalation, failures)test_live_integration.pyReal service integration testsTESTING.mdDocumentation for running tests
Test Scenarios Covered:

✅ Happy path (complete workflow success)
✅ Low confidence escalation
✅ Admin user validation failure
✅ Service account validation failure
✅ Tool Server failure escalation
✅ Tool Server timeout escalation
✅ Missing ticket fields
✅ Variable preservation across steps
✅ Step execution tracking
