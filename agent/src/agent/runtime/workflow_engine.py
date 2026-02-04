"""Core workflow execution engine."""
from __future__ import annotations
import logging
from typing import Any, TYPE_CHECKING

from .models import (
    AgentExport,
    WorkflowStepExportInfo,
    WorkflowTransitionExportInfo,
    StepType,
)
from .execution_context import ExecutionContext, ExecutionStatus, StepResult
from .condition_evaluator import ConditionEvaluator
from .executors.base import BaseStepExecutor
from .executors.registry import default_registry, ExecutorRegistry

if TYPE_CHECKING:
    from griptape.drivers.prompt import BasePromptDriver

logger = logging.getLogger(__name__)


class WorkflowExecutionError(Exception):
    """Raised when workflow execution fails."""
    pass


class WorkflowEngine:
    """
    Executes workflows defined in agent export.

    Parses steps and transitions, executes steps in order following
    transition conditions, until reaching End or Escalate.
    """

    def __init__(
        self,
        export: AgentExport,
        llm_driver: BasePromptDriver,
        admin_portal_url: str = "",
        executor_registry: ExecutorRegistry | None = None,
    ):
        """
        Initialize workflow engine.

        Args:
            export: Loaded agent export with workflow definition
            llm_driver: Configured LLM driver for classification steps
            admin_portal_url: URL for capability routing
            executor_registry: Optional custom executor registry (uses default if not provided)
        """
        self.export = export
        self.llm_driver = llm_driver
        self.admin_portal_url = admin_portal_url
        self.condition_evaluator = ConditionEvaluator()

        # Registry of step executors (populated by register_executor)
        self._executors: dict[str, BaseStepExecutor] = {}

        # Use provided registry or default
        self._registry = executor_registry or default_registry

        # Register all executors from registry
        for step_type, executor in self._registry.get_all().items():
            self.register_executor(executor)

        # Integration points (set by runner)
        self.servicenow_client: Any = None  # ServiceNowClient
        self.capability_router: Any = None  # CapabilityRouter

        # Build step lookup
        self._steps: dict[str, WorkflowStepExportInfo] = {}
        self._transitions: dict[str, list[WorkflowTransitionExportInfo]] = {}

        if export.workflow:
            for step in export.workflow.steps:
                self._steps[step.name] = step

            # Group transitions by source step
            for trans in export.workflow.transitions:
                if trans.from_step_name not in self._transitions:
                    self._transitions[trans.from_step_name] = []
                self._transitions[trans.from_step_name].append(trans)

    def register_executor(self, executor: BaseStepExecutor):
        """Register a step executor for a step type."""
        self._executors[executor.step_type] = executor
        logger.debug(f"Registered executor for step type: {executor.step_type}")

    def get_start_step(self) -> WorkflowStepExportInfo | None:
        """Find the trigger/start step."""
        for step in self._steps.values():
            if step.step_type == StepType.TRIGGER:
                return step

        # Fallback: first step by sort order
        if self._steps:
            return min(self._steps.values(), key=lambda s: s.sort_order)
        return None

    async def execute(
        self,
        ticket_id: str,
        ticket_data: dict[str, Any],
    ) -> ExecutionContext:
        """
        Execute workflow for a ticket.

        Args:
            ticket_id: ID of the ticket being processed
            ticket_data: Ticket fields (short_description, description, etc.)

        Returns:
            ExecutionContext with results from all steps
        """
        if not self.export.workflow:
            raise WorkflowExecutionError("No workflow defined in agent export")

        # Create execution context
        context = ExecutionContext(
            ticket_id=ticket_id,
            ticket_data=ticket_data,
            llm_driver=self.llm_driver,
            admin_portal_url=self.admin_portal_url,
        )

        # Inject integrations
        context.servicenow_client = self.servicenow_client
        context.capability_router = self.capability_router

        context.status = ExecutionStatus.RUNNING

        # Find start step
        current_step = self.get_start_step()
        if not current_step:
            raise WorkflowExecutionError("No start step found in workflow")

        logger.info(f"Starting workflow execution for ticket {ticket_id}")

        # Execute steps until we reach an end state
        max_steps = 100  # Prevent infinite loops
        step_count = 0

        while current_step and step_count < max_steps:
            step_count += 1
            context.current_step = current_step.name

            logger.info(f"Executing step: {current_step.name} ({current_step.step_type})")

            # Execute the step
            result = await self._execute_step(current_step, context)
            context.record_step_result(result)

            # Check for terminal states
            if result.status == ExecutionStatus.FAILED:
                context.fail(result.error or "Step failed")
                break

            if current_step.step_type == StepType.END:
                context.complete()
                break

            if current_step.step_type == StepType.ESCALATE:
                context.escalate(result.output.get("reason", "Escalated by workflow"))
                break

            # Find next step based on transitions
            next_step = await self._find_next_step(current_step.name, context)

            if next_step is None:
                # No valid transition - workflow complete
                logger.info(f"No outgoing transition from {current_step.name}, completing")
                context.complete()
                break

            current_step = next_step

        if step_count >= max_steps:
            context.fail("Maximum step count exceeded - possible infinite loop")

        logger.info(f"Workflow execution complete: {context.status}")
        return context

    async def _execute_step(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
    ) -> StepResult:
        """Execute a single step using its registered executor."""
        # Get executor for this step type
        executor = self._executors.get(step.step_type.value)

        if not executor:
            logger.warning(f"No executor for step type {step.step_type}, using passthrough")
            # Return passthrough result
            return StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.COMPLETED,
                output={"passthrough": True},
            )

        try:
            return await executor.execute(
                step=step,
                context=context,
                rulesets=self.export.rulesets,
            )
        except Exception as e:
            logger.error(f"Step {step.name} failed: {e}")
            result = StepResult(
                step_name=step.name,
                step_type=step.step_type.value,
                status=ExecutionStatus.FAILED,
            )
            result.fail(str(e))
            return result

    async def _find_next_step(
        self,
        current_step_name: str,
        context: ExecutionContext,
    ) -> WorkflowStepExportInfo | None:
        """Find next step by evaluating transition conditions."""
        transitions = self._transitions.get(current_step_name, [])

        if not transitions:
            return None

        # Build evaluation context from step results
        eval_context = context.to_evaluation_context()

        # Evaluate each transition's condition
        for trans in transitions:
            try:
                if self.condition_evaluator.evaluate(trans.condition, eval_context):
                    logger.debug(f"Transition matched: {trans.from_step_name} -> "
                               f"{trans.to_step_name} (condition: {trans.condition})")
                    return self._steps.get(trans.to_step_name)
            except Exception as e:
                logger.warning(f"Failed to evaluate condition '{trans.condition}': {e}")

        # No condition matched
        logger.warning(f"No transition condition matched for step {current_step_name}")
        return None
