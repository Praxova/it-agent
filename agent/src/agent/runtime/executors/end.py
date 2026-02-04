"""End step executor - marks workflow complete."""
from __future__ import annotations
import logging

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class EndExecutor(BaseStepExecutor):
    """
    Executes End steps - terminal step of workflow.

    Marks the workflow as complete and performs any cleanup.
    """

    @property
    def step_type(self) -> str:
        return StepType.END.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """Execute end step."""
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        # Mark workflow complete
        context.complete()

        result.complete({
            "ended": True,
            "final_status": context.status.value,
            "steps_executed": len(context.step_results),
        })

        logger.info(f"Workflow completed for ticket {context.ticket_id}")

        return result
