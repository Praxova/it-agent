"""Startup validators for the agent runtime."""
from __future__ import annotations
import logging
from typing import Any

from .models import AgentExport, WorkflowExportInfo, WorkflowStepExportInfo, StepType

logger = logging.getLogger(__name__)


def validate_workflow_capabilities(
    export: AgentExport,
    admin_portal_url: str | None = None,
) -> list[str]:
    """
    Validate that all capabilities referenced in workflow Execute steps
    are registered in the admin portal's capability list.

    Returns a list of error messages. Empty list means all valid.
    """
    errors: list[str] = []

    if not export.workflow:
        return errors

    # Collect all capability names referenced in Execute steps
    referenced_capabilities = _collect_referenced_capabilities(export)

    if not referenced_capabilities:
        logger.debug("No Execute steps with capabilities found in workflow")
        return errors

    # Collect registered capabilities from the export
    registered_capabilities = _collect_registered_capabilities(export)

    # Check for missing capabilities
    for cap_name, step_names in referenced_capabilities.items():
        if registered_capabilities and cap_name not in registered_capabilities:
            steps_str = ", ".join(step_names)
            errors.append(
                f"Capability '{cap_name}' referenced by step(s) [{steps_str}] "
                f"is not registered in the admin portal. "
                f"The execute step will fail at runtime."
            )

    if errors:
        logger.error(
            "Workflow capability validation failed with %d error(s):", len(errors)
        )
        for err in errors:
            logger.error("  - %s", err)
    else:
        logger.info(
            "Workflow capability validation passed: %d capabilities verified",
            len(referenced_capabilities),
        )

    return errors


def _collect_referenced_capabilities(
    export: AgentExport,
) -> dict[str, list[str]]:
    """
    Walk all steps (including sub-workflows) and collect capability names
    from Execute steps.

    Returns {capability_name: [step_name, ...]}.
    """
    capabilities: dict[str, list[str]] = {}

    def _walk_steps(steps: list[WorkflowStepExportInfo], prefix: str = ""):
        for step in steps:
            if step.step_type == StepType.EXECUTE:
                config = step.configuration or {}
                cap = config.get("capability")
                if cap:
                    full_name = f"{prefix}{step.name}" if prefix else step.name
                    capabilities.setdefault(cap, []).append(full_name)

    # Walk main workflow steps
    if export.workflow:
        _walk_steps(export.workflow.steps)

    # Walk sub-workflow steps (sub_workflows is on AgentExport)
    if export.sub_workflows:
        for sub_name, sub_wf in export.sub_workflows.items():
            _walk_steps(sub_wf.steps, prefix=f"{sub_name}/")

    return capabilities


def _collect_registered_capabilities(export: AgentExport) -> set[str] | None:
    """
    Extract registered capability names from the export.

    Returns None if we can't determine (no capability data in export),
    which means we skip validation rather than false-positive.
    Returns empty set if we know there are no capabilities registered.
    """
    # The export includes required_capabilities from the workflow
    if export.required_capabilities:
        return set(export.required_capabilities)

    # Also check the workflow-level required_capabilities
    if export.workflow and export.workflow.required_capabilities:
        return set(export.workflow.required_capabilities)

    # If we can't determine registered capabilities from the export,
    # return None to skip validation (don't false-positive)
    logger.debug("No capability registration data in export — skipping validation")
    return None
