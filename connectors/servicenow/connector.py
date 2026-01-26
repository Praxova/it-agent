"""ServiceNow connector implementation."""

import logging
from datetime import datetime
from typing import Any

from ..base import BaseConnector, Ticket, TicketState, TicketUpdate
from .client import ServiceNowClient
from .models import IncidentResponse

logger = logging.getLogger(__name__)


class ServiceNowConnector(BaseConnector):
    """ServiceNow implementation of BaseConnector.

    Provides methods to interact with ServiceNow incidents through the REST API.

    Attributes:
        client: Underlying ServiceNowClient for API calls.
        assignment_group: Default assignment group to filter tickets.
    """

    def __init__(
        self,
        instance: str,
        username: str,
        password: str,
        assignment_group: str = "Helpdesk",
    ) -> None:
        """Initialize ServiceNow connector.

        Args:
            instance: ServiceNow instance name (e.g., 'dev12345.service-now.com').
            username: Basic auth username.
            password: Basic auth password.
            assignment_group: Assignment group name to filter tickets (default: 'Helpdesk').
        """
        self.client = ServiceNowClient(instance, username, password)
        self.assignment_group = assignment_group

    async def close(self) -> None:
        """Close the underlying HTTP client."""
        await self.client.close()

    async def __aenter__(self) -> "ServiceNowConnector":
        """Context manager entry."""
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Context manager exit."""
        await self.close()

    def _build_query(
        self, since: datetime | None = None, include_states: list[int] | None = None
    ) -> str:
        """Build ServiceNow query string.

        Args:
            since: Only include incidents updated after this timestamp.
            include_states: List of state values to include (default: [1, 2] - New, In Progress).

        Returns:
            ServiceNow query string.
        """
        if include_states is None:
            include_states = [TicketState.NEW, TicketState.IN_PROGRESS]

        # Build state filter
        state_filter = "^OR".join([f"state={state}" for state in include_states])

        # Build base query: assignment_group AND (state1 OR state2)
        query = f"assignment_group.name={self.assignment_group}^{state_filter}"

        # Add time filter if provided
        if since:
            # Format datetime for ServiceNow: "2024-01-15 14:30:00"
            since_str = since.strftime("%Y-%m-%d %H:%M:%S")
            query += f"^sys_updated_on>{since_str}"

        # Order by update time
        query += "^ORDERBYsys_updated_on"

        return query

    async def poll_queue(self, since: datetime | None = None) -> list[Ticket]:
        """Fetch tickets updated since given time.

        Filters by assignment group and open states (New, In Progress).

        Args:
            since: Only fetch tickets updated after this timestamp.
                   If None, fetch all open tickets.

        Returns:
            List of tickets matching the criteria.
        """
        query = self._build_query(since=since)
        logger.info(
            f"Polling ServiceNow queue for group '{self.assignment_group}' "
            f"(since: {since or 'all time'})"
        )

        incident_dicts = await self.client.get_incidents(query=query)
        logger.info(f"Found {len(incident_dicts)} incidents")

        tickets = []
        for incident_dict in incident_dicts:
            try:
                incident = IncidentResponse(**incident_dict)
                ticket = incident.to_ticket()
                tickets.append(ticket)
            except Exception as e:
                logger.error(
                    f"Failed to parse incident {incident_dict.get('number', 'unknown')}: {e}"
                )
                continue

        return tickets

    async def get_ticket(self, ticket_id: str) -> Ticket:
        """Fetch a single ticket by ID.

        Args:
            ticket_id: ServiceNow sys_id of the incident.

        Returns:
            The requested ticket.

        Raises:
            Exception: If ticket not found or request fails.
        """
        logger.info(f"Fetching ticket {ticket_id}")
        incident_dict = await self.client.get_incident(ticket_id)

        incident = IncidentResponse(**incident_dict)
        return incident.to_ticket()

    async def update_ticket(self, ticket_id: str, update: TicketUpdate) -> Ticket:
        """Update ticket fields.

        Args:
            ticket_id: ServiceNow sys_id of the incident.
            update: Fields to update.

        Returns:
            The updated ticket.

        Raises:
            Exception: If update fails.
        """
        logger.info(f"Updating ticket {ticket_id}")

        # Build ServiceNow update payload
        update_data: dict[str, Any] = {}

        if update.state is not None:
            update_data["state"] = str(int(update.state))

        if update.assigned_to is not None:
            update_data["assigned_to"] = update.assigned_to

        if update.work_notes is not None:
            update_data["work_notes"] = update.work_notes

        if update.comments is not None:
            update_data["comments"] = update.comments

        incident_dict = await self.client.update_incident(ticket_id, update_data)

        incident = IncidentResponse(**incident_dict)
        return incident.to_ticket()

    async def add_work_note(self, ticket_id: str, note: str) -> None:
        """Add internal work note (not visible to caller).

        Args:
            ticket_id: ServiceNow sys_id of the incident.
            note: Internal note text.

        Raises:
            Exception: If operation fails.
        """
        logger.info(f"Adding work note to ticket {ticket_id}")
        await self.client.update_incident(ticket_id, {"work_notes": note})

    async def add_comment(self, ticket_id: str, comment: str) -> None:
        """Add customer-visible comment.

        Args:
            ticket_id: ServiceNow sys_id of the incident.
            comment: Comment text.

        Raises:
            Exception: If operation fails.
        """
        logger.info(f"Adding comment to ticket {ticket_id}")
        await self.client.update_incident(ticket_id, {"comments": comment})

    async def close_ticket(self, ticket_id: str, resolution: str) -> Ticket:
        """Close ticket with resolution notes.

        Sets state to RESOLVED (6) and adds resolution notes as work notes.

        Args:
            ticket_id: ServiceNow sys_id of the incident.
            resolution: Resolution notes describing how the issue was resolved.

        Returns:
            The closed ticket.

        Raises:
            Exception: If operation fails.
        """
        logger.info(f"Closing ticket {ticket_id}")

        update_data = {
            "state": str(int(TicketState.RESOLVED)),
            "work_notes": f"Resolution: {resolution}",
            "close_notes": resolution,
        }

        incident_dict = await self.client.update_incident(ticket_id, update_data)

        incident = IncidentResponse(**incident_dict)
        return incident.to_ticket()
