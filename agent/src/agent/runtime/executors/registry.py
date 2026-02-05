"""Registry for step executors."""
from __future__ import annotations
import logging

from .base import BaseStepExecutor
from .trigger import TriggerExecutor
from .classify import ClassifyExecutor
from .query import QueryExecutor
from .validate import ValidateExecutor
from .execute import ExecuteExecutor
from .update_ticket import UpdateTicketExecutor
from .notify import NotifyExecutor
from .escalate import EscalateExecutor
from .end import EndExecutor
from .sub_workflow import SubWorkflowExecutor

logger = logging.getLogger(__name__)


class ExecutorRegistry:
    """
    Registry of step executors.

    Provides factory method to get executor for a step type,
    and registers all built-in executors.
    """

    def __init__(self):
        self._executors: dict[str, BaseStepExecutor] = {}
        self._register_builtins()

    def _register_builtins(self):
        """Register all built-in executors."""
        builtins = [
            TriggerExecutor(),
            ClassifyExecutor(),
            QueryExecutor(),
            ValidateExecutor(),
            ExecuteExecutor(),
            UpdateTicketExecutor(),
            NotifyExecutor(),
            EscalateExecutor(),
            EndExecutor(),
            SubWorkflowExecutor(),
        ]

        for executor in builtins:
            self.register(executor)

    def register(self, executor: BaseStepExecutor):
        """Register a step executor."""
        self._executors[executor.step_type] = executor
        logger.debug(f"Registered executor for: {executor.step_type}")

    def get(self, step_type: str) -> BaseStepExecutor | None:
        """Get executor for a step type."""
        return self._executors.get(step_type)

    def get_all(self) -> dict[str, BaseStepExecutor]:
        """Get all registered executors."""
        return dict(self._executors)


# Global default registry
default_registry = ExecutorRegistry()
