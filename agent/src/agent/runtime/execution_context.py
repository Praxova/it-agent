"""Execution context for workflow runs."""
from __future__ import annotations
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any
from enum import Enum

from griptape.drivers.prompt import BasePromptDriver


class ExecutionStatus(str, Enum):
    """Status of workflow execution."""
    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    ESCALATED = "escalated"
    FAILED = "failed"
    SUSPENDED = "suspended"


@dataclass
class StepResult:
    """Result from executing a single step."""
    step_name: str
    step_type: str
    status: ExecutionStatus
    output: dict[str, Any] = field(default_factory=dict)
    error: str | None = None
    started_at: datetime = field(default_factory=datetime.utcnow)
    completed_at: datetime | None = None

    def complete(self, output: dict[str, Any] | None = None):
        """Mark step as completed."""
        self.status = ExecutionStatus.COMPLETED
        self.completed_at = datetime.utcnow()
        if output:
            self.output.update(output)

    def fail(self, error: str):
        """Mark step as failed."""
        self.status = ExecutionStatus.FAILED
        self.error = error
        self.completed_at = datetime.utcnow()


@dataclass
class ExecutionContext:
    """
    Shared context during workflow execution.

    Holds ticket data, step results, and configured drivers/clients.
    Passed to each step executor.
    """
    # Ticket being processed
    ticket_id: str
    ticket_data: dict[str, Any]

    # Configured drivers (set by runtime before execution)
    llm_driver: BasePromptDriver | None = None

    # Admin portal URL for capability routing
    admin_portal_url: str = ""

    # Agent identity (needed for operation token requests)
    agent_name: str = ""

    # Integration points (optional, set by runner)
    servicenow_client: Any = None  # ServiceNowClient
    capability_router: Any = None  # CapabilityRouter
    portal_client: Any = None  # httpx.AsyncClient with auth headers

    # Accumulated results from each step
    step_results: dict[str, StepResult] = field(default_factory=dict)

    # Current workflow variables (can be set/read by steps)
    variables: dict[str, Any] = field(default_factory=dict)

    # Overall execution status
    status: ExecutionStatus = ExecutionStatus.PENDING

    # Execution timing
    started_at: datetime = field(default_factory=datetime.utcnow)
    completed_at: datetime | None = None

    # Current step name (for tracking)
    current_step: str | None = None

    # Escalation info (if escalated)
    escalation_reason: str | None = None

    # Sub-workflow recursion tracking
    workflow_stack: list[str] = field(default_factory=list)

    # Maximum nesting depth for sub-workflows
    MAX_WORKFLOW_DEPTH: int = 10

    def get_step_result(self, step_name: str) -> StepResult | None:
        """Get result from a previously executed step."""
        return self.step_results.get(step_name)

    def get_step_output(self, step_name: str, key: str, default: Any = None) -> Any:
        """Get specific output value from a step result."""
        result = self.step_results.get(step_name)
        if result:
            return result.output.get(key, default)
        return default

    def set_variable(self, key: str, value: Any):
        """Set a workflow variable."""
        self.variables[key] = value

    def get_variable(self, key: str, default: Any = None) -> Any:
        """Get a workflow variable."""
        return self.variables.get(key, default)

    def record_step_result(self, result: StepResult):
        """Record a step's execution result."""
        self.step_results[result.step_name] = result

    def escalate(self, reason: str):
        """Mark execution as escalated."""
        self.status = ExecutionStatus.ESCALATED
        self.escalation_reason = reason
        self.completed_at = datetime.utcnow()

    def complete(self):
        """Mark execution as completed."""
        self.status = ExecutionStatus.COMPLETED
        self.completed_at = datetime.utcnow()

    def fail(self, error: str):
        """Mark execution as failed."""
        self.status = ExecutionStatus.FAILED
        self.escalation_reason = error
        self.completed_at = datetime.utcnow()

    def to_evaluation_context(self) -> dict[str, Any]:
        """
        Build context dict for condition evaluation.

        Includes all step outputs and variables, flattened for easy access.
        Example: confidence, valid, success, ticket_type, etc.
        """
        ctx = dict(self.variables)

        # Add outputs from all steps
        for step_name, result in self.step_results.items():
            # Add prefixed: classify_ticket.confidence
            for key, value in result.output.items():
                ctx[f"{step_name.replace('-', '_')}.{key}"] = value
                # Also add unprefixed for convenience
                if key not in ctx:
                    ctx[key] = value

        # Add ticket data
        ctx["ticket"] = self.ticket_data
        ctx["ticket_id"] = self.ticket_id

        return ctx
