"""ServiceNow-specific models for API responses."""

from datetime import datetime

from pydantic import BaseModel, Field, field_validator

from ..base import Ticket, TicketState


# ServiceNow state display value to integer mapping
STATE_MAP = {
    "New": 1,
    "In Progress": 2,
    "On Hold": 3,
    "Resolved": 6,
    "Closed": 7,
    "Canceled": 8,
    # Also handle integer strings
    "1": 1,
    "2": 2,
    "3": 3,
    "6": 6,
    "7": 7,
    "8": 8,
}


def parse_state(value: str | int) -> int:
    """Convert ServiceNow state to integer.

    Handles both display values ("New", "In Progress") and integer strings ("1", "2").

    Args:
        value: State value from ServiceNow API.

    Returns:
        Integer state value.

    Raises:
        ValueError: If state value is not recognized.
    """
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        # Try mapping from display value
        if value in STATE_MAP:
            return STATE_MAP[value]
        # Try parsing as integer directly
        try:
            return int(value)
        except ValueError:
            pass
    raise ValueError(f"Unknown state value: {value}")


class IncidentResponse(BaseModel):
    """ServiceNow incident API response model.

    Maps ServiceNow's incident JSON structure to a typed Pydantic model.
    Field names match ServiceNow's API field names.

    Attributes:
        sys_id: Unique system identifier.
        number: Human-readable incident number (e.g., INC0010001).
        short_description: Brief summary of the incident.
        description: Detailed description of the incident.
        state: Incident state (1-7).
        priority: Priority level (1-5).
        caller_id: Reference to caller's sys_user record.
        assignment_group: Reference to assignment group record.
        sys_created_on: Creation timestamp (ServiceNow format).
        sys_updated_on: Last update timestamp (ServiceNow format).
    """

    sys_id: str
    number: str
    short_description: str
    description: str | None = None
    state: str  # ServiceNow returns as string
    priority: str  # ServiceNow returns as string
    caller_id: dict[str, str] | str = Field(
        default_factory=dict
    )  # Can be dict with display_value or string
    assignment_group: dict[str, str] | str = Field(
        default_factory=dict
    )  # Can be dict with display_value or string
    sys_created_on: str
    sys_updated_on: str

    @field_validator("state", mode="before")
    @classmethod
    def validate_state(cls, v: str | int) -> str:
        """Convert state to string if needed."""
        return str(v)

    @field_validator("priority", mode="before")
    @classmethod
    def validate_priority(cls, v: str | int) -> str:
        """Convert priority to string if needed."""
        return str(v)

    def to_ticket(self, caller_username: str | None = None) -> Ticket:
        """Convert ServiceNow incident to internal Ticket model.

        Args:
            caller_username: Optional caller username. If not provided,
                            will attempt to extract from caller_id field.

        Returns:
            Internal Ticket representation.
        """
        # Extract caller username
        if caller_username is None:
            if isinstance(self.caller_id, dict):
                caller_username = self.caller_id.get("display_value", "unknown")
            else:
                caller_username = str(self.caller_id) if self.caller_id else "unknown"

        # Extract assignment group name
        if isinstance(self.assignment_group, dict):
            assignment_group_name = self.assignment_group.get("display_value", "unknown")
        else:
            assignment_group_name = (
                str(self.assignment_group) if self.assignment_group else "unknown"
            )

        # Parse ServiceNow timestamps (format: "2024-01-15 14:30:00")
        created_at = self._parse_servicenow_datetime(self.sys_created_on)
        updated_at = self._parse_servicenow_datetime(self.sys_updated_on)

        return Ticket(
            id=self.sys_id,
            number=self.number,
            short_description=self.short_description,
            description=self.description,
            state=TicketState(parse_state(self.state)),
            priority=int(self.priority) if self.priority.isdigit() else 3,  # Default to medium priority
            caller_username=caller_username,
            assignment_group=assignment_group_name,
            created_at=created_at,
            updated_at=updated_at,
        )

    @staticmethod
    def _parse_servicenow_datetime(date_str: str) -> datetime:
        """Parse ServiceNow datetime format.

        ServiceNow returns timestamps in format: "2024-01-15 14:30:00"

        Args:
            date_str: ServiceNow datetime string.

        Returns:
            Parsed datetime object.
        """
        return datetime.strptime(date_str, "%Y-%m-%d %H:%M:%S")
