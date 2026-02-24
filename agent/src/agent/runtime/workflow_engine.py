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
from .validators import validate_workflow_capabilities

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
        agent_name: str = "unknown",
        executor_registry: ExecutorRegistry | None = None,
    ):
        """
        Initialize workflow engine.

        Args:
            export: Loaded agent export with workflow definition
            llm_driver: Configured LLM driver for classification steps
            admin_portal_url: URL for capability routing
            agent_name: Name of the agent running this engine
            executor_registry: Optional custom executor registry (uses default if not provided)
        """
        self.export = export
        self.llm_driver = llm_driver
        self.admin_portal_url = admin_portal_url
        self.agent_name = agent_name
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
        self.portal_client: Any = None  # httpx.AsyncClient with auth headers

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

        # Validate workflow capabilities against registered capabilities
        validation_errors = validate_workflow_capabilities(export)
        if validation_errors:
            logger.warning(
                "Capability validation found %d issue(s). "
                "Some execute steps may fail at runtime. "
                "Check capability mappings in the Admin Portal.",
                len(validation_errors),
            )

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
        context: ExecutionContext | None = None,
    ) -> ExecutionContext:
        """
        Execute workflow for a ticket.

        Args:
            ticket_id: ID of the ticket being processed
            ticket_data: Ticket fields (short_description, description, etc.)
            context: Optional existing context (for sub-workflow shared context).
                     If None, creates a new context.

        Returns:
            ExecutionContext with results from all steps
        """
        if not self.export.workflow:
            raise WorkflowExecutionError("No workflow defined in agent export")

        # Detect sub-workflow mode
        is_sub_workflow = context is not None

        # Create or reuse execution context
        if context is None:
            context = ExecutionContext(
                ticket_id=ticket_id,
                ticket_data=ticket_data,
                llm_driver=self.llm_driver,
                admin_portal_url=self.admin_portal_url,
            )
            context.servicenow_client = self.servicenow_client
            context.capability_router = self.capability_router
            context.portal_client = self.portal_client
            context.variables["agent_name"] = self.agent_name
            context.status = ExecutionStatus.RUNNING

            # Initialize workflow stack for top-level workflow
            if self.export.workflow:
                context.workflow_stack.append(self.export.workflow.name)

        # Store export for sub-workflow resolution
        context._agent_export = self.export

        # Find start step
        current_step = self.get_start_step()
        if not current_step:
            raise WorkflowExecutionError("No start step found in workflow")

        wf_name = self.export.workflow.name if self.export.workflow else "unknown"
        logger.info(f"Starting workflow execution for ticket {ticket_id} "
                     f"(workflow: {wf_name}, sub={is_sub_workflow})")

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

            # Check for suspended state (approval pending)
            if result.status == ExecutionStatus.SUSPENDED:
                if is_sub_workflow:
                    context._sub_workflow_status = ExecutionStatus.SUSPENDED
                else:
                    context.status = ExecutionStatus.SUSPENDED
                logger.info(f"Workflow suspended at step {current_step.name} "
                            f"(approval pending, ticket: {ticket_id})")
                break

            # Check for terminal states
            if result.status == ExecutionStatus.FAILED:
                if is_sub_workflow:
                    context._sub_workflow_status = ExecutionStatus.FAILED
                    context._sub_workflow_escalation = result.error or "Step failed"
                else:
                    context.fail(result.error or "Step failed")
                break

            if current_step.step_type == StepType.END:
                if is_sub_workflow:
                    context._sub_workflow_status = ExecutionStatus.COMPLETED
                else:
                    context.complete()
                break

            if current_step.step_type == StepType.ESCALATE:
                if is_sub_workflow:
                    context._sub_workflow_status = ExecutionStatus.ESCALATED
                    context._sub_workflow_escalation = result.output.get("reason", "Escalated")
                else:
                    context.escalate(result.output.get("reason", "Escalated by workflow"))
                break

            # Find next step based on transitions
            next_step = await self._find_next_step(current_step.name, context)

            if next_step is None:
                # No valid transition - workflow complete
                logger.info(f"No outgoing transition from {current_step.name}, completing")
                if is_sub_workflow:
                    context._sub_workflow_status = ExecutionStatus.COMPLETED
                else:
                    context.complete()
                break

            current_step = next_step

        if step_count >= max_steps:
            if is_sub_workflow:
                context._sub_workflow_status = ExecutionStatus.FAILED
                context._sub_workflow_escalation = "Maximum step count exceeded - possible infinite loop"
            else:
                context.fail("Maximum step count exceeded - possible infinite loop")

        logger.info(f"Workflow execution complete: {context.status} "
                     f"(workflow: {wf_name}, sub={is_sub_workflow})")
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

    async def resume(
        self,
        context_snapshot: dict[str, Any],
        ticket_id: str,
        ticket_data: dict[str, Any],
        resume_after_step: str,
        outcome: str = "approved",
    ) -> ExecutionContext:
        """
        Resume a suspended workflow after approval decision.

        Rebuilds execution context from the stored snapshot,
        sets the approval outcome, and continues execution
        from the step after the approval node.

        Args:
            context_snapshot: Saved context variables dict
            ticket_id: Ticket being processed
            ticket_data: Original ticket data
            resume_after_step: Name of the Approval step to resume after
            outcome: "approved" or "rejected"

        Returns:
            ExecutionContext with results from remaining steps
        """
        if not self.export.workflow:
            raise WorkflowExecutionError("No workflow defined in agent export")

        # Create fresh execution context
        context = ExecutionContext(
            ticket_id=ticket_id,
            ticket_data=ticket_data,
            llm_driver=self.llm_driver,
            admin_portal_url=self.admin_portal_url,
        )

        # Restore variables from snapshot
        context.variables = dict(context_snapshot)
        # Remove internal keys that shouldn't be in variables
        context.variables.pop("_ticket_data", None)
        context.variables.pop("_workflow_stack", None)

        # Set outcome from approval decision
        context.variables["outcome"] = outcome

        # Inject integration points
        context.servicenow_client = self.servicenow_client
        context.capability_router = self.capability_router
        context.portal_client = self.portal_client
        context.status = ExecutionStatus.RUNNING

        # Restore workflow stack from snapshot, or initialize fresh
        saved_stack = context_snapshot.get("_workflow_stack")
        if saved_stack and isinstance(saved_stack, list):
            context.workflow_stack = list(saved_stack)
            logger.debug(f"Restored workflow stack: {context.workflow_stack}")
        elif self.export.workflow:
            context.workflow_stack.append(self.export.workflow.name)

        # Store export for sub-workflow resolution
        context._agent_export = self.export

        # Find the approval step
        approval_step = self._steps.get(resume_after_step)
        if not approval_step:
            logger.warning(f"Resume step '{resume_after_step}' not found in workflow")
            context.complete()
            return context

        # Find next step after the approval step using transitions
        next_step = await self._find_next_step(resume_after_step, context)
        if not next_step:
            logger.warning(f"No transition from '{resume_after_step}' for outcome '{outcome}'")
            context.complete()
            return context

        wf_name = self.export.workflow.name if self.export.workflow else "unknown"
        logger.info(f"Resuming workflow '{wf_name}' for ticket {ticket_id} "
                     f"after step '{resume_after_step}' with outcome '{outcome}'")

        # Execute from the next step using the same loop logic
        current_step = next_step
        max_steps = 100
        step_count = 0

        while current_step and step_count < max_steps:
            step_count += 1
            context.current_step = current_step.name

            logger.info(f"Executing step: {current_step.name} ({current_step.step_type})")

            result = await self._execute_step(current_step, context)
            context.record_step_result(result)

            # Check for suspended state (another approval)
            if result.status == ExecutionStatus.SUSPENDED:
                context.status = ExecutionStatus.SUSPENDED
                logger.info(f"Workflow suspended at step {current_step.name} "
                            f"(approval pending, ticket: {ticket_id})")
                break

            if result.status == ExecutionStatus.FAILED:
                context.fail(result.error or "Step failed")
                break

            if current_step.step_type == StepType.END:
                context.complete()
                break

            if current_step.step_type == StepType.ESCALATE:
                context.escalate(result.output.get("reason", "Escalated by workflow"))
                break

            next_step = await self._find_next_step(current_step.name, context)
            if next_step is None:
                logger.info(f"No outgoing transition from {current_step.name}, completing")
                context.complete()
                break

            current_step = next_step

        if step_count >= max_steps:
            context.fail("Maximum step count exceeded - possible infinite loop")

        logger.info(f"Resumed workflow complete: {context.status} "
                     f"(workflow: {wf_name})")
        return context

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

        # Defensive fallback: if context has an "outcome" variable (set by resume()),
        # try matching transitions by label instead of condition.
        # This handles the case where transitions have labels but no conditions.
        outcome = eval_context.get("outcome")
        if outcome:
            for trans in transitions:
                if trans.label and trans.label.lower() == outcome.lower():
                    logger.info(f"Transition matched by label fallback: {trans.from_step_name} -> "
                               f"{trans.to_step_name} (label: {trans.label}, outcome: {outcome})")
                    return self._steps.get(trans.to_step_name)

        # No condition matched
        logger.warning(f"No transition condition matched for step {current_step_name}")
        return None
