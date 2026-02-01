"""Capability router for dynamic Tool Server discovery."""

import logging
from datetime import datetime, timedelta
from typing import Any

import httpx

from .models import CapabilityServersResponse, ToolServerInfo

logger = logging.getLogger(__name__)


class CapabilityRouter:
    """Routes capability requests to available Tool Servers.

    Queries the Admin Portal to discover which Tool Servers provide a given capability,
    and returns the best available server based on health and recency.

    Attributes:
        admin_portal_url: Base URL of the Admin Portal API.
        cache_ttl: How long to cache capability routing results (seconds).
        timeout: Request timeout in seconds.
    """

    def __init__(
        self,
        admin_portal_url: str,
        cache_ttl: int = 300,  # 5 minutes
        timeout: float = 10.0,
    ) -> None:
        """Initialize capability router.

        Args:
            admin_portal_url: Base URL of Admin Portal API (e.g., "http://localhost:5000").
            cache_ttl: Cache time-to-live in seconds (default: 300).
            timeout: Request timeout in seconds (default: 10.0).
        """
        self.admin_portal_url = admin_portal_url.rstrip("/")
        self.cache_ttl = cache_ttl
        self.timeout = timeout

        # Simple in-memory cache: {capability: (timestamp, response)}
        self._cache: dict[str, tuple[datetime, CapabilityServersResponse]] = {}

    async def get_server_for_capability(
        self,
        capability: str,
        domain: str | None = None,
    ) -> ToolServerInfo | None:
        """Get the best available Tool Server for a capability.

        Returns the first healthy server (ordered by most recent heartbeat).
        If domain is specified, prefers servers in that domain.

        Args:
            capability: Capability name (e.g., "ad-password-reset").
            domain: (optional) Preferred domain/region.

        Returns:
            ToolServerInfo for the best available server, or None if no servers available.

        Raises:
            Exception: If API request fails.
        """
        logger.info(f"Looking up Tool Server for capability: {capability}")

        # Get all servers for this capability
        response = await self._query_capability(capability)

        if not response.servers:
            logger.warning(f"No Tool Servers found for capability: {capability}")
            return None

        # Filter by domain if specified
        servers = response.servers
        if domain:
            domain_servers = [s for s in servers if s.domain.lower() == domain.lower()]
            if domain_servers:
                logger.debug(f"Found {len(domain_servers)} servers in domain '{domain}'")
                servers = domain_servers
            else:
                logger.debug(f"No servers found in domain '{domain}', using all servers")

        # Servers are already ordered by Admin Portal (most recent heartbeat first)
        # Return the first one
        best_server = servers[0]

        logger.info(
            f"Selected Tool Server for {capability}: {best_server.name} "
            f"({best_server.url}, status={best_server.status})"
        )

        return best_server

    async def get_all_servers_for_capability(
        self,
        capability: str,
        status: str | None = None,
    ) -> list[ToolServerInfo]:
        """Get all Tool Servers for a capability.

        Args:
            capability: Capability name (e.g., "ad-password-reset").
            status: (optional) Filter by status (healthy, all, degraded, etc.).

        Returns:
            List of ToolServerInfo, ordered by most recent heartbeat.

        Raises:
            Exception: If API request fails.
        """
        logger.info(f"Looking up all Tool Servers for capability: {capability}")

        response = await self._query_capability(capability, status=status)

        logger.info(
            f"Found {response.total_count} Tool Server(s) for capability: {capability}"
        )

        return response.servers

    async def _query_capability(
        self,
        capability: str,
        status: str | None = None,
    ) -> CapabilityServersResponse:
        """Query Admin Portal for servers providing a capability.

        Results are cached for performance.

        Args:
            capability: Capability name.
            status: (optional) Status filter (healthy, all, degraded, etc.).

        Returns:
            CapabilityServersResponse with list of servers.

        Raises:
            ValueError: If capability not found.
            Exception: If API request fails.
        """
        # Check cache (only for default healthy status)
        cache_key = f"{capability}:{status or 'healthy'}"
        if cache_key in self._cache:
            cached_time, cached_response = self._cache[cache_key]
            age = (datetime.now() - cached_time).total_seconds()

            if age < self.cache_ttl:
                logger.debug(
                    f"Using cached result for {capability} (age: {age:.1f}s, ttl: {self.cache_ttl}s)"
                )
                return cached_response
            else:
                logger.debug(f"Cache expired for {capability} (age: {age:.1f}s)")

        # Query Admin Portal
        url = f"{self.admin_portal_url}/api/capabilities/{capability}/servers"
        params = {}
        if status:
            params["status"] = status

        logger.debug(f"Querying Admin Portal: {url}")

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            try:
                response = await client.get(url, params=params)
                response.raise_for_status()

                data = response.json()
                result = CapabilityServersResponse(**data)

                # Cache result
                self._cache[cache_key] = (datetime.now(), result)

                return result

            except httpx.HTTPStatusError as e:
                if e.response.status_code == 404:
                    raise ValueError(
                        f"Capability '{capability}' not found. "
                        f"Ensure the capability is registered in the Admin Portal."
                    )
                else:
                    raise Exception(f"HTTP {e.response.status_code}: {e.response.text}")

            except httpx.RequestError as e:
                raise Exception(f"Failed to connect to Admin Portal: {str(e)}")

    def clear_cache(self, capability: str | None = None) -> None:
        """Clear routing cache.

        Args:
            capability: (optional) Clear cache for specific capability. If None, clears all.
        """
        if capability:
            # Clear all cache entries for this capability (all status filters)
            keys_to_remove = [k for k in self._cache.keys() if k.startswith(f"{capability}:")]
            for key in keys_to_remove:
                del self._cache[key]
            logger.debug(f"Cleared cache for capability: {capability}")
        else:
            self._cache.clear()
            logger.debug("Cleared all routing cache")
