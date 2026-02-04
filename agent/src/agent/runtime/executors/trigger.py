"""Trigger step executor - workflow entry point."""
from __future__ import annotations
import logging
from datetime import datetime

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class TriggerExecutor(BaseStepExecutor):
    """
    Executes Trigger steps - the entry point of a workflow.

    Validates that required ticket data is present and initializes
    workflow variables from step configuration.
    """

    @property
    def step_type(self) -> str:
        return StepType.TRIGGER.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute trigger step.

        Configuration options:
        - required_fields: List of ticket fields that must be present
        - init_variables: Dict of variables to initialize in context
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}

        # Validate required fields
        required_fields = config.get("required_fields", ["short_description"])
        missing_fields = []

        for field in required_fields:
            if field not in context.ticket_data or not context.ticket_data[field]:
                missing_fields.append(field)

        if missing_fields:
            result.fail(f"Missing required ticket fields: {', '.join(missing_fields)}")
            return result

        # Initialize variables from config
        init_vars = config.get("init_variables", {})
        for key, value in init_vars.items():
            context.set_variable(key, value)

        # Extract common fields to variables for easy access
        context.set_variable("ticket_id", context.ticket_id)
        context.set_variable("short_description", context.ticket_data.get("short_description", ""))
        context.set_variable("description", context.ticket_data.get("description", ""))
        context.set_variable("caller", context.ticket_data.get("caller_id", ""))
        context.set_variable("triggered_at", datetime.utcnow().isoformat())

        logger.info(f"Trigger activated for ticket {context.ticket_id}")

        result.complete({
            "triggered": True,
            "ticket_id": context.ticket_id,
            "source": config.get("source", "servicenow"),
        })

        return result
