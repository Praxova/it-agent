"""Tests for ApprovalExecutor and related approval infrastructure."""
import json
import pytest
from unittest.mock import AsyncMock, MagicMock, patch

from agent.runtime.executors.approval import ApprovalExecutor
from agent.runtime.execution_context import ExecutionContext, ExecutionStatus, StepResult
from agent.runtime.models import StepType, WorkflowStepExportInfo
from agent.runtime.executors.registry import default_registry


@pytest.fixture
def executor():
    return ApprovalExecutor()


@pytest.fixture
def mock_context():
    """Create a mock ExecutionContext."""
    ctx = MagicMock(spec=ExecutionContext)
    ctx.ticket_id = "INC001"
    ctx.ticket_data = {"short_description": "Reset password for jsmith"}
    ctx.variables = {"agent_name": "test-agent", "ticket_type": "password-reset"}
    ctx.admin_portal_url = "http://localhost:5000"
    ctx.workflow_stack = ["test-workflow"]
    return ctx


@pytest.fixture
def approval_step():
    """Create an Approval step definition."""
    return WorkflowStepExportInfo(
        name="approve-action",
        displayName="Approve Action",
        stepType="Approval",
        configuration={
            "description_template": "Classified as {{ticket_type}} (confidence: {{confidence}})",
            "auto_approve_threshold": 0.90,
            "timeout_minutes": 60,
            "timeout_action": "escalate",
        },
        sortOrder=3,
    )


@pytest.mark.asyncio
async def test_auto_approve_above_threshold(executor, mock_context, approval_step):
    """Confidence 0.95 >= threshold 0.90 → auto-approve."""
    mock_context.variables["confidence"] = 0.95

    result = await executor.execute(
        step=approval_step,
        context=mock_context,
        rulesets={},
    )

    assert result.status == ExecutionStatus.COMPLETED
    assert result.output["outcome"] == "approved"
    assert result.output["auto_approved"] is True
    assert mock_context.variables["outcome"] == "approved"
    assert mock_context.variables["auto_approved"] is True


@pytest.mark.asyncio
async def test_no_auto_approve_below_threshold(executor, mock_context, approval_step):
    """Confidence 0.85 < threshold 0.90 → submit to portal, SUSPENDED."""
    mock_context.variables["confidence"] = 0.85

    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "test-id-123", "status": "Pending"}
    mock_response.raise_for_status = MagicMock()

    with patch("agent.runtime.executors.approval.httpx.AsyncClient") as mock_client_cls:
        mock_client = AsyncMock()
        mock_client.post = AsyncMock(return_value=mock_response)
        mock_client.__aenter__ = AsyncMock(return_value=mock_client)
        mock_client.__aexit__ = AsyncMock(return_value=False)
        mock_client_cls.return_value = mock_client

        result = await executor.execute(
            step=approval_step,
            context=mock_context,
            rulesets={},
        )

    assert result.status == ExecutionStatus.SUSPENDED
    assert result.output["outcome"] == "pending"
    assert result.output["approval_id"] == "test-id-123"
    assert mock_context.variables["approval_id"] == "test-id-123"


@pytest.mark.asyncio
async def test_no_auto_approve_missing_confidence(executor, mock_context, approval_step):
    """No confidence in context → submit to portal, SUSPENDED."""
    # confidence not set in variables

    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "test-id-456", "status": "Pending"}
    mock_response.raise_for_status = MagicMock()

    with patch("agent.runtime.executors.approval.httpx.AsyncClient") as mock_client_cls:
        mock_client = AsyncMock()
        mock_client.post = AsyncMock(return_value=mock_response)
        mock_client.__aenter__ = AsyncMock(return_value=mock_client)
        mock_client.__aexit__ = AsyncMock(return_value=False)
        mock_client_cls.return_value = mock_client

        result = await executor.execute(
            step=approval_step,
            context=mock_context,
            rulesets={},
        )

    assert result.status == ExecutionStatus.SUSPENDED
    assert result.output["outcome"] == "pending"


@pytest.mark.asyncio
async def test_no_auto_approve_missing_threshold(executor, mock_context):
    """Confidence 0.95 but no threshold → submit to portal, SUSPENDED."""
    mock_context.variables["confidence"] = 0.95

    step = WorkflowStepExportInfo(
        name="approve-action",
        displayName="Approve Action",
        stepType="Approval",
        configuration={
            "description_template": "Action for {{ticket_type}}",
        },
        sortOrder=3,
    )

    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "test-id-789", "status": "Pending"}
    mock_response.raise_for_status = MagicMock()

    with patch("agent.runtime.executors.approval.httpx.AsyncClient") as mock_client_cls:
        mock_client = AsyncMock()
        mock_client.post = AsyncMock(return_value=mock_response)
        mock_client.__aenter__ = AsyncMock(return_value=mock_client)
        mock_client.__aexit__ = AsyncMock(return_value=False)
        mock_client_cls.return_value = mock_client

        result = await executor.execute(
            step=step,
            context=mock_context,
            rulesets={},
        )

    assert result.status == ExecutionStatus.SUSPENDED
    assert result.output["outcome"] == "pending"


@pytest.mark.asyncio
async def test_server_side_auto_approve(executor, mock_context, approval_step):
    """Portal returns AutoApproved → completed with approved outcome."""
    mock_context.variables["confidence"] = 0.85

    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "test-id-auto", "status": "AutoApproved"}
    mock_response.raise_for_status = MagicMock()

    with patch("agent.runtime.executors.approval.httpx.AsyncClient") as mock_client_cls:
        mock_client = AsyncMock()
        mock_client.post = AsyncMock(return_value=mock_response)
        mock_client.__aenter__ = AsyncMock(return_value=mock_client)
        mock_client.__aexit__ = AsyncMock(return_value=False)
        mock_client_cls.return_value = mock_client

        result = await executor.execute(
            step=approval_step,
            context=mock_context,
            rulesets={},
        )

    assert result.status == ExecutionStatus.COMPLETED
    assert result.output["outcome"] == "approved"
    assert result.output["auto_approved"] is True


def test_render_template_basic(executor):
    """Template rendering substitutes known variables."""
    ctx = MagicMock(spec=ExecutionContext)
    ctx.ticket_data = {"short_description": "Password reset"}
    ctx.variables = {"affected_user": "jsmith", "ticket_type": "password-reset"}

    result = executor._render_template(
        "Reset password for {{affected_user}}",
        ctx,
    )
    assert result == "Reset password for jsmith"


def test_render_template_missing_var(executor):
    """Missing variables stay as literal placeholders."""
    ctx = MagicMock(spec=ExecutionContext)
    ctx.ticket_data = {}
    ctx.variables = {}

    result = executor._render_template("Hello {{name}}", ctx)
    assert result == "Hello {{name}}"


def test_approval_registered_in_registry():
    """Verify ApprovalExecutor is registered in default_registry."""
    all_executors = default_registry.get_all()
    assert "Approval" in all_executors
    assert isinstance(all_executors["Approval"], ApprovalExecutor)


def test_suspended_status_exists():
    """Verify ExecutionStatus.SUSPENDED exists."""
    assert hasattr(ExecutionStatus, "SUSPENDED")
    assert ExecutionStatus.SUSPENDED.value == "suspended"
