"""ServiceNow exporter — pushes generated tickets to a ServiceNow PDI.

Wraps the same REST API pattern as the existing create_test_tickets.py
script but works with GeneratedTicket objects from the generator framework.
"""

from __future__ import annotations

import logging
import os
from typing import Any

try:
    import httpx
except ImportError:
    httpx = None  # type: ignore

from ..models import GeneratedTicket

logger = logging.getLogger(__name__)

# PDI demo user mapping (username -> PDI user_name for caller lookup)
PDI_CALLERS = {
    "abel.tuter": "abel.tuter",
    "beth.anglin": "beth.anglin",
    "charlie.whitherspoon": "charlie.whitherspoon",
    "david.loo": "david.loo",
    "fred.luddy": "fred.luddy",
}


class ServiceNowExporter:
    """Push generated tickets to a ServiceNow instance as incidents.

    Usage:
        exporter = ServiceNowExporter.from_env()
        results = exporter.create_tickets(tickets)
        exporter.cleanup_tickets(results)
    """

    def __init__(
        self,
        instance_url: str,
        username: str,
        password: str,
        assignment_group: str = "Help Desk",
    ):
        if httpx is None:
            raise ImportError("httpx is required: pip install httpx")

        self.base_url = instance_url.rstrip("/")
        self.auth = (username, password)
        self.headers = {
            "Accept": "application/json",
            "Content-Type": "application/json",
        }
        self.assignment_group = assignment_group
        self._group_id: str | None = None
        self._caller_cache: dict[str, str | None] = {}

    @classmethod
    def from_env(cls, assignment_group: str = "Help Desk") -> ServiceNowExporter:
        """Create exporter from environment variables.

        Checks SERVICENOW_*, LUCID_SERVICENOW_*, and SNOW_* prefixes.
        """
        def _env(key: str, default: str = "") -> str:
            return (
                os.environ.get(f"SERVICENOW_{key}")
                or os.environ.get(f"LUCID_SERVICENOW_{key}")
                or os.environ.get(f"SNOW_{key}")
                or default
            )

        instance = _env("INSTANCE", "https://dev341394.service-now.com")
        if not instance.startswith("http"):
            instance = f"https://{instance}"

        username = _env("USERNAME", "admin")
        password = _env("PASSWORD")

        if not password:
            raise ValueError(
                "ServiceNow password not set. "
                "Set SERVICENOW_PASSWORD, LUCID_SERVICENOW_PASSWORD, or SNOW_PASSWORD"
            )

        return cls(instance, username, password, assignment_group)

    def test_connection(self) -> bool:
        """Test connectivity and authentication."""
        try:
            self._get("incident", {"sysparm_limit": "1", "sysparm_fields": "sys_id"})
            return True
        except Exception as e:
            logger.error(f"Connection test failed: {e}")
            return False

    def create_tickets(
        self,
        tickets: list[GeneratedTicket],
        verbose: bool = True,
    ) -> list[GeneratedTicket]:
        """Create tickets in ServiceNow. Updates tickets in-place with sys_id/number.

        Returns the same list with snow_sys_id and snow_number populated.
        """
        group_id = self._get_group_id()
        if not group_id:
            raise ValueError(f"Assignment group '{self.assignment_group}' not found")

        created: list[GeneratedTicket] = []

        for ticket in tickets:
            caller_id = self._resolve_caller(ticket.caller_username)

            payload: dict[str, Any] = {
                "short_description": ticket.short_description,
                "description": ticket.description,
                "assignment_group": group_id,
                "category": ticket.category,
                "subcategory": ticket.subcategory,
                "impact": ticket.impact,
                "urgency": ticket.urgency,
                "state": "1",  # New
            }
            if caller_id:
                payload["caller_id"] = caller_id

            try:
                result = self._post("incident", payload)
                ticket.snow_sys_id = result.get("sys_id")
                ticket.snow_number = result.get("number")
                created.append(ticket)

                if verbose:
                    logger.info(
                        f"  ✓  {ticket.snow_number}: "
                        f"{ticket.scenario_id} / {ticket.variation_label}"
                    )
            except Exception as e:
                logger.error(f"  ✗  Failed to create ticket: {e}")

        return created

    def cleanup_open_tickets(self) -> int:
        """Close all New and In Progress tickets in the assignment group."""
        group_id = self._get_group_id()
        if not group_id:
            return 0

        closed = 0
        for state in ["1", "2"]:  # New, In Progress
            incidents = self._get("incident", {
                "sysparm_query": f"assignment_group={group_id}^state={state}",
                "sysparm_limit": "200",
                "sysparm_fields": "sys_id,number",
            })
            for inc in incidents:
                try:
                    self._patch("incident", inc["sys_id"], {
                        "state": "7",
                        "close_code": "Closed/Resolved by Caller",
                        "close_notes": "Test data generator cleanup",
                    })
                    closed += 1
                except Exception as e:
                    logger.error(f"Failed to close {inc.get('number')}: {e}")

        return closed

    # --- Internal helpers ---

    def _get_group_id(self) -> str | None:
        if self._group_id:
            return self._group_id
        results = self._get("sys_user_group", {
            "sysparm_query": f"name={self.assignment_group}",
            "sysparm_limit": "1",
            "sysparm_fields": "sys_id",
        })
        if results:
            self._group_id = results[0]["sys_id"]
        return self._group_id

    def _resolve_caller(self, username: str) -> str | None:
        pdi_user = PDI_CALLERS.get(username, username)
        if pdi_user in self._caller_cache:
            return self._caller_cache[pdi_user]

        results = self._get("sys_user", {
            "sysparm_query": f"user_name={pdi_user}",
            "sysparm_limit": "1",
            "sysparm_fields": "sys_id",
        })
        caller_id = results[0]["sys_id"] if results else None
        self._caller_cache[pdi_user] = caller_id
        return caller_id

    def _get(self, table: str, params: dict | None = None) -> list[dict]:
        url = f"{self.base_url}/api/now/table/{table}"
        with httpx.Client(timeout=30.0) as client:
            resp = client.get(url, params=params, auth=self.auth, headers=self.headers)
            resp.raise_for_status()
            return resp.json().get("result", [])

    def _post(self, table: str, data: dict) -> dict:
        url = f"{self.base_url}/api/now/table/{table}"
        with httpx.Client(timeout=30.0) as client:
            resp = client.post(url, json=data, auth=self.auth, headers=self.headers)
            resp.raise_for_status()
            return resp.json().get("result", {})

    def _patch(self, table: str, sys_id: str, data: dict) -> dict:
        url = f"{self.base_url}/api/now/table/{table}/{sys_id}"
        with httpx.Client(timeout=30.0) as client:
            resp = client.patch(url, json=data, auth=self.auth, headers=self.headers)
            resp.raise_for_status()
            return resp.json().get("result", {})
