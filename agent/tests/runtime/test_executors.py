"""Tests for step executors."""
import pytest
from unittest.mock import MagicMock, AsyncMock

from agent.runtime.execution_context import ExecutionContext, ExecutionStatus
from agent.runtime.models import WorkflowStepExportInfo, StepType
from agent.runtime.executors import (
    TriggerExecutor,
    ValidateExecutor,
    EscalateExecutor,
    EndExecutor,
)


@pytest.fixture
def basic_context():
    """Create a basic execution context."""
    return ExecutionContext(
        ticket_id="INC0001234",
        ticket_data={
            "short_description": "Password reset for jsmith",
            "description": "User forgot password",
            "caller_id": "requester@example.com",
        }
    )


@pytest.fixture
def trigger_step():
    """Create a trigger step."""
    return WorkflowStepExportInfo(
        name="trigger-start",
        step_type=StepType.TRIGGER,
        configuration={"source": "servicenow"},
        sort_order=1,
    )


class TestTriggerExecutor:
    @pytest.mark.asyncio
    async def test_trigger_succeeds_with_valid_ticket(self, basic_context, trigger_step):
        executor = TriggerExecutor()
        result = await executor.execute(trigger_step, basic_context, {})

        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["triggered"] is True
        assert basic_context.get_variable("ticket_id") == "INC0001234"

    @pytest.mark.asyncio
    async def test_trigger_fails_missing_required_field(self, trigger_step):
        context = ExecutionContext(
            ticket_id="INC0001234",
            ticket_data={}  # Missing short_description
        )

        executor = TriggerExecutor()
        result = await executor.execute(trigger_step, context, {})

        assert result.status == ExecutionStatus.FAILED
        assert "Missing required" in result.error


class TestValidateExecutor:
    @pytest.mark.asyncio
    async def test_validate_passes_with_valid_user(self, basic_context):
        basic_context.set_variable("affected_user", "jsmith")
        basic_context.set_variable("ticket_type", "password_reset")

        step = WorkflowStepExportInfo(
            name="validate-request",
            step_type=StepType.VALIDATE,
            configuration={"checks": ["user_exists", "not_admin"]},
            sort_order=3,
        )

        executor = ValidateExecutor()
        result = await executor.execute(step, basic_context, {})

        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["valid"] is True

    @pytest.mark.asyncio
    async def test_validate_fails_admin_user(self, basic_context):
        basic_context.set_variable("affected_user", "admin")

        step = WorkflowStepExportInfo(
            name="validate-request",
            step_type=StepType.VALIDATE,
            configuration={"checks": ["not_admin"]},
            sort_order=3,
        )

        executor = ValidateExecutor()
        result = await executor.execute(step, basic_context, {})

        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["valid"] is False
        assert any("admin" in e for e in result.output["validation_errors"])


class TestEscalateExecutor:
    @pytest.mark.asyncio
    async def test_escalate_sets_reason(self, basic_context):
        basic_context.set_variable("confidence", 0.5)

        step = WorkflowStepExportInfo(
            name="escalate-to-human",
            step_type=StepType.ESCALATE,
            configuration={"target_group": "Level 2 Support"},
            sort_order=6,
        )

        executor = EscalateExecutor()
        result = await executor.execute(step, basic_context, {})

        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["escalated"] is True
        assert basic_context.status == ExecutionStatus.ESCALATED


class TestEndExecutor:
    @pytest.mark.asyncio
    async def test_end_completes_workflow(self, basic_context):
        step = WorkflowStepExportInfo(
            name="end",
            step_type=StepType.END,
            configuration={},
            sort_order=10,
        )

        executor = EndExecutor()
        result = await executor.execute(step, basic_context, {})

        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["ended"] is True
        assert basic_context.status == ExecutionStatus.COMPLETED
