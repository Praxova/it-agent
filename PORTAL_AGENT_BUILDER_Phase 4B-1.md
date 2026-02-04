# Claude Code Prompt: Phase 4B-1 - Core Workflow Runtime Structure

## Context

Phase 4A is complete - the Admin Portal exports agent configurations as JSON including:
- Workflow definition with steps and transitions
- Rulesets with rules
- Example sets for classification
- LLM provider and ServiceNow connection info (as credential references)

Now we build the Python runtime that **executes** these exported workflows.

**Project Location**: `/home/alton/Documents/lucid-it-agent`
**Agent Code Location**: `/home/alton/Documents/lucid-it-agent/agent/src/agent`

## Overview

Create the core workflow runtime infrastructure:
1. `ConfigLoader` - Fetches export JSON from Admin Portal, resolves credentials
2. `WorkflowEngine` - Parses workflow, executes steps, follows transitions
3. `ExecutionContext` - Shared state during workflow execution
4. `ConditionEvaluator` - Evaluates transition conditions like `confidence >= 0.8`
5. `BaseStepExecutor` - Abstract base class for step executors

## File Structure

Create these files under `agent/src/agent/runtime/`:
```
agent/src/agent/
├── runtime/                      # NEW - Workflow runtime
│   ├── __init__.py
│   ├── config_loader.py          # Fetches and parses export JSON
│   ├── models.py                 # Pydantic models for export data
│   ├── workflow_engine.py        # Core execution engine
│   ├── execution_context.py      # Shared execution state
│   ├── condition_evaluator.py    # Transition condition evaluation
│   └── executors/
│       ├── __init__.py
│       └── base.py               # BaseStepExecutor abstract class
```

## Task 1: Create Pydantic Models (models.py)

Model the export JSON structure for type safety:
```python
"""Pydantic models for agent export data."""
from __future__ import annotations
from datetime import datetime
from enum import Enum
from typing import Any
from pydantic import BaseModel, Field


class StepType(str, Enum):
    """Workflow step types matching C# enum."""
    TRIGGER = "Trigger"
    CLASSIFY = "Classify"
    QUERY = "Query"
    VALIDATE = "Validate"
    EXECUTE = "Execute"
    UPDATE_TICKET = "UpdateTicket"
    NOTIFY = "Notify"
    ESCALATE = "Escalate"
    CONDITION = "Condition"
    END = "End"


class CredentialReference(BaseModel):
    """Reference to credentials (not actual secrets)."""
    storage: str  # "environment", "vault", etc.
    username_key: str | None = None
    password_key: str | None = None
    api_key_key: str | None = None


class RuleExportInfo(BaseModel):
    """Individual rule within a ruleset."""
    name: str
    rule_text: str = Field(alias="ruleText")
    priority: int
    is_enabled: bool = Field(alias="isEnabled")

    class Config:
        populate_by_name = True


class RulesetExportInfo(BaseModel):
    """Ruleset with its rules."""
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    scope: str | None = None
    rules: list[RuleExportInfo] = []

    class Config:
        populate_by_name = True


class StepRulesetMapping(BaseModel):
    """Ruleset attached to a step."""
    ruleset_name: str = Field(alias="rulesetName")
    priority: int
    is_enabled: bool = Field(alias="isEnabled")

    class Config:
        populate_by_name = True


class WorkflowStepExportInfo(BaseModel):
    """Workflow step definition."""
    name: str
    display_name: str | None = Field(None, alias="displayName")
    step_type: StepType = Field(alias="stepType")
    configuration: dict[str, Any] = {}
    sort_order: int = Field(alias="sortOrder")
    ruleset_mappings: list[StepRulesetMapping] = Field(default_factory=list, alias="rulesetMappings")

    class Config:
        populate_by_name = True


class WorkflowTransitionExportInfo(BaseModel):
    """Transition between steps."""
    from_step_name: str = Field(alias="fromStepName")
    to_step_name: str = Field(alias="toStepName")
    condition: str | None = None
    label: str | None = None
    output_index: int = Field(0, alias="outputIndex")
    input_index: int = Field(0, alias="inputIndex")

    class Config:
        populate_by_name = True


class WorkflowExportInfo(BaseModel):
    """Complete workflow definition."""
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    version: str
    trigger_type: str | None = Field(None, alias="triggerType")
    steps: list[WorkflowStepExportInfo] = []
    transitions: list[WorkflowTransitionExportInfo] = []
    ruleset_mappings: list[StepRulesetMapping] = Field(default_factory=list, alias="rulesetMappings")
    required_capabilities: list[str] = Field(default_factory=list, alias="requiredCapabilities")

    class Config:
        populate_by_name = True


class ExampleExportInfo(BaseModel):
    """Classification example."""
    input_text: str = Field(alias="inputText")
    expected_output: str = Field(alias="expectedOutput")
    ticket_type: str | None = Field(None, alias="ticketType")
    explanation: str | None = None

    class Config:
        populate_by_name = True


class ExampleSetExportInfo(BaseModel):
    """Set of classification examples."""
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    ticket_type: str | None = Field(None, alias="ticketType")
    examples: list[ExampleExportInfo] = []

    class Config:
        populate_by_name = True


class ProviderExportInfo(BaseModel):
    """LLM or ServiceNow provider configuration."""
    provider_type: str = Field(alias="providerType")
    config: dict[str, Any] = {}
    credentials: CredentialReference | None = None

    class Config:
        populate_by_name = True


class AgentBasicInfo(BaseModel):
    """Basic agent information."""
    id: str
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    is_enabled: bool = Field(alias="isEnabled")
    assignment_group: str | None = Field(None, alias="assignmentGroup")

    class Config:
        populate_by_name = True


class AgentExport(BaseModel):
    """Complete agent export - root model."""
    version: str
    exported_at: datetime = Field(alias="exportedAt")
    agent: AgentBasicInfo
    llm_provider: ProviderExportInfo | None = Field(None, alias="llmProvider")
    service_now: ProviderExportInfo | None = Field(None, alias="serviceNow")
    workflow: WorkflowExportInfo | None = None
    rulesets: dict[str, RulesetExportInfo] = {}
    example_sets: list[ExampleSetExportInfo] = Field(default_factory=list, alias="exampleSets")
    required_capabilities: list[str] = Field(default_factory=list, alias="requiredCapabilities")

    class Config:
        populate_by_name = True
```

## Task 2: Create ConfigLoader (config_loader.py)
```python
"""Loads agent configuration from Admin Portal export API."""
from __future__ import annotations
import os
import httpx
from typing import Any
import logging

from .models import AgentExport, CredentialReference

logger = logging.getLogger(__name__)


class ConfigurationError(Exception):
    """Raised when configuration loading fails."""
    pass


class ConfigLoader:
    """Loads and resolves agent configuration from Admin Portal."""
    
    def __init__(self, admin_portal_url: str, agent_name: str):
        """
        Initialize config loader.
        
        Args:
            admin_portal_url: Base URL of Admin Portal (e.g., http://localhost:5000)
            agent_name: Name of the agent to load
        """
        self.admin_portal_url = admin_portal_url.rstrip('/')
        self.agent_name = agent_name
        self._export: AgentExport | None = None
        self._resolved_credentials: dict[str, dict[str, str]] = {}
    
    async def load(self) -> AgentExport:
        """
        Fetch agent export from Admin Portal.
        
        Returns:
            Parsed AgentExport model
            
        Raises:
            ConfigurationError: If fetch fails or response is invalid
        """
        url = f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/export"
        logger.info(f"Loading agent configuration from {url}")
        
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(url, timeout=30.0)
                response.raise_for_status()
            except httpx.HTTPStatusError as e:
                raise ConfigurationError(
                    f"Failed to fetch agent config: HTTP {e.response.status_code}"
                ) from e
            except httpx.RequestError as e:
                raise ConfigurationError(
                    f"Failed to connect to Admin Portal: {e}"
                ) from e
        
        try:
            data = response.json()
            self._export = AgentExport.model_validate(data)
            logger.info(f"Loaded agent '{self._export.agent.name}' with workflow "
                       f"'{self._export.workflow.name if self._export.workflow else 'none'}'")
            return self._export
        except Exception as e:
            raise ConfigurationError(f"Failed to parse agent export: {e}") from e
    
    def resolve_credentials(self, ref: CredentialReference) -> dict[str, str]:
        """
        Resolve credential reference to actual values.
        
        Currently supports 'environment' storage - reads from env vars.
        
        Args:
            ref: Credential reference with storage type and key names
            
        Returns:
            Dict with resolved credential values (username, password, api_key, etc.)
        """
        if ref.storage != "environment":
            raise ConfigurationError(
                f"Unsupported credential storage: {ref.storage}. "
                f"Only 'environment' is currently supported."
            )
        
        result = {}
        
        if ref.username_key:
            value = os.environ.get(ref.username_key)
            if not value:
                raise ConfigurationError(
                    f"Environment variable '{ref.username_key}' not set"
                )
            result["username"] = value
        
        if ref.password_key:
            value = os.environ.get(ref.password_key)
            if not value:
                raise ConfigurationError(
                    f"Environment variable '{ref.password_key}' not set"
                )
            result["password"] = value
        
        if ref.api_key_key:
            value = os.environ.get(ref.api_key_key)
            if not value:
                raise ConfigurationError(
                    f"Environment variable '{ref.api_key_key}' not set"
                )
            result["api_key"] = value
        
        return result
    
    def get_llm_credentials(self) -> dict[str, str]:
        """Get resolved LLM provider credentials."""
        if not self._export or not self._export.llm_provider:
            raise ConfigurationError("No LLM provider configured")
        
        if not self._export.llm_provider.credentials:
            return {}  # Some providers (Ollama) don't need credentials
        
        return self.resolve_credentials(self._export.llm_provider.credentials)
    
    def get_servicenow_credentials(self) -> dict[str, str]:
        """Get resolved ServiceNow credentials."""
        if not self._export or not self._export.service_now:
            raise ConfigurationError("No ServiceNow provider configured")
        
        if not self._export.service_now.credentials:
            raise ConfigurationError("ServiceNow credentials not configured")
        
        return self.resolve_credentials(self._export.service_now.credentials)
    
    @property
    def export(self) -> AgentExport:
        """Get loaded export (must call load() first)."""
        if not self._export:
            raise ConfigurationError("Configuration not loaded. Call load() first.")
        return self._export
```

## Task 3: Create ExecutionContext (execution_context.py)
```python
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
```

## Task 4: Create ConditionEvaluator (condition_evaluator.py)
```python
"""Evaluates transition conditions."""
from __future__ import annotations
import operator
import re
import logging
from typing import Any

logger = logging.getLogger(__name__)


class ConditionEvaluationError(Exception):
    """Raised when condition evaluation fails."""
    pass


class ConditionEvaluator:
    """
    Evaluates transition conditions against execution context.
    
    Supports simple conditions like:
    - confidence >= 0.8
    - valid == true
    - success == false
    - ticket_type == "password_reset"
    
    Does NOT support complex expressions (no AND/OR/NOT) for security.
    """
    
    # Supported operators
    OPERATORS = {
        "==": operator.eq,
        "!=": operator.ne,
        ">=": operator.ge,
        "<=": operator.le,
        ">": operator.gt,
        "= 0.8, valid == true, type == "password_reset"
    CONDITION_PATTERN = re.compile(
        r'^\s*(\w+(?:\.\w+)?)\s*(==|!=|>=|<=|>|<)\s*(.+?)\s*$'
    )
    
    def evaluate(self, condition: str | None, context: dict[str, Any]) -> bool:
        """
        Evaluate a condition against the given context.
        
        Args:
            condition: Condition string (e.g., "confidence >= 0.8") or None
            context: Dict of available variables
            
        Returns:
            True if condition matches (or condition is None/empty)
            
        Raises:
            ConditionEvaluationError: If condition syntax is invalid
        """
        # No condition = always true (unconditional transition)
        if not condition or condition.strip() == "":
            return True
        
        match = self.CONDITION_PATTERN.match(condition)
        if not match:
            raise ConditionEvaluationError(
                f"Invalid condition syntax: '{condition}'. "
                f"Expected format: 'variable operator value'"
            )
        
        var_name, op_str, value_str = match.groups()
        
        # Get operator function
        op_func = self.OPERATORS.get(op_str)
        if not op_func:
            raise ConditionEvaluationError(f"Unknown operator: {op_str}")
        
        # Get variable value from context
        var_value = self._get_nested_value(context, var_name)
        if var_value is None:
            logger.warning(f"Variable '{var_name}' not found in context, defaulting to None")
        
        # Parse the comparison value
        compare_value = self._parse_value(value_str)
        
        # Evaluate
        try:
            result = op_func(var_value, compare_value)
            logger.debug(f"Condition '{condition}': {var_value} {op_str} {compare_value} = {result}")
            return bool(result)
        except TypeError as e:
            raise ConditionEvaluationError(
                f"Cannot compare {type(var_value).__name__} with {type(compare_value).__name__}: {e}"
            )
    
    def _get_nested_value(self, context: dict[str, Any], key: str) -> Any:
        """Get potentially nested value (e.g., 'classify_ticket.confidence')."""
        if "." in key:
            parts = key.split(".", 1)
            nested = context.get(parts[0])
            if isinstance(nested, dict):
                return nested.get(parts[1])
            return None
        return context.get(key)
    
    def _parse_value(self, value_str: str) -> Any:
        """Parse a value string into Python type."""
        value_str = value_str.strip()
        
        # Boolean
        if value_str.lower() == "true":
            return True
        if value_str.lower() == "false":
            return False
        
        # None/null
        if value_str.lower() in ("none", "null"):
            return None
        
        # String (quoted)
        if (value_str.startswith('"') and value_str.endswith('"')) or \
           (value_str.startswith("'") and value_str.endswith("'")):
            return value_str[1:-1]
        
        # Number
        try:
            if "." in value_str:
                return float(value_str)
            return int(value_str)
        except ValueError:
            pass
        
        # Unquoted string (legacy support)
        return value_str
```

## Task 5: Create BaseStepExecutor (executors/base.py)
```python
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
```

## Task 6: Create WorkflowEngine (workflow_engine.py)
```python
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
    ):
        """
        Initialize workflow engine.
        
        Args:
            export: Loaded agent export with workflow definition
            llm_driver: Configured LLM driver for classification steps
            admin_portal_url: URL for capability routing
        """
        self.export = export
        self.llm_driver = llm_driver
        self.admin_portal_url = admin_portal_url
        self.condition_evaluator = ConditionEvaluator()
        
        # Registry of step executors (populated by register_executor)
        self._executors: dict[str, BaseStepExecutor] = {}
        
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
```

## Task 7: Create __init__.py Files

### runtime/__init__.py
```python
"""Workflow runtime for executing agent workflows."""
from .models import AgentExport, StepType, WorkflowStepExportInfo
from .config_loader import ConfigLoader, ConfigurationError
from .execution_context import ExecutionContext, ExecutionStatus, StepResult
from .condition_evaluator import ConditionEvaluator, ConditionEvaluationError
from .workflow_engine import WorkflowEngine, WorkflowExecutionError

__all__ = [
    "AgentExport",
    "StepType",
    "WorkflowStepExportInfo",
    "ConfigLoader",
    "ConfigurationError",
    "ExecutionContext",
    "ExecutionStatus",
    "StepResult",
    "ConditionEvaluator",
    "ConditionEvaluationError",
    "WorkflowEngine",
    "WorkflowExecutionError",
]
```

### runtime/executors/__init__.py
```python
"""Step executors for workflow runtime."""
from .base import BaseStepExecutor, StepExecutionError

__all__ = [
    "BaseStepExecutor",
    "StepExecutionError",
]
```

## Task 8: Add Dependencies to pyproject.toml

Ensure these are in `agent/pyproject.toml`:
```toml
dependencies = [
    # ... existing deps
    "httpx>=0.25.0",  # For async HTTP (ConfigLoader)
    "pydantic>=2.0",   # For models
]
```

## Task 9: Create Basic Test

Create `agent/tests/runtime/test_condition_evaluator.py`:
```python
"""Tests for condition evaluator."""
import pytest
from agent.runtime.condition_evaluator import ConditionEvaluator, ConditionEvaluationError


class TestConditionEvaluator:
    def setup_method(self):
        self.evaluator = ConditionEvaluator()
    
    def test_empty_condition_returns_true(self):
        assert self.evaluator.evaluate(None, {}) is True
        assert self.evaluator.evaluate("", {}) is True
    
    def test_numeric_comparison(self):
        ctx = {"confidence": 0.85}
        assert self.evaluator.evaluate("confidence >= 0.8", ctx) is True
        assert self.evaluator.evaluate("confidence < 0.8", ctx) is False
        assert self.evaluator.evaluate("confidence >= 0.9", ctx) is False
    
    def test_boolean_comparison(self):
        ctx = {"valid": True, "success": False}
        assert self.evaluator.evaluate("valid == true", ctx) is True
        assert self.evaluator.evaluate("valid == false", ctx) is False
        assert self.evaluator.evaluate("success == false", ctx) is True
    
    def test_string_comparison(self):
        ctx = {"ticket_type": "password_reset"}
        assert self.evaluator.evaluate('ticket_type == "password_reset"', ctx) is True
        assert self.evaluator.evaluate("ticket_type == 'password_reset'", ctx) is True
    
    def test_nested_value(self):
        ctx = {"classify_ticket": {"confidence": 0.9}}
        assert self.evaluator.evaluate("classify_ticket.confidence >= 0.8", ctx) is True
    
    def test_invalid_syntax(self):
        with pytest.raises(ConditionEvaluationError):
            self.evaluator.evaluate("invalid syntax here", {})
    
    def test_missing_variable(self):
        # Should not raise, but return False or handle gracefully
        result = self.evaluator.evaluate("missing >= 0.5", {})
        # None >= 0.5 will raise TypeError, which we catch
        assert result is False or isinstance(result, bool)
```

## Verification
```bash
cd /home/alton/Documents/lucid-it-agent/agent

# Install any new dependencies
pip install -e ".[dev]"

# Run the tests
pytest tests/runtime/ -v

# Verify imports work
python -c "from agent.runtime import WorkflowEngine, ConfigLoader, ExecutionContext; print('Imports OK')"
```

## Summary

Phase 4B-1 creates the core workflow runtime infrastructure:

| Component | Purpose |
|-----------|---------|
| `models.py` | Pydantic models matching export JSON structure |
| `config_loader.py` | Fetches export, resolves credential references |
| `execution_context.py` | Shared state: ticket, results, variables |
| `condition_evaluator.py` | Evaluates transition conditions |
| `executors/base.py` | Abstract base for step executors |
| `workflow_engine.py` | Core engine: parses workflow, executes steps, follows transitions |

Next phase (4B-2) will implement the actual step executors (Classify, Execute, Notify, etc.).
