#!/usr/bin/env python3
"""
ServiceNow Test Ticket Generator for Lucid IT Agent E2E Testing.

Creates test incidents in a ServiceNow PDI instance with various
ticket types to exercise different workflow paths.

Automatically loads .env file from the project root if python-dotenv
is installed. Also checks both SERVICENOW_* and LUCID_SERVICENOW_*
env var prefixes.

Usage:
    # If python-dotenv is installed, just run from project root:
    python agent/scripts/create_test_tickets.py --all

    # Or set credentials manually:
    export SERVICENOW_INSTANCE=https://dev341394.service-now.com
    export SERVICENOW_USERNAME=admin
    export SERVICENOW_PASSWORD=your_password
    python agent/scripts/create_test_tickets.py --all

    # Or pass directly:
    python agent/scripts/create_test_tickets.py --all --password 'MyP@ss!'

    # List current tickets in Help Desk queue
    python agent/scripts/create_test_tickets.py --list

    # Clean up (close) all open test tickets
    python agent/scripts/create_test_tickets.py --cleanup

Place in: agent/scripts/create_test_tickets.py
"""

import argparse
import json
import os
import sys
from datetime import datetime
from pathlib import Path

# ─────────────────────────────────────────────────────────────────────
# Load .env file (best-effort)
# ─────────────────────────────────────────────────────────────────────
def _load_dotenv():
    """
    Try to load .env file using python-dotenv.
    Searches current directory and parent directories up to 3 levels.
    Falls back silently if python-dotenv is not installed.
    """
    try:
        from dotenv import load_dotenv

        # Search for .env starting from cwd, walking up
        search_dir = Path.cwd()
        for _ in range(4):  # cwd + 3 parent levels
            env_file = search_dir / ".env"
            if env_file.exists():
                load_dotenv(env_file)
                return str(env_file)
            search_dir = search_dir.parent

        # Also try dotenv without a specific path (python-dotenv's own search)
        load_dotenv()
        return None

    except ImportError:
        return None

_dotenv_path = _load_dotenv()


# ─────────────────────────────────────────────────────────────────────
# Dependency check
# ─────────────────────────────────────────────────────────────────────
try:
    import httpx
except ImportError:
    print("httpx not installed. Run: pip install httpx")
    print("Or activate the project venv: source .venv/bin/activate")
    sys.exit(1)


# =============================================================================
# Configuration Helpers
# =============================================================================

DEFAULT_INSTANCE = "https://dev341394.service-now.com"
DEFAULT_USERNAME = "admin"
ASSIGNMENT_GROUP_NAME = "Help Desk"

# Caller users - these should exist in a default ServiceNow PDI.
# PDI comes pre-loaded with demo users.
DEMO_CALLERS = {
    "abel.tuter": "Abel Tuter",
    "beth.anglin": "Beth Anglin",
    "charlie.whitherspoon": "Charlie Whitherspoon",
    "david.loo": "David Loo",
    "fred.luddy": "Fred Luddy",
}


def _env(key: str, default: str = "") -> str:
    """
    Get an environment variable, checking multiple prefixes.

    Checks in order:
      1. SERVICENOW_{key}       (standard)
      2. LUCID_SERVICENOW_{key} (project .env convention)
      3. SNOW_{key}             (common shorthand)

    Returns default if none found.
    """
    return (
        os.environ.get(f"SERVICENOW_{key}")
        or os.environ.get(f"LUCID_SERVICENOW_{key}")
        or os.environ.get(f"SNOW_{key}")
        or default
    )


def normalize_instance_url(url: str) -> str:
    """
    Ensure the instance URL has a proper https:// scheme.

    Handles common formats from .env files:
      dev341394.service-now.com       → https://dev341394.service-now.com
      http://dev341394.service-now.com → https://dev341394.service-now.com
      https://dev341394.service-now.com → https://dev341394.service-now.com (unchanged)
    """
    url = url.strip().rstrip("/")

    if not url:
        return ""

    # Already has https
    if url.startswith("https://"):
        return url

    # Has http — upgrade to https (ServiceNow always uses HTTPS)
    if url.startswith("http://"):
        return "https://" + url[7:]

    # Bare hostname — prepend https
    return "https://" + url


# =============================================================================
# Test Ticket Scenarios
# =============================================================================

TEST_SCENARIOS = {
    "password_reset": {
        "name": "Password Reset - Happy Path",
        "short_description": "Password reset needed for jsmith",
        "description": (
            "User John Smith (jsmith) called saying he forgot his password "
            "and is locked out of his workstation. He has verified his identity "
            "via security questions. Please reset his Active Directory password "
            "and provide a temporary password."
        ),
        "category": "Software",
        "subcategory": "Password Reset",
        "impact": "3",       # Low
        "urgency": "3",      # Low
        "caller": "abel.tuter",
        "expected_classification": "password_reset",
        "expected_confidence": ">0.9",
        "expected_path": "trigger → classify → validate → execute → notify → end",
    },

    "password_reset_admin": {
        "name": "Password Reset - Admin Account (should fail validation)",
        "short_description": "Reset password for administrator account",
        "description": (
            "Need to reset the password for the administrator account. "
            "The admin password has expired and we need access urgently."
        ),
        "category": "Software",
        "subcategory": "Password Reset",
        "impact": "2",       # Medium
        "urgency": "2",      # Medium
        "caller": "beth.anglin",
        "expected_classification": "password_reset",
        "expected_confidence": ">0.8",
        "expected_path": "trigger → classify → validate(FAIL: admin deny list) → escalate",
    },

    "password_reset_vague": {
        "name": "Password Reset - Vague (low confidence expected)",
        "short_description": "Can't log in",
        "description": (
            "I can't get into my computer. It was working yesterday. "
            "Can someone help?"
        ),
        "category": "Software",
        "subcategory": "Operating System",
        "impact": "3",
        "urgency": "3",
        "caller": "charlie.whitherspoon",
        "expected_classification": "unknown or password_reset",
        "expected_confidence": "<0.8",
        "expected_path": "trigger → classify(low confidence) → escalate",
    },

    "group_access_add": {
        "name": "Group Access - Add User to Group",
        "short_description": "Add mjohnson to Finance-ReadOnly group",
        "description": (
            "Please add Mary Johnson (mjohnson) to the Finance-ReadOnly "
            "Active Directory group. She has been transferred to the Finance "
            "department effective today. Approved by her manager David Chen."
        ),
        "category": "Software",
        "subcategory": "Access / Permissions",
        "impact": "3",
        "urgency": "3",
        "caller": "david.loo",
        "expected_classification": "group_access_add",
        "expected_confidence": ">0.9",
        "expected_path": "trigger → classify → validate → execute → notify → end",
    },

    "file_permission": {
        "name": "File Permission Request",
        "short_description": "Need read access to Q4 financial reports",
        "description": (
            "I need read access to the Q4 financial reports folder located at "
            "\\\\fileserver\\finance\\Q4-Reports. I'm working on the annual "
            "audit and need to review these documents. My username is bsmith."
        ),
        "category": "Software",
        "subcategory": "Access / Permissions",
        "impact": "3",
        "urgency": "3",
        "caller": "fred.luddy",
        "expected_classification": "file_permission",
        "expected_confidence": ">0.85",
        "expected_path": "trigger → classify → validate → execute → notify → end",
    },

    "not_it_ticket": {
        "name": "Non-IT Request (should classify as unknown)",
        "short_description": "Office supplies needed",
        "description": (
            "We need more printer paper and toner cartridges for the "
            "3rd floor printer room. Also the coffee machine is broken again."
        ),
        "category": "Inquiry / Help",
        "subcategory": "General",
        "impact": "3",
        "urgency": "3",
        "caller": "abel.tuter",
        "expected_classification": "unknown",
        "expected_confidence": "<0.5",
        "expected_path": "trigger → classify(unknown) → escalate",
    },

    "password_reset_multiple": {
        "name": "Password Reset - Ambiguous (multiple users)",
        "short_description": "Password resets for new hires",
        "description": (
            "We have three new hires starting Monday: Tom Wilson (twilson), "
            "Sarah Park (spark), and Jim Davis (jdavis). They all need their "
            "initial Active Directory passwords set up. Can you handle all three?"
        ),
        "category": "Software",
        "subcategory": "Password Reset",
        "impact": "3",
        "urgency": "2",
        "caller": "beth.anglin",
        "expected_classification": "password_reset",
        "expected_confidence": "~0.7-0.85 (ambiguous)",
        "expected_path": "trigger → classify → escalate (or validate → fail: ambiguous)",
    },
}


# =============================================================================
# ServiceNow API Client
# =============================================================================

class ServiceNowTestClient:
    """Lightweight ServiceNow REST API client for test ticket management."""

    def __init__(self, instance_url: str, username: str, password: str):
        self.base_url = instance_url.rstrip("/")
        self.auth = (username, password)
        self.headers = {
            "Accept": "application/json",
            "Content-Type": "application/json",
        }

    def _get(self, table: str, params: dict = None) -> list[dict]:
        """GET records from a table."""
        url = f"{self.base_url}/api/now/table/{table}"
        with httpx.Client(timeout=30.0) as client:
            resp = client.get(url, params=params, auth=self.auth, headers=self.headers)
            resp.raise_for_status()
            return resp.json().get("result", [])

    def _post(self, table: str, data: dict) -> dict:
        """POST a new record to a table."""
        url = f"{self.base_url}/api/now/table/{table}"
        with httpx.Client(timeout=30.0) as client:
            resp = client.post(url, json=data, auth=self.auth, headers=self.headers)
            resp.raise_for_status()
            return resp.json().get("result", {})

    def _patch(self, table: str, sys_id: str, data: dict) -> dict:
        """PATCH an existing record."""
        url = f"{self.base_url}/api/now/table/{table}/{sys_id}"
        with httpx.Client(timeout=30.0) as client:
            resp = client.patch(url, json=data, auth=self.auth, headers=self.headers)
            resp.raise_for_status()
            return resp.json().get("result", {})

    def test_connection(self) -> bool:
        """Test connectivity and auth."""
        try:
            self._get("incident", {"sysparm_limit": "1", "sysparm_fields": "sys_id"})
            return True
        except httpx.HTTPStatusError as e:
            if e.response.status_code == 401:
                print(f"Authentication failed (401). Check username/password.")
            else:
                print(f"HTTP error: {e.response.status_code}")
            return False
        except httpx.ConnectError as e:
            print(f"Connection failed: {e}")
            print("  Check the instance URL and your network connection.")
            return False
        except Exception as e:
            print(f"Connection failed: {e}")
            return False

    def get_assignment_group_id(self, group_name: str) -> str | None:
        """Look up assignment group sys_id by name."""
        results = self._get("sys_user_group", {
            "sysparm_query": f"name={group_name}",
            "sysparm_limit": "1",
            "sysparm_fields": "sys_id,name",
        })
        if results:
            return results[0]["sys_id"]
        return None

    def get_caller_id(self, username: str) -> str | None:
        """Look up user sys_id by username."""
        results = self._get("sys_user", {
            "sysparm_query": f"user_name={username}",
            "sysparm_limit": "1",
            "sysparm_fields": "sys_id,user_name,name",
        })
        if results:
            return results[0]["sys_id"]
        return None

    def create_incident(self, data: dict) -> dict:
        """Create a new incident. Returns the full result."""
        return self._post("incident", data)

    def list_incidents(
        self,
        assignment_group_id: str,
        state: str | None = None,
        limit: int = 50,
    ) -> list[dict]:
        """List incidents for an assignment group."""
        query = f"assignment_group={assignment_group_id}"
        if state:
            query += f"^state={state}"

        return self._get("incident", {
            "sysparm_query": query + "^ORDERBYDESCopened_at",
            "sysparm_limit": str(limit),
            "sysparm_fields": "sys_id,number,short_description,state,caller_id,opened_at",
            "sysparm_display_value": "true",
        })

    def close_incident(self, sys_id: str, close_notes: str = "Test cleanup") -> dict:
        """Close an incident."""
        return self._patch("incident", sys_id, {
            "state": "7",  # Closed
            "close_code": "Closed/Resolved by Caller",
            "close_notes": close_notes,
        })


# =============================================================================
# Actions
# =============================================================================

def do_create(
    client: ServiceNowTestClient,
    group_id: str,
    scenario_keys: list[str],
    verbose: bool = True,
) -> list[dict]:
    """Create test tickets. Returns list of created ticket info."""
    created = []
    caller_cache: dict[str, str | None] = {}

    for key in scenario_keys:
        scenario = TEST_SCENARIOS.get(key)
        if not scenario:
            print(f"  ⚠  Unknown scenario: {key}")
            continue

        # Resolve caller (with cache)
        caller_username = scenario.get("caller", "abel.tuter")
        if caller_username not in caller_cache:
            caller_cache[caller_username] = client.get_caller_id(caller_username)
            if not caller_cache[caller_username]:
                print(f"  ⚠  Caller '{caller_username}' not found in PDI, using null")

        caller_id = caller_cache[caller_username]

        # Build incident payload
        payload = {
            "short_description": scenario["short_description"],
            "description": scenario["description"],
            "assignment_group": group_id,
            "category": scenario.get("category", "Software"),
            "subcategory": scenario.get("subcategory", ""),
            "impact": scenario.get("impact", "3"),
            "urgency": scenario.get("urgency", "3"),
            "state": "1",  # New
        }
        if caller_id:
            payload["caller_id"] = caller_id

        # Create it
        result = client.create_incident(payload)
        number = result.get("number", "???")
        sys_id = result.get("sys_id", "???")

        created.append({
            "scenario": key,
            "number": number,
            "sys_id": sys_id,
            "name": scenario["name"],
        })

        print(f"  ✓  {number}: {scenario['name']}")
        if verbose:
            print(f"       Expected: {scenario['expected_classification']} "
                  f"(confidence {scenario['expected_confidence']})")
            print(f"       Path: {scenario['expected_path']}")
            print()

    return created


def do_list(client: ServiceNowTestClient, group_id: str):
    """List current tickets in the assignment group."""
    state_icons = {
        "New": "🆕", "In Progress": "🔄", "On Hold": "⏸ ",
        "Resolved": "✅", "Closed": "⚫", "Canceled": "🚫",
    }

    tickets = client.list_incidents(group_id)
    if not tickets:
        print("  (no tickets found)")
        return

    for t in tickets:
        state = t.get("state", "?")
        icon = state_icons.get(state, "❓")
        number = t.get("number", "???")
        desc = t.get("short_description", "")[:65]
        opened = t.get("opened_at", "")
        print(f"  {icon}  {number}  [{state:<12}]  {desc}")


def do_cleanup(client: ServiceNowTestClient, group_id: str):
    """Close all New and In Progress tickets."""
    closed_count = 0

    for state_code in ["1", "2"]:  # New, In Progress
        tickets = client.list_incidents(group_id, state=state_code)
        for t in tickets:
            sys_id = t.get("sys_id")
            number = t.get("number", "???")
            if sys_id:
                try:
                    client.close_incident(sys_id, close_notes="Lucid E2E test cleanup")
                    print(f"  ✓  Closed {number}")
                    closed_count += 1
                except Exception as e:
                    print(f"  ✗  Failed to close {number}: {e}")

    print(f"\n  Closed {closed_count} ticket(s)")


# =============================================================================
# Main
# =============================================================================

def main():
    parser = argparse.ArgumentParser(
        description="ServiceNow Test Ticket Generator for Lucid IT Agent",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Scenarios:
  password_reset           Password reset happy path (jsmith)
  password_reset_admin     Admin account (should fail validation deny list)
  password_reset_vague     Vague description (low confidence → escalate)
  password_reset_multiple  Multiple users in one ticket (ambiguous → escalate)
  group_access_add         Add user to AD group
  file_permission          File/folder permission request
  not_it_ticket            Non-IT request (classify as unknown → escalate)

Environment variables (checked in order):
  SERVICENOW_INSTANCE / LUCID_SERVICENOW_INSTANCE / SNOW_INSTANCE
  SERVICENOW_USERNAME / LUCID_SERVICENOW_USERNAME / SNOW_USERNAME
  SERVICENOW_PASSWORD / LUCID_SERVICENOW_PASSWORD / SNOW_PASSWORD

Loads .env file automatically if python-dotenv is installed.

Examples:
  %(prog)s --all                                   Create all scenarios
  %(prog)s --type password_reset                    Create one scenario
  %(prog)s --type password_reset --type not_it_ticket  Create two scenarios
  %(prog)s --list                                  List tickets in queue
  %(prog)s --cleanup                               Close all open tickets
        """,
    )

    # Connection args — use _env() helper to check multiple prefixes
    parser.add_argument("--instance",
        default=_env("INSTANCE", DEFAULT_INSTANCE),
        help=f"ServiceNow instance URL (default: {DEFAULT_INSTANCE})")
    parser.add_argument("--username",
        default=_env("USERNAME", DEFAULT_USERNAME),
        help="ServiceNow username (default: admin)")
    parser.add_argument("--password",
        default=_env("PASSWORD", ""),
        help="ServiceNow password")
    parser.add_argument("--group",
        default=ASSIGNMENT_GROUP_NAME,
        help=f"Assignment group name (default: {ASSIGNMENT_GROUP_NAME})")

    # Action args
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--all", action="store_true",
        help="Create all test scenarios")
    action.add_argument("--type", action="append", dest="types",
        choices=list(TEST_SCENARIOS.keys()),
        help="Create specific scenario(s). Can repeat.")
    action.add_argument("--list", action="store_true",
        help="List current tickets in the assignment group")
    action.add_argument("--cleanup", action="store_true",
        help="Close all open tickets in the assignment group")

    # Output args
    parser.add_argument("--quiet", "-q", action="store_true",
        help="Minimal output")
    parser.add_argument("--json", action="store_true",
        help="Output created ticket info as JSON")

    args = parser.parse_args()

    # Normalize instance URL (handle missing https://)
    args.instance = normalize_instance_url(args.instance)

    # Validate password
    if not args.password:
        print("ERROR: ServiceNow password required.\n")
        print("Options (in order of preference):")
        print("  1. Install python-dotenv and use your .env file:")
        print("       pip install python-dotenv")
        print("  2. Source the .env file first:")
        print("       set -a && source .env && set +a")
        print("  3. Set env var directly:")
        print("       export SERVICENOW_PASSWORD=your_password")
        print("  4. Pass on command line:")
        print("       --password 'your_password'")
        sys.exit(1)

    # Connect
    client = ServiceNowTestClient(args.instance, args.username, args.password)

    print(f"\n  Instance:  {args.instance}")
    print(f"  Username:  {args.username}")
    print(f"  Group:     {args.group}")
    if _dotenv_path:
        print(f"  .env:      {_dotenv_path}")

    print(f"\n  Testing connection...", end=" ")
    if not client.test_connection():
        print("FAILED")
        sys.exit(1)
    print("OK")

    group_id = client.get_assignment_group_id(args.group)
    if not group_id:
        print(f"\n  ERROR: Assignment group '{args.group}' not found")
        sys.exit(1)
    print(f"  Group ID:  {group_id}")
    print()

    # Execute action
    if args.list:
        print(f"  Tickets in '{args.group}':\n")
        do_list(client, group_id)

    elif args.cleanup:
        print(f"  Cleaning up open tickets in '{args.group}'...\n")
        do_cleanup(client, group_id)

    else:
        # Create tickets
        scenarios = list(TEST_SCENARIOS.keys()) if args.all else args.types

        print(f"  Creating {len(scenarios)} test ticket(s)...\n")
        created = do_create(client, group_id, scenarios, verbose=not args.quiet)

        if args.json:
            print(json.dumps(created, indent=2))

        print(f"  {'─' * 55}")
        print(f"  Created {len(created)} / {len(scenarios)} tickets\n")

        if created:
            print(f"  Next step — run the agent:")
            print(f"    python -m agent.runtime.cli \\")
            print(f"      --admin-url http://localhost:5000 \\")
            print(f"      --agent-name test-agent \\")
            print(f"      --log-level DEBUG")

    print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
