"""Synthetic employee personas for realistic ticket generation.

Personas are built dynamically from the test_users.csv file — the same
file that drives the AD setup script. This ensures persona usernames
always match what exists in Active Directory.

Each persona has a distinct communication style, technical literacy level,
and typical issues they encounter.
"""

from __future__ import annotations

from .models import Persona
from .data import get_users, get_users_for_issue, TestUser


def _user_to_persona(user: TestUser) -> Persona:
    """Convert a TestUser from CSV into a Persona."""
    return Persona(
        username=user.username,
        display_name=user.display_name,
        department=user.department,
        role=user.role,
        tech_literacy=user.tech_literacy,
        communication_style=user.communication_style,
        common_issues=user.common_issues,
    )


def all_personas() -> list[Persona]:
    """Return all personas (one per test user from CSV)."""
    return [_user_to_persona(u) for u in get_users()]


def get_personas_for_issue(issue_type: str) -> list[Persona]:
    """Get all personas who commonly encounter this issue type."""
    return [_user_to_persona(u) for u in get_users_for_issue(issue_type)]


def get_persona(username: str) -> Persona | None:
    """Get a persona by username."""
    from .data import get_user
    user = get_user(username)
    return _user_to_persona(user) if user else None


# Backward compat — PERSONAS dict keyed by username
def _build_personas_dict() -> dict[str, Persona]:
    return {p.username: p for p in all_personas()}


PERSONAS: dict[str, Persona] = _build_personas_dict()
