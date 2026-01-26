"""Tests for ServiceNow client."""

from typing import Any
from unittest.mock import AsyncMock, patch, MagicMock

import httpx
import pytest

from connectors.servicenow.client import ServiceNowClient


@pytest.mark.asyncio
class TestServiceNowClient:
    """Test cases for ServiceNowClient."""

    @pytest.fixture
    def client(self, servicenow_config: dict[str, str]) -> ServiceNowClient:
        """Create ServiceNowClient instance."""
        return ServiceNowClient(
            instance=servicenow_config["instance"],
            username=servicenow_config["username"],
            password=servicenow_config["password"],
        )

    async def test_client_initialization(
        self, client: ServiceNowClient, servicenow_config: dict[str, str]
    ) -> None:
        """Test client is initialized with correct configuration."""
        assert client.instance == servicenow_config["instance"]
        assert client.username == servicenow_config["username"]
        assert client.password == servicenow_config["password"]
        assert (
            client.base_url
            == f"https://{servicenow_config['instance']}/api/now/table"
        )

    async def test_client_strips_https_from_instance(self) -> None:
        """Test that https:// is stripped from instance name."""
        client = ServiceNowClient(
            instance="https://dev341394.service-now.com",
            username="test",
            password="test",
        )
        assert client.base_url == "https://dev341394.service-now.com/api/now/table"

    async def test_get_incidents_success(
        self, client: ServiceNowClient, sample_incident_list: list[dict[str, Any]]
    ) -> None:
        """Test successful incident retrieval."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"result": sample_incident_list}
        mock_response.raise_for_status = MagicMock()

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.return_value = mock_response

            incidents = await client.get_incidents()

            assert len(incidents) == 3
            assert incidents[0]["number"] == "INC0010001"
            assert incidents[1]["number"] == "INC0010002"
            mock_request.assert_called_once()

    async def test_get_incidents_with_query(self, client: ServiceNowClient) -> None:
        """Test incident retrieval with query parameter."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"result": []}
        mock_response.raise_for_status = MagicMock()

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.return_value = mock_response

            query = "state=1^assignment_group=Helpdesk"
            await client.get_incidents(query=query, limit=50)

            # Verify the request was made with correct parameters
            call_args = mock_request.call_args
            assert call_args.kwargs["params"]["sysparm_query"] == query
            assert call_args.kwargs["params"]["sysparm_limit"] == 50

    async def test_get_incident_success(
        self, client: ServiceNowClient, sample_incident: dict[str, Any]
    ) -> None:
        """Test successful single incident retrieval."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"result": sample_incident}
        mock_response.raise_for_status = MagicMock()

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.return_value = mock_response

            incident = await client.get_incident("abc123def456")

            assert incident["sys_id"] == "abc123def456"
            assert incident["number"] == "INC0010001"

    async def test_update_incident_success(
        self, client: ServiceNowClient, incident_fixtures: dict[str, Any]
    ) -> None:
        """Test successful incident update."""
        updated = incident_fixtures["updated_incident"]
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"result": updated}
        mock_response.raise_for_status = MagicMock()

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.return_value = mock_response

            update_data = {"state": "6", "work_notes": "Resolved"}
            incident = await client.update_incident("abc123def456", update_data)

            assert incident["state"] == "6"
            call_args = mock_request.call_args
            assert call_args.kwargs["json"] == update_data

    async def test_retry_on_connection_error(self, client: ServiceNowClient) -> None:
        """Test retry logic on connection error."""
        call_count = 0

        async def mock_request_side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                raise httpx.ConnectError("Connection failed")
            # Third call succeeds
            mock_success = MagicMock()
            mock_success.status_code = 200
            mock_success.json.return_value = {"result": []}
            mock_success.raise_for_status = MagicMock()
            return mock_success

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.side_effect = mock_request_side_effect

            result = await client.get_incidents()

            # Should retry and eventually succeed
            assert result == []
            assert call_count == 3

    async def test_retry_on_timeout(self, client: ServiceNowClient) -> None:
        """Test retry logic on timeout."""
        call_count = 0

        async def mock_request_side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                raise httpx.TimeoutException("Request timeout")
            # Second call succeeds
            mock_success = MagicMock()
            mock_success.status_code = 200
            mock_success.json.return_value = {"result": []}
            mock_success.raise_for_status = MagicMock()
            return mock_success

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.side_effect = mock_request_side_effect

            result = await client.get_incidents()

            assert result == []
            assert call_count == 2

    async def test_retry_on_503_status(self, client: ServiceNowClient) -> None:
        """Test retry logic on 503 Service Unavailable."""
        call_count = 0

        async def mock_request_side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                # First call returns 503
                mock_503 = MagicMock()
                mock_503.status_code = 503
                mock_503.raise_for_status.side_effect = httpx.HTTPStatusError(
                    "Service Unavailable",
                    request=MagicMock(),
                    response=mock_503,
                )
                return mock_503
            # Second call succeeds
            mock_success = MagicMock()
            mock_success.status_code = 200
            mock_success.json.return_value = {"result": []}
            mock_success.raise_for_status = MagicMock()
            return mock_success

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.side_effect = mock_request_side_effect

            result = await client.get_incidents()

            assert result == []
            assert call_count == 2

    async def test_no_retry_on_404(self, client: ServiceNowClient) -> None:
        """Test that 404 errors are not retried."""
        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_404 = MagicMock()
            mock_404.status_code = 404
            mock_404.raise_for_status.side_effect = httpx.HTTPStatusError(
                "Not Found",
                request=MagicMock(),
                response=mock_404,
            )

            mock_request.return_value = mock_404

            with pytest.raises(httpx.HTTPStatusError):
                await client.get_incident("nonexistent")

            # Should only be called once (no retry)
            assert mock_request.call_count == 1

    async def test_max_retries_exceeded(self, client: ServiceNowClient) -> None:
        """Test that max retries (3) is respected."""
        call_count = 0

        async def mock_request_side_effect(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            raise httpx.ConnectError("Connection failed")

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.side_effect = mock_request_side_effect

            with pytest.raises(httpx.ConnectError):
                await client.get_incidents()

            # Should try 3 times total
            assert call_count == 3

    async def test_health_check_success(self, client: ServiceNowClient) -> None:
        """Test successful health check."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"result": []}
        mock_response.raise_for_status = MagicMock()

        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.return_value = mock_response

            result = await client.health_check()

            assert result is True

    async def test_health_check_failure(self, client: ServiceNowClient) -> None:
        """Test failed health check."""
        with patch.object(
            client._client, "request", new_callable=AsyncMock
        ) as mock_request:
            mock_request.side_effect = httpx.ConnectError("Connection failed")

            result = await client.health_check()

            assert result is False

    async def test_context_manager(self, servicenow_config: dict[str, str]) -> None:
        """Test client can be used as context manager."""
        async with ServiceNowClient(
            instance=servicenow_config["instance"],
            username=servicenow_config["username"],
            password=servicenow_config["password"],
        ) as client:
            assert client is not None

        # Client should be closed after context exit
        # (we can't easily test this without checking internal state)

    async def test_close_method(self, client: ServiceNowClient) -> None:
        """Test close method."""
        with patch.object(client._client, "aclose", new_callable=AsyncMock) as mock_close:
            await client.close()
            mock_close.assert_called_once()
