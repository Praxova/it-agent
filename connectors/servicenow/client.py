"""Low-level ServiceNow REST API client with retry logic."""

import logging
from typing import Any

import httpx
from tenacity import (
    retry,
    retry_if_exception,
    retry_if_exception_type,
    stop_after_attempt,
    wait_exponential,
    before_sleep_log,
)

logger = logging.getLogger(__name__)


def _should_retry_http_error(exception: BaseException) -> bool:
    """Check if HTTP status code should trigger a retry.

    Args:
        exception: Exception to check.

    Returns:
        True if the status code indicates a retry should be attempted.
    """
    if isinstance(exception, httpx.HTTPStatusError):
        return exception.response.status_code in {429, 500, 502, 503, 504}
    return False


def _should_retry(exception: BaseException) -> bool:
    """Check if exception should trigger a retry.

    Args:
        exception: Exception to check.

    Returns:
        True if the exception should trigger a retry.
    """
    if isinstance(exception, (httpx.ConnectError, httpx.TimeoutException)):
        return True
    return _should_retry_http_error(exception)


class ServiceNowClient:
    """Low-level REST client for ServiceNow API.

    Handles authentication, retry logic, and basic error handling.
    Uses exponential backoff for transient failures.

    Attributes:
        instance: ServiceNow instance name (e.g., 'dev12345').
        username: Basic auth username.
        password: Basic auth password.
        base_url: Full base URL for the Table API.
    """

    def __init__(self, instance: str, username: str, password: str) -> None:
        """Initialize ServiceNow client.

        Args:
            instance: ServiceNow instance name (e.g., 'dev12345.service-now.com').
            username: Basic auth username.
            password: Basic auth password.
        """
        self.instance = instance
        self.username = username
        self.password = password
        # Remove https:// if present in instance name
        clean_instance = instance.replace("https://", "").replace("http://", "")
        self.base_url = f"https://{clean_instance}/api/now/table"

        # Create httpx client with auth
        self._client = httpx.AsyncClient(
            auth=(username, password),
            headers={
                "Accept": "application/json",
                "Content-Type": "application/json",
            },
            timeout=30.0,
        )

    async def __aenter__(self) -> "ServiceNowClient":
        """Context manager entry."""
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Context manager exit."""
        await self.close()

    async def close(self) -> None:
        """Close the HTTP client."""
        await self._client.aclose()

    @retry(
        retry=retry_if_exception(_should_retry),
        stop=stop_after_attempt(3),
        wait=wait_exponential(multiplier=1, min=2, max=30),
        before_sleep=before_sleep_log(logger, logging.WARNING),
        reraise=True,
    )
    async def _request(
        self, method: str, endpoint: str, **kwargs: Any
    ) -> dict[str, Any] | list[dict[str, Any]]:
        """Make HTTP request with retry logic.

        Args:
            method: HTTP method (GET, POST, PATCH, etc.).
            endpoint: API endpoint (e.g., 'incident', 'incident/sys_id').
            **kwargs: Additional arguments passed to httpx request.

        Returns:
            JSON response from ServiceNow.

        Raises:
            httpx.HTTPStatusError: If request fails after retries.
            httpx.ConnectError: If connection fails after retries.
            httpx.TimeoutException: If request times out after retries.
        """
        url = f"{self.base_url}/{endpoint}"
        logger.debug(f"ServiceNow API request: {method} {url}")

        response = await self._client.request(method, url, **kwargs)
        response.raise_for_status()

        data = response.json()
        logger.debug(f"ServiceNow API response: {response.status_code}")

        return data

    async def get_incidents(
        self, query: str | None = None, limit: int = 100
    ) -> list[dict[str, Any]]:
        """Fetch incidents from ServiceNow.

        Args:
            query: ServiceNow query string (e.g., 'state=1^assignment_group=Helpdesk').
            limit: Maximum number of incidents to return.

        Returns:
            List of incident dictionaries.
        """
        params: dict[str, Any] = {
            "sysparm_limit": limit,
            "sysparm_display_value": "true",  # Include display values
        }

        if query:
            params["sysparm_query"] = query

        data = await self._request("GET", "incident", params=params)

        # ServiceNow wraps results in a 'result' key
        if isinstance(data, dict) and "result" in data:
            return data["result"]  # type: ignore

        return []

    async def get_incident(self, sys_id: str) -> dict[str, Any]:
        """Fetch a single incident by sys_id.

        Args:
            sys_id: ServiceNow sys_id of the incident.

        Returns:
            Incident dictionary.

        Raises:
            httpx.HTTPStatusError: If incident not found or request fails.
        """
        params = {"sysparm_display_value": "true"}
        data = await self._request("GET", f"incident/{sys_id}", params=params)

        # ServiceNow wraps single results in a 'result' key
        if isinstance(data, dict) and "result" in data:
            return data["result"]  # type: ignore

        return data  # type: ignore

    async def update_incident(
        self, sys_id: str, data: dict[str, Any]
    ) -> dict[str, Any]:
        """Update an incident.

        Args:
            sys_id: ServiceNow sys_id of the incident.
            data: Fields to update (e.g., {'state': '6', 'work_notes': 'Updated'}).

        Returns:
            Updated incident dictionary.

        Raises:
            httpx.HTTPStatusError: If update fails.
        """
        params = {"sysparm_display_value": "true"}
        result = await self._request(
            "PATCH", f"incident/{sys_id}", params=params, json=data
        )

        # ServiceNow wraps results in a 'result' key
        if isinstance(result, dict) and "result" in result:
            return result["result"]  # type: ignore

        return result  # type: ignore

    async def health_check(self) -> bool:
        """Check if ServiceNow instance is reachable.

        Returns:
            True if instance is healthy, False otherwise.
        """
        try:
            # Try to fetch a single incident to verify connectivity
            await self._request("GET", "incident", params={"sysparm_limit": 1})
            return True
        except Exception as e:
            logger.error(f"ServiceNow health check failed: {e}")
            return False
