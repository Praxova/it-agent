"""ServiceNow client for workflow runtime."""
from __future__ import annotations
import logging
from typing import Any
from dataclasses import dataclass

import httpx

logger = logging.getLogger(__name__)


@dataclass
class ServiceNowCredentials:
    """ServiceNow connection credentials."""
    instance_url: str
    username: str
    password: str


@dataclass
class Ticket:
    """ServiceNow incident ticket."""
    sys_id: str
    number: str
    short_description: str
    description: str
    caller_id: str
    state: str
    assignment_group: str
    caller_name: str = ""     # Display name resolved from caller_id sys_id
    caller_username: str = "" # AD username resolved from caller_id sys_id

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Ticket":
        """Create Ticket from ServiceNow API response."""
        return cls(
            sys_id=data.get("sys_id", ""),
            number=data.get("number", ""),
            short_description=data.get("short_description", ""),
            description=data.get("description", ""),
            caller_id=data.get("caller_id", {}).get("value", "") if isinstance(data.get("caller_id"), dict) else data.get("caller_id", ""),
            state=data.get("state", ""),
            assignment_group=data.get("assignment_group", {}).get("value", "") if isinstance(data.get("assignment_group"), dict) else data.get("assignment_group", ""),
        )

    def to_ticket_data(self) -> dict[str, Any]:
        """Convert to ticket_data format for ExecutionContext."""
        return {
            "sys_id": self.sys_id,
            "number": self.number,
            "short_description": self.short_description,
            "description": self.description,
            "caller_id": self.caller_id,
            "caller_name": self.caller_name,
            "caller_username": self.caller_username,
            "state": self.state,
            "assignment_group": self.assignment_group,
        }


class ServiceNowClient:
    """
    Client for ServiceNow REST API.

    Handles:
    - Polling for new tickets
    - Updating ticket state
    - Adding work notes and comments
    """

    def __init__(self, credentials: ServiceNowCredentials):
        self.credentials = credentials
        self.base_url = credentials.instance_url.rstrip("/")
        self._auth = (credentials.username, credentials.password)

    async def poll_queue(
        self,
        assignment_group: str,
        state: str = "1",  # New
        limit: int = 10,
    ) -> list[Ticket]:
        """
        Poll for new tickets assigned to a group.

        Args:
            assignment_group: ServiceNow assignment group sys_id or name
            state: Ticket state to filter (1=New, 2=In Progress)
            limit: Maximum tickets to return

        Returns:
            List of Ticket objects
        """
        url = f"{self.base_url}/api/now/table/incident"
        params = {
            "sysparm_query": f"assignment_group.name={assignment_group}^state={state}",
            "sysparm_limit": str(limit),
            "sysparm_display_value": "false",
        }

        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    params=params,
                    auth=self._auth,
                    headers={"Accept": "application/json"},
                    timeout=30.0,
                )
                response.raise_for_status()

                data = response.json()
                tickets = [Ticket.from_dict(t) for t in data.get("result", [])]

                logger.info(f"Polled {len(tickets)} tickets from queue")
                return tickets

        except Exception as e:
            logger.error(f"Failed to poll ServiceNow: {e}")
            return []

    async def resolve_caller(self, caller_sys_id: str) -> dict[str, str]:
        """
        Resolve a caller sys_id to display name and AD username.

        Args:
            caller_sys_id: The sys_id GUID of the caller from the incident record.

        Returns:
            Dict with keys: display_name, username, email.
            Returns safe defaults if the lookup fails so ticket processing continues.
        """
        if not caller_sys_id:
            return {"display_name": "", "username": "", "email": ""}

        url = f"{self.base_url}/api/now/table/sys_user/{caller_sys_id}"
        params = {"sysparm_fields": "user_name,name,email"}

        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    params=params,
                    auth=self._auth,
                    headers={"Accept": "application/json"},
                    timeout=10.0,
                )
                response.raise_for_status()
                result = response.json().get("result", {})
                return {
                    "display_name": result.get("name", ""),
                    "username": result.get("user_name", ""),
                    "email": result.get("email", ""),
                }
        except Exception as e:
            logger.warning(f"Failed to resolve caller {caller_sys_id}: {e}")
            return {"display_name": "", "username": "", "email": ""}

    async def get_ticket(self, sys_id: str) -> Ticket | None:
        """Get a single ticket by sys_id."""
        url = f"{self.base_url}/api/now/table/incident/{sys_id}"

        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    auth=self._auth,
                    headers={"Accept": "application/json"},
                    timeout=30.0,
                )

                if response.status_code == 404:
                    return None

                response.raise_for_status()
                data = response.json()
                return Ticket.from_dict(data.get("result", {}))

        except Exception as e:
            logger.error(f"Failed to get ticket {sys_id}: {e}")
            return None

    async def update_ticket(
        self,
        sys_id: str,
        updates: dict[str, Any],
    ) -> bool:
        """
        Update a ticket.

        Args:
            sys_id: Ticket sys_id
            updates: Fields to update

        Returns:
            True if successful
        """
        url = f"{self.base_url}/api/now/table/incident/{sys_id}"

        try:
            async with httpx.AsyncClient() as client:
                response = await client.patch(
                    url,
                    json=updates,
                    auth=self._auth,
                    headers={
                        "Accept": "application/json",
                        "Content-Type": "application/json",
                    },
                    timeout=30.0,
                )
                response.raise_for_status()

                logger.info(f"Updated ticket {sys_id}")
                return True

        except Exception as e:
            logger.error(f"Failed to update ticket {sys_id}: {e}")
            return False

    async def add_work_note(self, sys_id: str, note: str) -> bool:
        """Add a work note to a ticket."""
        return await self.update_ticket(sys_id, {"work_notes": note})

    async def add_comment(self, sys_id: str, comment: str) -> bool:
        """Add a customer-visible comment to a ticket."""
        return await self.update_ticket(sys_id, {"comments": comment})

    async def set_state(
        self,
        sys_id: str,
        state: str,
        close_code: str | None = None,
        close_notes: str | None = None,
    ) -> bool:
        """
        Set ticket state.

        Args:
            sys_id: Ticket sys_id
            state: New state (1=New, 2=In Progress, 6=Resolved, 7=Closed)
            close_code: Resolution code (for resolved/closed)
            close_notes: Resolution notes
        """
        updates = {"state": state}

        if close_code:
            updates["close_code"] = close_code
        if close_notes:
            updates["close_notes"] = close_notes

        return await self.update_ticket(sys_id, updates)

    async def assign_to_group(self, sys_id: str, group: str) -> bool:
        """Assign ticket to a different group."""
        return await self.update_ticket(sys_id, {"assignment_group": group})

    async def get_customer_comments(
        self,
        sys_id: str,
        since: str | None = None,
    ) -> list[dict[str, Any]]:
        """
        Get customer-visible comments (journal entries) for a ticket.

        Queries the sys_journal_field table for entries where:
        - element_id = sys_id (the ticket)
        - element = "comments" (customer-visible, not "work_notes")
        - Optionally filtered by sys_created_on > since

        Args:
            sys_id: The ticket's sys_id
            since: ISO datetime string — only return comments after this time
                   Format: "2026-02-08 15:30:00" (ServiceNow format)

        Returns:
            List of dicts with keys: sys_id, sys_created_on, value (comment text),
            sys_created_by (username who posted)

        The returned list is ordered by sys_created_on ascending (oldest first).
        """
        url = f"{self.base_url}/api/now/table/sys_journal_field"

        query = f"element_id={sys_id}^element=comments"
        if since:
            # Convert ISO format to ServiceNow format if needed
            sn_since = since.replace("T", " ").split(".")[0].split("+")[0]
            query += f"^sys_created_on>{sn_since}"

        params = {
            "sysparm_query": query,
            "sysparm_fields": "sys_id,sys_created_on,value,sys_created_by",
            "sysparm_limit": "20",
            "sysparm_orderby": "sys_created_on",
        }

        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    params=params,
                    auth=self._auth,
                    headers={"Accept": "application/json"},
                    timeout=30.0,
                )
                response.raise_for_status()

                data = response.json()
                results = data.get("result", [])

                # Filter out comments posted by our own service account
                results = [
                    r for r in results
                    if r.get("sys_created_by") != self.credentials.username
                ]

                logger.info(
                    f"Found {len(results)} customer comments for ticket {sys_id}"
                )
                return results

        except Exception as e:
            logger.error(f"Failed to get customer comments for {sys_id}: {e}")
            return []
