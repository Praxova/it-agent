"""Sample tickets and expected classifications for testing."""

from datetime import datetime

from connectors import Ticket, TicketState

from agent.classifier.models import ClassificationResult, TicketType


# Sample tickets with expected classifications
SAMPLE_TICKETS = [
    {
        "ticket": Ticket(
            id="test001",
            number="INC0010001",
            short_description="Password reset needed",
            description="I forgot my password and can't log into my computer. My username is jsmith. Please help!",
            state=TicketState.NEW,
            priority=3,
            caller_username="jsmith",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 14, 30, 0),
            updated_at=datetime(2024, 1, 15, 14, 30, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.90,  # Minimum expected
            reasoning="Clear password reset request",
            affected_user="jsmith",
            should_escalate=False,
        ),
    },
    {
        "ticket": Ticket(
            id="test002",
            number="INC0010002",
            short_description="Need Finance group access",
            description="Hi, I just joined the Finance team. Can you please add me to the Finance-Users group? Thanks! Username: sconnor",
            state=TicketState.NEW,
            priority=4,
            caller_username="sconnor",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 15, 0, 0),
            updated_at=datetime(2024, 1, 15, 15, 0, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.GROUP_ACCESS_ADD,
            confidence=0.85,
            reasoning="Group access add request",
            affected_user="sconnor",
            target_group="Finance-Users",
            should_escalate=False,
        ),
    },
    {
        "ticket": Ticket(
            id="test003",
            number="INC0010003",
            short_description="Remove user from group",
            description="Please remove mjones from the IT-Admins group. He moved to a different department.",
            state=TicketState.NEW,
            priority=3,
            caller_username="manager1",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 15, 30, 0),
            updated_at=datetime(2024, 1, 15, 15, 30, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.GROUP_ACCESS_REMOVE,
            confidence=0.85,
            reasoning="Group access removal request",
            affected_user="mjones",
            target_group="IT-Admins",
            should_escalate=False,
        ),
    },
    {
        "ticket": Ticket(
            id="test004",
            number="INC0010004",
            short_description="Need folder access",
            description="I need read access to \\\\fileserver\\projects\\Q4Planning folder for the upcoming project. Username: bwilson",
            state=TicketState.NEW,
            priority=4,
            caller_username="bwilson",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 16, 0, 0),
            updated_at=datetime(2024, 1, 15, 16, 0, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.FILE_PERMISSION,
            confidence=0.80,
            reasoning="File permission request",
            affected_user="bwilson",
            target_resource="\\\\fileserver\\projects\\Q4Planning",
            should_escalate=False,
        ),
    },
    {
        "ticket": Ticket(
            id="test005",
            number="INC0010005",
            short_description="Computer won't turn on",
            description="My laptop won't power on at all. I tried plugging it in but nothing happens. Help!",
            state=TicketState.NEW,
            priority=2,
            caller_username="auser",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 16, 30, 0),
            updated_at=datetime(2024, 1, 15, 16, 30, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.UNKNOWN,
            confidence=0.80,
            reasoning="Hardware issue",
            affected_user="auser",
            should_escalate=True,
            escalation_reason="Hardware issue",
        ),
    },
    {
        "ticket": Ticket(
            id="test006",
            number="INC0010006",
            short_description="Help needed",
            description="I need help with something",
            state=TicketState.NEW,
            priority=5,
            caller_username="vague_user",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 17, 0, 0),
            updated_at=datetime(2024, 1, 15, 17, 0, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.UNKNOWN,
            confidence=0.40,  # Low confidence
            reasoning="Vague request",
            affected_user="vague_user",
            should_escalate=True,
            escalation_reason="Insufficient information",
        ),
    },
    {
        "ticket": Ticket(
            id="test007",
            number="INC0010007",
            short_description="Account locked",
            description="My account got locked after too many failed login attempts. Can you unlock it? Username: dsmith",
            state=TicketState.NEW,
            priority=2,
            caller_username="dsmith",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 17, 30, 0),
            updated_at=datetime(2024, 1, 15, 17, 30, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.85,
            reasoning="Account lockout requires password reset",
            affected_user="dsmith",
            should_escalate=False,
        ),
    },
    {
        "ticket": Ticket(
            id="test008",
            number="INC0010008",
            short_description="Need new hire added to groups",
            description="New hire John Doe (jdoe) needs to be added to: Engineering-All, Engineering-Dev, and VPN-Access groups. Start date is Monday.",
            state=TicketState.NEW,
            priority=3,
            caller_username="hr_manager",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 18, 0, 0),
            updated_at=datetime(2024, 1, 15, 18, 0, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.GROUP_ACCESS_ADD,
            confidence=0.75,  # Multiple groups might reduce confidence slightly
            reasoning="Multiple group access adds for new hire",
            affected_user="jdoe",
            should_escalate=False,  # Or True - could go either way
        ),
    },
    {
        "ticket": Ticket(
            id="test009",
            number="INC0010009",
            short_description="Cant acces shared drive",  # Typo intentional
            description="i cant get to the sahred drive where we keep the reports. its at \\\\fs01\\reports i think",  # Typos and poor grammar
            state=TicketState.NEW,
            priority=4,
            caller_username="casual_user",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 18, 30, 0),
            updated_at=datetime(2024, 1, 15, 18, 30, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.FILE_PERMISSION,
            confidence=0.70,  # Lower due to typos/uncertainty
            reasoning="File permission request despite typos",
            affected_user="casual_user",
            target_resource="\\\\fs01\\reports",
            should_escalate=False,
        ),
    },
    {
        "ticket": Ticket(
            id="test010",
            number="INC0010010",
            short_description="Urgent - CEO needs access",
            description="CEO needs immediate access to the Executive-Confidential group and the \\\\fileserver\\executive\\board_docs folder. This is urgent!",
            state=TicketState.NEW,
            priority=1,
            caller_username="executive_assistant",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 19, 0, 0),
            updated_at=datetime(2024, 1, 15, 19, 0, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.UNKNOWN,  # Mixed request - should escalate
            confidence=0.60,
            reasoning="Multiple request types, high-privilege user",
            should_escalate=True,
            escalation_reason="High-privilege access request requires approval",
        ),
    },
    {
        "ticket": Ticket(
            id="test011",
            number="INC0010011",
            short_description="Password expiring soon",
            description="I got a notification that my password is expiring in 3 days. How do I change it? Username: echen",
            state=TicketState.NEW,
            priority=4,
            caller_username="echen",
            assignment_group="Helpdesk",
            created_at=datetime(2024, 1, 15, 19, 30, 0),
            updated_at=datetime(2024, 1, 15, 19, 30, 0),
        ),
        "expected": ClassificationResult(
            ticket_type=TicketType.PASSWORD_RESET,
            confidence=0.85,
            reasoning="Password change request",
            affected_user="echen",
            should_escalate=False,
        ),
    },
]


def get_sample_tickets() -> list[dict]:
    """Get all sample tickets with expected classifications.

    Returns:
        List of dictionaries with 'ticket' and 'expected' keys.
    """
    return SAMPLE_TICKETS


def get_tickets_by_type(ticket_type: TicketType) -> list[dict]:
    """Get sample tickets filtered by expected type.

    Args:
        ticket_type: Filter by this ticket type.

    Returns:
        List of dictionaries with 'ticket' and 'expected' keys.
    """
    return [
        sample
        for sample in SAMPLE_TICKETS
        if sample["expected"].ticket_type == ticket_type
    ]


def get_high_confidence_tickets() -> list[dict]:
    """Get tickets expected to have high confidence (>= 0.8).

    Returns:
        List of dictionaries with 'ticket' and 'expected' keys.
    """
    return [
        sample for sample in SAMPLE_TICKETS if sample["expected"].confidence >= 0.8
    ]


def get_escalation_tickets() -> list[dict]:
    """Get tickets expected to require escalation.

    Returns:
        List of dictionaries with 'ticket' and 'expected' keys.
    """
    return [
        sample for sample in SAMPLE_TICKETS if sample["expected"].should_escalate
    ]
