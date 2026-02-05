"""Executor for SubWorkflow steps — runs a referenced workflow as a sub-step."""
from __future__ import annotations
import logging
from typing import Any, TYPE_CHECKING

from .base import BaseStepExecutor, StepExecutionError
from ..execution_context import ExecutionContext, ExecutionStatus, StepResult
from ..models import WorkflowStepExportInfo, RulesetExportInfo, WorkflowExportInfo, AgentExport

if TYPE_CHECKING:
    pass

logger = logging.getLogger(__name__)


class SubWorkflowExecutor(BaseStepExecutor):
    """
    Executes a referenced workflow as a sub-step.

    When the parent workflow engine encounters a SubWorkflow step, this executor:
    1. Looks up the referenced workflow from the export's sub_workflows dict
    2. Checks for circular references (workflow_stack)
    3. Creates a child WorkflowEngine with the sub-workflow definition
    4. Executes with SHARED context (same variables, same ticket data)
    5. Maps the child's terminal state to output for transition evaluation

    The output dict contains:
    - "outcome": "completed" | "escalated" | "failed"
    - "sub_workflow_name": name of the executed sub-workflow
    """

    @property
    def step_type(self) -> str:
        return "SubWorkflow"

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        # Get sub-workflow reference from step configuration
        config = step.configuration or {}
        workflow_id = config.get("workflow_id")
        workflow_name = config.get("workflow_name", "unknown")

        if not workflow_id:
            result.fail("SubWorkflow step has no referenced workflow_id in configuration")
            return result

        logger.info(f"SubWorkflow step '{step.name}' referencing workflow '{workflow_name}'")

        # Check recursion: is this workflow already on the stack?
        if workflow_name in context.workflow_stack:
            chain = " → ".join(context.workflow_stack + [workflow_name])
            result.fail(f"Circular workflow reference detected: {chain}")
            return result

        # Check depth limit
        if len(context.workflow_stack) >= context.MAX_WORKFLOW_DEPTH:
            result.fail(
                f"Maximum sub-workflow nesting depth ({context.MAX_WORKFLOW_DEPTH}) exceeded. "
                f"Stack: {' → '.join(context.workflow_stack)}"
            )
            return result

        # Look up sub-workflow definition from export stored on context
        export: AgentExport | None = getattr(context, '_agent_export', None)
        if not export:
            result.fail("Agent export not available in context — cannot resolve sub-workflow")
            return result

        sub_workflow_def = export.sub_workflows.get(workflow_name)
        if not sub_workflow_def:
            # Also try by ID in case name doesn't match
            for name, wf in export.sub_workflows.items():
                if str(getattr(wf, 'id', '')) == str(workflow_id):
                    sub_workflow_def = wf
                    workflow_name = name
                    break

        if not sub_workflow_def:
            result.fail(
                f"Sub-workflow '{workflow_name}' (id={workflow_id}) not found in export. "
                f"Available: {list(export.sub_workflows.keys())}"
            )
            return result

        # Push onto recursion stack
        context.workflow_stack.append(workflow_name)

        try:
            # Import here to avoid circular imports
            from ..workflow_engine import WorkflowEngine

            # Build a temporary export with the sub-workflow as the main workflow
            child_export = AgentExport(
                version=export.version,
                exported_at=export.exported_at,
                agent=export.agent,
                llm_provider=export.llm_provider,
                service_now=export.service_now,
                workflow=sub_workflow_def,
                rulesets=rulesets,  # Pass through all available rulesets
                example_sets=export.example_sets,
                required_capabilities=export.required_capabilities,
                sub_workflows=export.sub_workflows,  # Pass through for nested sub-workflows
            )

            child_engine = WorkflowEngine(
                export=child_export,
                llm_driver=context.llm_driver,
                admin_portal_url=context.admin_portal_url,
            )

            # Share integration points
            child_engine.servicenow_client = context.servicenow_client
            child_engine.capability_router = context.capability_router

            # Execute sub-workflow with SHARED context (Approach B)
            child_result = await child_engine.execute(
                ticket_id=context.ticket_id,
                ticket_data=context.ticket_data,
                context=context,  # Shared context
            )

            # Read sub-workflow terminal state from internal flags
            sub_status = getattr(context, '_sub_workflow_status', None)
            sub_escalation = getattr(context, '_sub_workflow_escalation', None)

            # Clean up internal flags
            if hasattr(context, '_sub_workflow_status'):
                del context._sub_workflow_status
            if hasattr(context, '_sub_workflow_escalation'):
                del context._sub_workflow_escalation

            # Map child result to parent transition output
            if sub_status == ExecutionStatus.COMPLETED:
                result.complete({
                    "outcome": "completed",
                    "sub_workflow_name": workflow_name,
                })
                logger.info(f"Sub-workflow '{workflow_name}' completed successfully")

            elif sub_status == ExecutionStatus.ESCALATED:
                result.complete({
                    "outcome": "escalated",
                    "sub_workflow_name": workflow_name,
                    "escalation_reason": sub_escalation,
                })
                logger.info(f"Sub-workflow '{workflow_name}' escalated: {sub_escalation}")

            else:
                result.complete({
                    "outcome": "failed",
                    "sub_workflow_name": workflow_name,
                    "error": sub_escalation or "Unknown sub-workflow failure",
                })
                logger.warning(f"Sub-workflow '{workflow_name}' failed: {sub_escalation}")

        except Exception as e:
            logger.error(f"Sub-workflow '{workflow_name}' execution error: {e}", exc_info=True)
            result.fail(f"Sub-workflow execution error: {e}")

        finally:
            # Pop from recursion stack
            if context.workflow_stack and context.workflow_stack[-1] == workflow_name:
                context.workflow_stack.pop()

        return result
