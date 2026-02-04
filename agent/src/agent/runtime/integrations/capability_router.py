"""Capability router for workflow runtime."""
from __future__ import annotations
import logging
import time
from dataclasses import dataclass
from typing import Any

import httpx

logger = logging.getLogger(__name__)


@dataclass
class ToolServerInfo:
    """Information about a Tool Server."""
    id: str
    name: str
    url: str
    status: str
    capabilities: list[str]


@dataclass
class CacheEntry:
    """Cache entry for capability routing."""
    servers: list[ToolServerInfo]
    timestamp: float


class NoCapableServerError(Exception):
    """No server found that can handle the capability."""
    pass


class CapabilityRouter:
    """
    Routes capability requests to appropriate Tool Servers.

    Queries Admin Portal for Tool Server URLs and caches results
    to reduce API calls.
    """

    def __init__(
        self,
        admin_portal_url: str,
        cache_ttl: int = 60,
    ):
        self._admin_url = admin_portal_url.rstrip("/")
        self._cache: dict[str, CacheEntry] = {}
        self._cache_ttl = cache_ttl

    async def get_server_for_capability(
        self,
        capability: str,
        prefer_healthy: bool = True,
    ) -> ToolServerInfo:
        """
        Get a Tool Server that provides the specified capability.

        Args:
            capability: Capability name (e.g., "ad-password-reset")
            prefer_healthy: Only return healthy servers

        Returns:
            ToolServerInfo with URL and details

        Raises:
            NoCapableServerError: If no server provides this capability
        """
        # Check cache
        if self._is_cached(capability):
            servers = self._cache[capability].servers
            if servers:
                return servers[0]  # Return first (highest priority)

        # Query Admin Portal
        servers = await self._query_servers(capability, prefer_healthy)

        if not servers:
            raise NoCapableServerError(f"No server provides capability: {capability}")

        # Cache result
        self._cache[capability] = CacheEntry(
            servers=servers,
            timestamp=time.time(),
        )

        return servers[0]

    async def get_all_servers_for_capability(
        self,
        capability: str,
    ) -> list[ToolServerInfo]:
        """Get all servers that provide a capability."""
        if self._is_cached(capability):
            return self._cache[capability].servers

        servers = await self._query_servers(capability, prefer_healthy=False)

        self._cache[capability] = CacheEntry(
            servers=servers,
            timestamp=time.time(),
        )

        return servers

    def _is_cached(self, capability: str) -> bool:
        """Check if capability is cached and not expired."""
        if capability not in self._cache:
            return False

        entry = self._cache[capability]
        age = time.time() - entry.timestamp
        return age < self._cache_ttl

    async def _query_servers(
        self,
        capability: str,
        prefer_healthy: bool,
    ) -> list[ToolServerInfo]:
        """Query Admin Portal for servers."""
        url = f"{self._admin_url}/api/capabilities/{capability}/servers"
        params = {}
        if prefer_healthy:
            params["status"] = "online"

        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    params=params,
                    timeout=10.0,
                )

                if response.status_code == 404:
                    return []

                response.raise_for_status()
                data = response.json()
                server_list = data.get("servers", []) if isinstance(data, dict) else data

                servers = []
                for item in server_list:
                    servers.append(ToolServerInfo(
                        id=item.get("id", ""),
                        name=item.get("name", ""),
                        url=item.get("url", ""),
                        status=item.get("status", "unknown"),
                        capabilities=item.get("capabilities", []),
                    ))

                logger.debug(f"Found {len(servers)} servers for capability '{capability}'")
                return servers

        except Exception as e:
            logger.error(f"Failed to query capability routing: {e}")
            return []

    def clear_cache(self):
        """Clear the routing cache."""
        self._cache.clear()
