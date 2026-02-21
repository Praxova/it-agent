"""Shared fixtures for runtime tests."""
import pytest
from unittest.mock import AsyncMock, MagicMock
from typing import Any

from agent.runtime.models import (
    AgentExport,
    AgentBasicInfo,
    WorkflowExportInfo,
    WorkflowStepExportInfo,
    WorkflowTransitionExportInfo,
    RulesetExportInfo,
    RuleExportInfo,
    ProviderExportInfo,
    ServiceNowExportInfo,
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
            from_step_name="trigger-start",
            to_step_name="classify-ticket",
            condition=None,  # Always proceed
        ),
        # From classify - high confidence
        WorkflowTransitionExportInfo(
            from_step_name="classify-ticket",
            to_step_name="validate-request",
            condition="confidence >= 0.8",
        ),
        # From classify - low confidence
        WorkflowTransitionExportInfo(
            from_step_name="classify-ticket",
            to_step_name="escalate-to-human",
            condition="confidence < 0.8",
        ),
        # From validate - valid
        WorkflowTransitionExportInfo(
            from_step_name="validate-request",
            to_step_name="execute-reset",
            condition="valid == true",
        ),
        # From validate - invalid
        WorkflowTransitionExportInfo(
            from_step_name="validate-request",
            to_step_name="escalate-to-human",
            condition="valid == false",
        ),
        # From execute - success
        WorkflowTransitionExportInfo(
            from_step_name="execute-reset",
            to_step_name="notify-user",
            condition="success == true",
        ),
        # From execute - failure
        WorkflowTransitionExportInfo(
            from_step_name="execute-reset",
            to_step_name="escalate-to-human",
            condition="success == false",
        ),
        # From notify to close
        WorkflowTransitionExportInfo(
            from_step_name="notify-user",
            to_step_name="close-ticket",
            condition=None,
        ),
        # From close to end
        WorkflowTransitionExportInfo(
            from_step_name="close-ticket",
            to_step_name="end",
            condition=None,
        ),
        # From escalate to end
        WorkflowTransitionExportInfo(
            from_step_name="escalate-to-human",
            to_step_name="end",
            condition=None,
        ),
    ]

    return WorkflowExportInfo(
        name="password-reset-workflow",
        display_name="Password Reset Workflow",
        version="1.0",
        steps=steps,
        transitions=transitions,
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
        provider_type="llm-ollama",
        config={
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
        agent=AgentBasicInfo(
            id="test-agent-123",
            name="test-agent",
            display_name="Test Agent",
            is_enabled=True,
            assignment_group="helpdesk-group",
        ),
        workflow=password_reset_workflow,
        rulesets=sample_rulesets,
        example_sets={},
        llm_provider=sample_llm_provider,
        service_now=ServiceNowExportInfo(
            provider_type="servicenow-basic",
            config={"instance_url": "https://test.service-now.com"},
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
