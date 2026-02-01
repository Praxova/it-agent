"""Prompt templates and few-shot examples for ticket classification."""

import json
from typing import Any

from .models import ClassificationResult


SYSTEM_PROMPT = """You are an IT helpdesk ticket classifier for an automated IT agent.
Your job is to analyze incoming tickets and classify them so the agent knows how to respond.

## Ticket Types
- password_reset: User forgot password, account locked, needs password reset or change
- group_access_add: User needs to be ADDED to an Active Directory group
- group_access_remove: User needs to be REMOVED from an Active Directory group
- file_permission: User needs access to files, folders, or network shares
- unknown: Does not fit the above categories, requires human review

## Instructions
1. Read the ticket carefully
2. Identify the ticket type
3. Extract relevant entities (usernames, group names, file paths)
4. Assign a confidence score (0.0 to 1.0)
5. If confidence < 0.6 OR the request is ambiguous/complex, set should_escalate=true

## Output Format
Respond with valid JSON using this exact structure:
```json
{
  "ticket_type": "password_reset",
  "confidence": 0.95,
  "reasoning": "Brief explanation",
  "affected_user": "username",
  "target_group": null,
  "target_resource": null,
  "should_escalate": false,
  "escalation_reason": null
}
```

Field descriptions:
- ticket_type: One of: password_reset, group_access_add, group_access_remove, file_permission, unknown
- confidence: Number between 0.0 and 1.0
- reasoning: Brief explanation of your classification
- affected_user: Username of the person needing help (or null)
- target_group: AD group name (or null)
- target_resource: File/folder path (or null)
- should_escalate: true if human review needed, false otherwise
- escalation_reason: Explanation of why escalation is needed (or null)

## Important
- Extract usernames exactly as written (e.g., "jsmith", "john.smith")
- Group names are case-sensitive, preserve exact casing
- For file permissions, extract the full path if provided
- If caller is the affected user, use caller's username for affected_user
- When in doubt, escalate to human (should_escalate=true)
"""


FEW_SHOT_EXAMPLES = [
    {
        "ticket": {
            "number": "INC0010001",
            "short_description": "Password reset needed",
            "description": "I forgot my password and can't log into my computer. My username is jsmith. Please help!",
            "caller_username": "jsmith",
        },
        "classification": {
            "ticket_type": "password_reset",
            "confidence": 0.95,
            "reasoning": "User explicitly states they forgot their password and cannot log in. Clear password reset request.",
            "affected_user": "jsmith",
            "target_group": None,
            "target_resource": None,
            "should_escalate": False,
            "escalation_reason": None,
        },
    },
    {
        "ticket": {
            "number": "INC0010002",
            "short_description": "Need access to Finance reports",
            "description": "Hi, I just transferred to the Finance department. My manager said I need to be added to the Finance-Reports group to access the monthly reports. Thanks! - Sarah Connor (sconnor)",
            "caller_username": "sconnor",
        },
        "classification": {
            "ticket_type": "group_access_add",
            "confidence": 0.92,
            "reasoning": "User explicitly requests to be added to a specific AD group (Finance-Reports) for access.",
            "affected_user": "sconnor",
            "target_group": "Finance-Reports",
            "target_resource": None,
            "should_escalate": False,
            "escalation_reason": None,
        },
    },
    {
        "ticket": {
            "number": "INC0010003",
            "short_description": "Remove access for terminated employee",
            "description": "Please remove John Smith (jsmith) from the IT-Admins group. He transferred to Marketing last week.",
            "caller_username": "manager1",
        },
        "classification": {
            "ticket_type": "group_access_remove",
            "confidence": 0.90,
            "reasoning": "Request to remove a user from a specific AD group due to role change.",
            "affected_user": "jsmith",
            "target_group": "IT-Admins",
            "target_resource": None,
            "should_escalate": False,
            "escalation_reason": None,
        },
    },
    {
        "ticket": {
            "number": "INC0010004",
            "short_description": "Need access to project folder",
            "description": "I need read access to \\\\fileserver\\projects\\ProjectX folder. I'm working with the ProjectX team on the Q2 deliverables. Username: bwilson",
            "caller_username": "bwilson",
        },
        "classification": {
            "ticket_type": "file_permission",
            "confidence": 0.88,
            "reasoning": "User requesting file/folder access with specific path and access level (read).",
            "affected_user": "bwilson",
            "target_group": None,
            "target_resource": "\\\\fileserver\\projects\\ProjectX",
            "should_escalate": False,
            "escalation_reason": None,
        },
    },
    {
        "ticket": {
            "number": "INC0010005",
            "short_description": "Computer making weird noise",
            "description": "My laptop has been making a grinding noise for the past few days. It's getting louder. Should I be worried?",
            "caller_username": "auser",
        },
        "classification": {
            "ticket_type": "unknown",
            "confidence": 0.85,
            "reasoning": "Hardware issue (possible failing hard drive or fan). Outside scope of automated IT agent capabilities.",
            "affected_user": "auser",
            "target_group": None,
            "target_resource": None,
            "should_escalate": True,
            "escalation_reason": "Hardware issue requires physical inspection by IT technician",
        },
    },
    {
        "ticket": {
            "number": "INC0010006",
            "short_description": "Access issue",
            "description": "Can't access the thing I need for work",
            "caller_username": "vague_user",
        },
        "classification": {
            "ticket_type": "unknown",
            "confidence": 0.35,
            "reasoning": "Request is too vague. No specific resource, group, or issue type identified.",
            "affected_user": "vague_user",
            "target_group": None,
            "target_resource": None,
            "should_escalate": True,
            "escalation_reason": "Insufficient information to determine request type. Human follow-up needed to clarify.",
        },
    },
    {
        "ticket": {
            "number": "INC0010007",
            "short_description": "Account locked out",
            "description": "My account is locked after trying to log in too many times. Can you unlock it? Username: mjones",
            "caller_username": "mjones",
        },
        "classification": {
            "ticket_type": "password_reset",
            "confidence": 0.90,
            "reasoning": "Account lockout requiring password reset. Standard password reset procedure applies.",
            "affected_user": "mjones",
            "target_group": None,
            "target_resource": None,
            "should_escalate": False,
            "escalation_reason": None,
        },
    },
    {
        "ticket": {
            "number": "INC0010008",
            "short_description": "Need to be removed from old team group",
            "description": "I've moved to a new team and no longer need access to the Engineering-Dev group. Please remove me (username: kjohnson). Thanks!",
            "caller_username": "kjohnson",
        },
        "classification": {
            "ticket_type": "group_access_remove",
            "confidence": 0.88,
            "reasoning": "User requesting removal from AD group after team change. Clear group removal request.",
            "affected_user": "kjohnson",
            "target_group": "Engineering-Dev",
            "target_resource": None,
            "should_escalate": False,
            "escalation_reason": None,
        },
    },
]


def get_classification_schema() -> dict[str, Any]:
    """Get the JSON schema for ClassificationResult.

    Returns:
        JSON schema dictionary for ClassificationResult model.
    """
    return ClassificationResult.model_json_schema()


def build_classification_prompt(ticket: dict[str, Any]) -> str:
    """Build the full prompt with system instructions, examples, and the ticket to classify.

    Args:
        ticket: Ticket dictionary to classify (from Ticket.model_dump()).

    Returns:
        Complete prompt string for LLM.
    """
    # Build system prompt (no schema needed anymore)
    system_text = SYSTEM_PROMPT

    # Build few-shot examples
    examples_text = "## Examples\n\n"
    for i, example in enumerate(FEW_SHOT_EXAMPLES, 1):
        examples_text += f"### Example {i}\n"
        examples_text += "**Ticket:**\n"
        examples_text += f"```json\n{json.dumps(example['ticket'], indent=2)}\n```\n\n"
        examples_text += "**Classification:**\n"
        examples_text += f"```json\n{json.dumps(example['classification'], indent=2)}\n```\n\n"

    # Build current ticket section
    # Extract only relevant fields for classification
    ticket_summary = {
        "number": ticket.get("number"),
        "short_description": ticket.get("short_description"),
        "description": ticket.get("description"),
        "caller_username": ticket.get("caller_username"),
        "priority": ticket.get("priority"),
    }

    ticket_text = "## Ticket to Classify\n\n"
    ticket_text += "**Ticket:**\n"
    ticket_text += f"```json\n{json.dumps(ticket_summary, indent=2)}\n```\n\n"
    ticket_text += "**Your Classification:**\n"
    ticket_text += "Provide your classification as valid JSON matching the schema above.\n"

    # Combine all sections
    full_prompt = f"{system_text}\n\n{examples_text}\n{ticket_text}"

    return full_prompt
