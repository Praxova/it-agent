"""Models for ticket classification."""

from enum import Enum

from pydantic import BaseModel, Field


class TicketType(str, Enum):
    """Types of tickets the agent can handle."""

    PASSWORD_RESET = "password_reset"
    GROUP_ACCESS_ADD = "group_access_add"
    GROUP_ACCESS_REMOVE = "group_access_remove"
    FILE_PERMISSION = "file_permission"
    UNKNOWN = "unknown"


class ClassificationResult(BaseModel):
    """Structured output from ticket classification.

    Attributes:
        ticket_type: Classified ticket type.
        confidence: Confidence score (0.0 to 1.0).
        reasoning: Brief explanation of the classification decision.
        affected_user: Username of the user needing help.
        target_group: Active Directory group name for access requests.
        target_resource: File or folder path for permission requests.
        should_escalate: True if human review is needed.
        escalation_reason: Explanation of why escalation is needed.
    """

    ticket_type: TicketType
    confidence: float = Field(
        ge=0.0, le=1.0, description="Confidence score from 0.0 to 1.0"
    )
    reasoning: str = Field(description="Brief explanation of classification")

    # Extracted entities (when applicable)
    affected_user: str | None = Field(
        default=None, description="Username of user needing help"
    )
    target_group: str | None = Field(
        default=None, description="AD group name for access requests"
    )
    target_resource: str | None = Field(
        default=None, description="File/folder path for permission requests"
    )

    # Escalation
    should_escalate: bool = Field(
        default=False, description="True if human review needed"
    )
    escalation_reason: str | None = Field(default=None)

    @property
    def action_recommended(self) -> str:
        """Returns recommended action based on confidence thresholds.

        Returns:
            "proceed": High confidence (>= 0.8), safe to proceed automatically.
            "proceed_with_review": Medium confidence (>= 0.6), proceed but flag for review.
            "escalate": Low confidence (< 0.6), escalate to human.
        """
        if self.confidence >= 0.8:
            return "proceed"
        elif self.confidence >= 0.6:
            return "proceed_with_review"
        else:
            return "escalate"
