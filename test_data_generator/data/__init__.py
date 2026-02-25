"""Loads test environment data from CSV files.

This module is the single source of truth for test usernames, groups,
and file shares. The same CSV files are consumed by:
  1. This Python loader (for ticket generation)
  2. The PowerShell AD setup script (for creating the actual AD objects)

This ensures the usernames in generated tickets always match what
exists in Active Directory.
"""

from __future__ import annotations

import csv
from dataclasses import dataclass, field
from pathlib import Path

_DATA_DIR = Path(__file__).parent


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass
class TestUser:
    username: str
    first_name: str
    last_name: str
    display_name: str
    department: str
    role: str
    email_prefix: str
    tech_literacy: str
    communication_style: str
    manager_username: str
    common_issues: list[str] = field(default_factory=list)


@dataclass
class TestGroup:
    group_name: str
    description: str
    category: str  # department, privileged, access, project
    initial_members: list[str] = field(default_factory=list)


@dataclass
class TestShare:
    share_path: str
    description: str
    department: str
    access_type: str  # read, write


# ---------------------------------------------------------------------------
# Deny-list accounts (these should NOT exist in test_users.csv —
# they're used to verify the agent correctly refuses to act)
# ---------------------------------------------------------------------------

DENY_LIST_USERNAMES = [
    "administrator",
    "admin",
    "svc_backup",
    "svc_sql",
    "sa_deploy",
    "domain_admin",
    "svc-lucid-agent",
]


# ---------------------------------------------------------------------------
# Loaders
# ---------------------------------------------------------------------------

def _load_csv(filename: str) -> list[dict[str, str]]:
    """Load a CSV from the data directory."""
    filepath = _DATA_DIR / filename
    if not filepath.exists():
        raise FileNotFoundError(f"Test data file not found: {filepath}")
    with open(filepath, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def load_users() -> list[TestUser]:
    """Load test users from CSV."""
    rows = _load_csv("test_users.csv")
    users = []
    for row in rows:
        users.append(TestUser(
            username=row["username"],
            first_name=row["first_name"],
            last_name=row["last_name"],
            display_name=row["display_name"],
            department=row["department"],
            role=row["role"],
            email_prefix=row["email_prefix"],
            tech_literacy=row["tech_literacy"],
            communication_style=row["communication_style"],
            manager_username=row.get("manager_username", ""),
            common_issues=row.get("common_issues", "").split("|") if row.get("common_issues") else [],
        ))
    return users


def load_groups() -> list[TestGroup]:
    """Load test groups from CSV."""
    rows = _load_csv("test_groups.csv")
    groups = []
    for row in rows:
        groups.append(TestGroup(
            group_name=row["group_name"],
            description=row["description"],
            category=row["category"],
            initial_members=row.get("initial_members", "").split("|") if row.get("initial_members") else [],
        ))
    return groups


def load_shares() -> list[TestShare]:
    """Load test file shares from CSV."""
    rows = _load_csv("test_shares.csv")
    shares = []
    for row in rows:
        shares.append(TestShare(
            share_path=row["share_path"],
            description=row["description"],
            department=row["department"],
            access_type=row["access_type"],
        ))
    return shares


# ---------------------------------------------------------------------------
# Convenience accessors
# ---------------------------------------------------------------------------

_users_cache: list[TestUser] | None = None
_groups_cache: list[TestGroup] | None = None
_shares_cache: list[TestShare] | None = None


def get_users() -> list[TestUser]:
    """Get all test users (cached)."""
    global _users_cache
    if _users_cache is None:
        _users_cache = load_users()
    return _users_cache


def get_groups() -> list[TestGroup]:
    """Get all test groups (cached)."""
    global _groups_cache
    if _groups_cache is None:
        _groups_cache = load_groups()
    return _groups_cache


def get_shares() -> list[TestShare]:
    """Get all test shares (cached)."""
    global _shares_cache
    if _shares_cache is None:
        _shares_cache = load_shares()
    return _shares_cache


def get_usernames() -> list[str]:
    """Get all valid test usernames."""
    return [u.username for u in get_users()]


def get_user_display_names() -> list[str]:
    """Get all display names."""
    return [u.display_name for u in get_users()]


def get_username_display_pairs() -> list[tuple[str, str]]:
    """Get (username, display_name) pairs."""
    return [(u.username, u.display_name) for u in get_users()]


def get_group_names() -> list[str]:
    """Get all test group names."""
    return [g.group_name for g in get_groups()]


def get_department_groups() -> list[str]:
    """Get group names in the 'department' category."""
    return [g.group_name for g in get_groups() if g.category == "department"]


def get_share_paths(access_type: str | None = None) -> list[str]:
    """Get file share paths, optionally filtered by access type."""
    shares = get_shares()
    if access_type:
        shares = [s for s in shares if s.access_type == access_type]
    return [s.share_path for s in shares]


def get_users_by_department(department: str) -> list[TestUser]:
    """Get users in a specific department."""
    return [u for u in get_users() if u.department.lower() == department.lower()]


def get_users_for_issue(issue_type: str) -> list[TestUser]:
    """Get users who commonly experience this issue type."""
    return [u for u in get_users() if issue_type in u.common_issues]


def get_user(username: str) -> TestUser | None:
    """Look up a single user by username."""
    for u in get_users():
        if u.username == username:
            return u
    return None


def departments() -> list[str]:
    """Get all unique departments."""
    return sorted(set(u.department for u in get_users()))
