"""Base class for step executors."""
from __future__ import annotations
from abc import ABC, abstractmethod
from typing import Any, TYPE_CHECKING

if TYPE_CHECKING:
    from ..execution_context import ExecutionContext, StepResult
    from ..models import WorkflowStepExportInfo, RulesetExportInfo


class StepExecutionError(Exception):
    """Raised when step execution fails."""
    pass


class BaseStepExecutor(ABC):
    """
    Abstract base class for workflow step executors.

    Each step type (Trigger, Classify, Execute, etc.) has its own executor.
    """

    @property
    @abstractmethod
    def step_type(self) -> str:
        """Return the step type this executor handles."""
        pass

    @abstractmethod
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute the step.

        Args:
            step: Step definition from workflow
            context: Current execution context with ticket data and results
            rulesets: Available rulesets (both workflow-level and step-level)

        Returns:
            StepResult with output data

        Raises:
            StepExecutionError: If execution fails
        """
        pass

    def get_step_rulesets(
        self,
        step: WorkflowStepExportInfo,
        all_rulesets: dict[str, RulesetExportInfo],
    ) -> list[RulesetExportInfo]:
        """
        Get rulesets applicable to this step.

        Combines workflow-level rulesets with step-specific rulesets,
        sorted by priority.
        """
        result = []

        for mapping in step.ruleset_mappings:
            if not mapping.is_enabled:
                continue
            ruleset = all_rulesets.get(mapping.ruleset_name)
            if ruleset:
                result.append((mapping.priority, ruleset))

        # Sort by priority (lower = higher priority)
        result.sort(key=lambda x: x[0])
        return [r for _, r in result]

    def build_rules_prompt(self, rulesets: list[RulesetExportInfo]) -> str:
        """
        Build a prompt section from rulesets for LLM.

        Returns formatted rules text to include in LLM prompts.
        """
        if not rulesets:
            return ""

        lines = ["## Rules to Follow\n"]

        for ruleset in rulesets:
            lines.append(f"### {ruleset.display_name or ruleset.name}")
            if ruleset.description:
                lines.append(f"{ruleset.description}\n")

            for rule in sorted(ruleset.rules, key=lambda r: r.priority):
                if rule.is_enabled:
                    lines.append(f"- {rule.rule_text}")
            lines.append("")

        return "\n".join(lines)
