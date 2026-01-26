"""Tests for ServiceNow connector."""

from datetime import datetime
from typing import Any
from unittest.mock import AsyncMock

import pytest

from connectors.base import TicketState, TicketUpdate
from connectors.servicenow.connector import ServiceNowConnector


@pytest.mark.asyncio
class TestServiceNowConnector:
    """Test cases for ServiceNowConnector."""

    @pytest.fixture
    def connector(
        self, servicenow_config: dict[str, str], mock_client: AsyncMock
    ) -> ServiceNowConnector:
        """Create ServiceNowConnector with mocked client."""
        connector = ServiceNowConnector(
            instance=servicenow_config["instance"],
            username=servicenow_config["username"],
            password=servicenow_config["password"],
            assignment_group=servicenow_config["assignment_group"],
        )
        # Replace client with mock
        connector.client = mock_client
        return connector

    async def test_connector_initialization(
        self, servicenow_config: dict[str, str]
    ) -> None:
        """Test connector is initialized with correct configuration."""
        connector = ServiceNowConnector(
            instance=servicenow_config["instance"],
            username=servicenow_config["username"],
            password=servicenow_config["password"],
            assignment_group=servicenow_config["assignment_group"],
        )

        assert connector.assignment_group == servicenow_config["assignment_group"]
        assert connector.client is not None

    async def test_build_query_without_since(
        self, connector: ServiceNowConnector
    ) -> None:
        """Test query building without time filter."""
        query = connector._build_query()

        assert "assignment_group.name=Helpdesk" in query
        assert "state=1^ORstate=2" in query
        assert "ORDERBYsys_updated_on" in query
        assert "sys_updated_on>" not in query

    async def test_build_query_with_since(self, connector: ServiceNowConnector) -> None:
        """Test query building with time filter."""
        since = datetime(2024, 1, 15, 14, 30, 0)
        query = connector._build_query(since=since)

        assert "assignment_group.name=Helpdesk" in query
        assert "state=1^ORstate=2" in query
        assert "sys_updated_on>2024-01-15 14:30:00" in query
        assert "ORDERBYsys_updated_on" in query

    async def test_build_query_custom_states(
        self, connector: ServiceNowConnector
    ) -> None:
        """Test query building with custom states."""
        query = connector._build_query(
            include_states=[TicketState.NEW, TicketState.RESOLVED]
        )

        assert "state=1^ORstate=6" in query

    async def test_poll_queue_success(
        self,
        connector: ServiceNowConnector,
        mock_client: AsyncMock,
        sample_incident_list: list[dict[str, Any]],
    ) -> None:
        """Test successful queue polling."""
        mock_client.get_incidents.return_value = sample_incident_list

        tickets = await connector.poll_queue()

        assert len(tickets) == 3
        assert tickets[0].number == "INC0010001"
        assert tickets[0].state == TicketState.NEW
        assert tickets[1].number == "INC0010002"
        assert tickets[1].state == TicketState.IN_PROGRESS
        mock_client.get_incidents.assert_called_once()

    async def test_poll_queue_with_since(
        self,
        connector: ServiceNowConnector,
        mock_client: AsyncMock,
        sample_incident_list: list[dict[str, Any]],
    ) -> None:
        """Test queue polling with time filter."""
        mock_client.get_incidents.return_value = sample_incident_list
        since = datetime(2024, 1, 15, 15, 0, 0)

        tickets = await connector.poll_queue(since=since)

        assert len(tickets) == 3
        # Verify the query includes the time filter
        call_args = mock_client.get_incidents.call_args
        query = call_args.kwargs.get("query", call_args.args[0] if call_args.args else "")
        assert "sys_updated_on>2024-01-15 15:00:00" in query

    async def test_poll_queue_empty_result(
        self, connector: ServiceNowConnector, mock_client: AsyncMock
    ) -> None:
        """Test queue polling with no results."""
        mock_client.get_incidents.return_value = []

        tickets = await connector.poll_queue()

        assert len(tickets) == 0

    async def test_poll_queue_handles_invalid_incident(
        self, connector: ServiceNowConnector, mock_client: AsyncMock
    ) -> None:
        """Test that invalid incidents are skipped."""
        invalid_incidents = [
            {
                "sys_id": "abc123",
                "number": "INC0010001",
                # Missing required fields
            },
            {
                "sys_id": "def456",
                "number": "INC0010002",
                "short_description": "Valid incident",
                "state": "1",
                "priority": "3",
                "caller_id": {"display_value": "user"},
                "assignment_group": {"display_value": "Helpdesk"},
                "sys_created_on": "2024-01-15 14:30:00",
                "sys_updated_on": "2024-01-15 14:30:00",
            },
        ]
        mock_client.get_incidents.return_value = invalid_incidents

        tickets = await connector.poll_queue()

        # Should only return the valid incident
        assert len(tickets) == 1
        assert tickets[0].number == "INC0010002"

    async def test_get_ticket_success(
        self,
        connector: ServiceNowConnector,
        mock_client: AsyncMock,
        sample_incident: dict[str, Any],
    ) -> None:
        """Test successful ticket retrieval."""
        mock_client.get_incident.return_value = sample_incident

        ticket = await connector.get_ticket("abc123def456")

        assert ticket.id == "abc123def456"
        assert ticket.number == "INC0010001"
        assert ticket.state == TicketState.NEW
        assert ticket.priority == 3
        assert ticket.caller_username == "john.doe"
        assert ticket.assignment_group == "Helpdesk"
        mock_client.get_incident.assert_called_once_with("abc123def456")

    async def test_update_ticket_success(
        self,
        connector: ServiceNowConnector,
        mock_client: AsyncMock,
        incident_fixtures: dict[str, Any],
    ) -> None:
        """Test successful ticket update."""
        updated_incident = incident_fixtures["updated_incident"]
        mock_client.update_incident.return_value = updated_incident

        update = TicketUpdate(
            state=TicketState.RESOLVED, work_notes="Password reset completed"
        )
        ticket = await connector.update_ticket("abc123def456", update)

        assert ticket.state == TicketState.RESOLVED
        call_args = mock_client.update_incident.call_args
        assert call_args.args[0] == "abc123def456"
        assert call_args.args[1]["state"] == "6"
        assert call_args.args[1]["work_notes"] == "Password reset completed"

    async def test_update_ticket_with_all_fields(
        self,
        connector: ServiceNowConnector,
        mock_client: AsyncMock,
        sample_incident: dict[str, Any],
    ) -> None:
        """Test ticket update with all fields."""
        mock_client.update_incident.return_value = sample_incident

        update = TicketUpdate(
            state=TicketState.IN_PROGRESS,
            assigned_to="agent.user",
            work_notes="Working on this",
            comments="Looking into your issue",
        )
        await connector.update_ticket("abc123def456", update)

        call_args = mock_client.update_incident.call_args
        update_data = call_args.args[1]
        assert update_data["state"] == "2"
        assert update_data["assigned_to"] == "agent.user"
        assert update_data["work_notes"] == "Working on this"
        assert update_data["comments"] == "Looking into your issue"

    async def test_add_work_note_success(
        self, connector: ServiceNowConnector, mock_client: AsyncMock
    ) -> None:
        """Test adding work note."""
        mock_client.update_incident.return_value = {}

        await connector.add_work_note("abc123def456", "Internal note")

        mock_client.update_incident.assert_called_once_with(
            "abc123def456", {"work_notes": "Internal note"}
        )

    async def test_add_comment_success(
        self, connector: ServiceNowConnector, mock_client: AsyncMock
    ) -> None:
        """Test adding customer comment."""
        mock_client.update_incident.return_value = {}

        await connector.add_comment("abc123def456", "We are working on this")

        mock_client.update_incident.assert_called_once_with(
            "abc123def456", {"comments": "We are working on this"}
        )

    async def test_close_ticket_success(
        self,
        connector: ServiceNowConnector,
        mock_client: AsyncMock,
        incident_fixtures: dict[str, Any],
    ) -> None:
        """Test closing ticket."""
        updated_incident = incident_fixtures["updated_incident"]
        mock_client.update_incident.return_value = updated_incident

        ticket = await connector.close_ticket(
            "abc123def456", "Password reset successfully"
        )

        assert ticket.state == TicketState.RESOLVED
        call_args = mock_client.update_incident.call_args
        update_data = call_args.args[1]
        assert update_data["state"] == "6"
        assert "Resolution: Password reset successfully" in update_data["work_notes"]
        assert update_data["close_notes"] == "Password reset successfully"

    async def test_context_manager(self, servicenow_config: dict[str, str]) -> None:
        """Test connector can be used as context manager."""
        async with ServiceNowConnector(
            instance=servicenow_config["instance"],
            username=servicenow_config["username"],
            password=servicenow_config["password"],
        ) as connector:
            assert connector is not None

    async def test_close_method(
        self, connector: ServiceNowConnector, mock_client: AsyncMock
    ) -> None:
        """Test close method."""
        await connector.close()
        mock_client.close.assert_called_once()

    async def test_model_conversion(self, sample_incident: dict[str, Any]) -> None:
        """Test incident model conversion to ticket."""
        from connectors.servicenow.models import IncidentResponse

        incident = IncidentResponse(**sample_incident)
        ticket = incident.to_ticket()

        assert ticket.id == sample_incident["sys_id"]
        assert ticket.number == sample_incident["number"]
        assert ticket.short_description == sample_incident["short_description"]
        assert ticket.state == TicketState(int(sample_incident["state"]))
        assert ticket.priority == int(sample_incident["priority"])
        assert ticket.caller_username == "john.doe"
        assert ticket.assignment_group == "Helpdesk"

    async def test_model_conversion_with_string_caller(self) -> None:
        """Test incident model conversion when caller_id is a string."""
        from connectors.servicenow.models import IncidentResponse

        incident_data = {
            "sys_id": "test123",
            "number": "INC0010001",
            "short_description": "Test",
            "state": "1",
            "priority": "3",
            "caller_id": "john.doe",  # String instead of dict
            "assignment_group": "Helpdesk",  # String instead of dict
            "sys_created_on": "2024-01-15 14:30:00",
            "sys_updated_on": "2024-01-15 14:30:00",
        }

        incident = IncidentResponse(**incident_data)
        ticket = incident.to_ticket()

        assert ticket.caller_username == "john.doe"
        assert ticket.assignment_group == "Helpdesk"
