"""Pytest fixtures for connector tests."""

import json
from pathlib import Path
from typing import Any

import pytest
from unittest.mock import AsyncMock

from connectors.servicenow.client import ServiceNowClient
from connectors.base import Ticket, TicketState


@pytest.fixture
def fixtures_dir() -> Path:
    """Return path to fixtures directory."""
    return Path(__file__).parent / "fixtures"


@pytest.fixture
def incident_fixtures(fixtures_dir: Path) -> dict[str, Any]:
    """Load incident fixtures from JSON file."""
    with open(fixtures_dir / "incidents.json") as f:
        return json.load(f)


@pytest.fixture
def sample_incident(incident_fixtures: dict[str, Any]) -> dict[str, Any]:
    """Return a single sample incident."""
    return incident_fixtures["single_incident"]


@pytest.fixture
def sample_incident_list(incident_fixtures: dict[str, Any]) -> list[dict[str, Any]]:
    """Return a list of sample incidents."""
    return incident_fixtures["incident_list"]


@pytest.fixture
def sample_ticket() -> Ticket:
    """Return a sample Ticket object."""
    from datetime import datetime

    return Ticket(
        id="abc123def456",
        number="INC0010001",
        short_description="Password reset request",
        description="User forgot password and cannot log in to workstation",
        state=TicketState.NEW,
        priority=3,
        caller_username="john.doe",
        assignment_group="Helpdesk",
        created_at=datetime(2024, 1, 15, 14, 30, 0),
        updated_at=datetime(2024, 1, 15, 14, 30, 0),
    )


@pytest.fixture
def mock_client() -> AsyncMock:
    """Return a mock ServiceNowClient."""
    client = AsyncMock(spec=ServiceNowClient)
    client.close = AsyncMock()
    return client


@pytest.fixture
def servicenow_config() -> dict[str, str]:
    """Return ServiceNow configuration for testing."""
    return {
        "instance": "dev341394.service-now.com",
        "username": "test_user",
        "password": "test_password",
        "assignment_group": "Helpdesk",
    }
