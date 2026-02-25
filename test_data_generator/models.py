"""Core data models for the ticket generation framework.

These models serve dual purposes:
1. Immediate: structured test data for agent E2E testing and load testing
2. Future: annotated interaction traces for fine-tuning (QLoRA on Llama 3.1)
"""

from __future__ import annotations

import uuid
from datetime import datetime
from enum import Enum
from typing import Any

from pydantic import BaseModel, Field


# ---------------------------------------------------------------------------
# Enums aligned with the agent's classifier/models.py
# ---------------------------------------------------------------------------

class TicketType(str, Enum):
    """Mirror of agent.classifier.models.TicketType."""
    PASSWORD_RESET = "password_reset"
    GROUP_ACCESS_ADD = "group_access_add"
    GROUP_ACCESS_REMOVE = "group_access_remove"
    FILE_PERMISSION = "file_permission"
    SOFTWARE_INSTALL = "software_install"
    UNKNOWN = "unknown"


class ComplexityTier(int, Enum):
    """How hard the ticket is for the agent to handle."""
    TIER_1 = 1  # Single-tool, unambiguous
    TIER_2 = 2  # Single-tool, requires interpretation
    TIER_3 = 3  # Multi-step workflows
    TIER_4 = 4  # Edge cases and failure modes
    TIER_5 = 5  # Ambiguous escalation boundaries


class ExpectedOutcome(str, Enum):
    """What should happen when the agent processes this ticket."""
    RESOLVE = "resolve"             # Agent handles end-to-end
    ESCALATE_LOW_CONF = "escalate_low_confidence"
    ESCALATE_VALIDATION = "escalate_validation_failure"
    ESCALATE_SCOPE = "escalate_out_of_scope"
    CLARIFY = "clarify"             # Agent should ask for more info
    PARTIAL = "partial"             # Agent handles some, escalates rest


# ---------------------------------------------------------------------------
# Persona model (synthetic employees)
# ---------------------------------------------------------------------------

class Persona(BaseModel):
    """A synthetic employee who submits tickets."""
    username: str
    display_name: str
    department: str
    role: str
    tech_literacy: str = Field(description="low | medium | high")
    communication_style: str = Field(
        description="terse | normal | verbose | rambling | formal | angry"
    )
    common_issues: list[str] = Field(default_factory=list)


# ---------------------------------------------------------------------------
# Scenario definition (the "recipe" for generating tickets)
# ---------------------------------------------------------------------------

class ScenarioVariation(BaseModel):
    """A single variation within a scenario — controls how the ticket text
    is generated while keeping the same underlying intent."""
    label: str
    description_template: str = Field(
        description="Template string with {placeholders} for slot-filling"
    )
    short_description_template: str


class ToolCall(BaseModel):
    """Expected tool invocation the agent should make."""
    tool_name: str
    method: str
    parameters: dict[str, Any] = Field(default_factory=dict)


class ExpectedClassification(BaseModel):
    """What the classifier should output for this scenario."""
    ticket_type: TicketType
    min_confidence: float = 0.0
    max_confidence: float = 1.0
    affected_user: str | None = None
    target_group: str | None = None
    target_resource: str | None = None
    should_escalate: bool = False


class Scenario(BaseModel):
    """A complete scenario definition — the blueprint for generating tickets.

    Each scenario can have multiple variations (different phrasings of the
    same underlying request) and carries metadata about expected agent
    behavior for automated scoring.
    """
    id: str = Field(description="Unique scenario identifier, e.g. 'pwd_reset_happy'")
    name: str
    ticket_type: TicketType
    complexity_tier: ComplexityTier
    expected_outcome: ExpectedOutcome

    # Generation
    variations: list[ScenarioVariation] = Field(default_factory=list)
    slot_values: dict[str, list[str]] = Field(
        default_factory=dict,
        description="Named slot -> list of possible values for template expansion"
    )
    servicenow_fields: dict[str, str] = Field(
        default_factory=dict,
        description="Static ServiceNow fields: category, subcategory, impact, urgency"
    )

    # Expected agent behavior (for scoring)
    expected_classification: ExpectedClassification
    expected_tool_calls: list[ToolCall] = Field(default_factory=list)
    expected_workflow_path: str = ""
    validation_should_pass: bool = True
    deny_list_trigger: bool = False

    # Tags for filtering and reporting
    tags: list[str] = Field(default_factory=list)


# ---------------------------------------------------------------------------
# Generated ticket (the output)
# ---------------------------------------------------------------------------

class GeneratedTicket(BaseModel):
    """A fully-realized ticket instance ready for testing or training."""
    id: str = Field(default_factory=lambda: str(uuid.uuid4())[:8])
    generated_at: datetime = Field(default_factory=datetime.utcnow)

    # Source traceability
    scenario_id: str
    variation_label: str
    persona_username: str | None = None

    # Ticket content (what gets sent to ServiceNow or the agent)
    short_description: str
    description: str
    caller_username: str = "abel.tuter"
    category: str = "Software"
    subcategory: str = ""
    impact: str = "3"
    urgency: str = "3"

    # Expected results (for automated scoring)
    expected_classification: ExpectedClassification
    expected_tool_calls: list[ToolCall] = Field(default_factory=list)
    expected_outcome: ExpectedOutcome
    expected_workflow_path: str = ""
    complexity_tier: ComplexityTier
    tags: list[str] = Field(default_factory=list)

    # ServiceNow tracking (populated after creation)
    snow_sys_id: str | None = None
    snow_number: str | None = None


# ---------------------------------------------------------------------------
# Training annotation (future use — wraps a ticket with interaction trace)
# ---------------------------------------------------------------------------

class InteractionStep(BaseModel):
    """A single step in an agent interaction trace."""
    role: str = Field(description="system | user | assistant | tool_call | tool_result")
    content: str
    metadata: dict[str, Any] = Field(default_factory=dict)


class AnnotatedInteraction(BaseModel):
    """A complete interaction trace suitable for fine-tuning.

    Generated by running the agent against a GeneratedTicket, capturing
    the trace, and then marking it as correct or correcting it.
    """
    ticket: GeneratedTicket
    interaction: list[InteractionStep] = Field(default_factory=list)
    is_correct: bool = False
    corrected_by: str | None = None
    corrected_at: datetime | None = None
    notes: str = ""
