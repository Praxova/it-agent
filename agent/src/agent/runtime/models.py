"""Pydantic models for agent export data."""
from __future__ import annotations
from datetime import datetime
from enum import Enum
from typing import Any
from pydantic import BaseModel, ConfigDict, Field, field_validator


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
    SUB_WORKFLOW = "SubWorkflow"  # Phase C2 will add executor
    APPROVAL = "Approval"
    CLARIFY = "Clarify"


class CredentialReference(BaseModel):
    """Reference to credentials (not actual secrets)."""
    storage: str  # "environment", "vault", etc.
    username_key: str | None = None
    password_key: str | None = None
    api_key_key: str | None = None


class RuleExportInfo(BaseModel):
    """Individual rule within a ruleset."""
    model_config = ConfigDict(populate_by_name=True)

    name: str
    rule_text: str = Field(alias="ruleText")
    priority: int
    is_enabled: bool = Field(True, alias="isEnabled")


class RulesetExportInfo(BaseModel):
    """Ruleset with its rules."""
    model_config = ConfigDict(populate_by_name=True)

    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    scope: str | None = None
    rules: list[RuleExportInfo] = []


class StepRulesetMapping(BaseModel):
    """Ruleset attached to a step."""
    model_config = ConfigDict(populate_by_name=True)

    ruleset_name: str = Field(alias="rulesetName")
    priority: int
    is_enabled: bool = Field(alias="isEnabled")


class WorkflowStepExportInfo(BaseModel):
    """Workflow step definition."""
    model_config = ConfigDict(populate_by_name=True)

    name: str
    display_name: str | None = Field(None, alias="displayName")
    step_type: StepType = Field(alias="stepType")
    configuration: dict[str, Any] = {}
    sort_order: int = Field(alias="sortOrder")
    ruleset_mappings: list[StepRulesetMapping] = Field(default_factory=list, alias="rulesetMappings")


class WorkflowTransitionExportInfo(BaseModel):
    """Transition between steps."""
    model_config = ConfigDict(populate_by_name=True)

    from_step_name: str = Field(alias="fromStepName")
    to_step_name: str = Field(alias="toStepName")
    condition: str | None = None
    label: str | None = None
    output_index: int = Field(0, alias="outputIndex")
    input_index: int = Field(0, alias="inputIndex")


class WorkflowExportInfo(BaseModel):
    """Complete workflow definition."""
    model_config = ConfigDict(populate_by_name=True)

    id: str | None = None  # Workflow GUID from portal
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    version: str
    trigger_type: str | None = Field(None, alias="triggerType")
    example_set_name: str | None = Field(None, alias="exampleSetName")
    steps: list[WorkflowStepExportInfo] = []
    transitions: list[WorkflowTransitionExportInfo] = []
    ruleset_mappings: list[StepRulesetMapping] = Field(default_factory=list, alias="rulesetMappings")
    required_capabilities: list[str] = Field(default_factory=list, alias="requiredCapabilities")


class ExampleExportInfo(BaseModel):
    """Classification example."""
    model_config = ConfigDict(populate_by_name=True)

    id: str | None = None
    input_text: str = Field(alias="inputText")
    expected_output_json: str | None = Field(None, alias="expectedOutputJson")
    notes: str | None = None


class ExampleSetExportInfo(BaseModel):
    """Set of classification examples."""
    model_config = ConfigDict(populate_by_name=True)

    id: str | None = None
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    examples: list[ExampleExportInfo] = []


class ProviderExportInfo(BaseModel):
    """LLM or ServiceNow provider configuration."""
    model_config = ConfigDict(populate_by_name=True)

    provider_type: str = Field(alias="providerType")
    config: dict[str, Any] = {}
    credentials: CredentialReference | None = None


class ServiceNowExportInfo(ProviderExportInfo):
    """ServiceNow provider with assignment group."""
    assignment_group: str | None = Field(None, alias="assignmentGroup")


class ServiceAccountBindingInfo(BaseModel):
    """Dynamic service account binding for an agent."""
    model_config = ConfigDict(populate_by_name=True)

    role: str
    qualifier: str | None = None
    service_account_name: str = Field(alias="serviceAccountName")
    provider_type: str = Field(alias="providerType")


class AgentBasicInfo(BaseModel):
    """Basic agent information."""
    model_config = ConfigDict(populate_by_name=True)

    id: str
    name: str
    display_name: str | None = Field(None, alias="displayName")
    description: str | None = None
    is_enabled: bool = Field(alias="isEnabled")
    service_account_bindings: list[ServiceAccountBindingInfo] = Field(
        default_factory=list, alias="serviceAccountBindings"
    )

    @field_validator("service_account_bindings", mode="before")
    @classmethod
    def _coerce_null_to_list(cls, v):
        """Handle C# serializer sending null instead of [] for empty collections."""
        return v if v is not None else []


class AgentExport(BaseModel):
    """Complete agent export - root model."""
    model_config = ConfigDict(populate_by_name=True)

    version: str
    exported_at: datetime = Field(alias="exportedAt")
    agent: AgentBasicInfo
    llm_provider: ProviderExportInfo | None = Field(None, alias="llmProvider")
    service_now: ServiceNowExportInfo | None = Field(None, alias="serviceNow")
    workflow: WorkflowExportInfo | None = None
    rulesets: dict[str, RulesetExportInfo] = {}
    example_sets: dict[str, ExampleSetExportInfo] = Field(default_factory=dict, alias="exampleSets")
    required_capabilities: list[str] = Field(default_factory=list, alias="requiredCapabilities")

    # Sub-workflow definitions referenced by SubWorkflow steps
    # Key: workflow name, Value: workflow definition
    sub_workflows: dict[str, WorkflowExportInfo] = Field(
        default_factory=dict, alias="subWorkflows"
    )
