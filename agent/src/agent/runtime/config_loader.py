"""Loads agent configuration from Admin Portal export API.

Agent authentication:
1. Generate an API key for this agent in the Admin Portal (API Keys page)
2. Set LUCID_API_KEY=<key> in the agent's environment (.env file or docker-compose)
3. The agent uses this key for all portal API calls (credentials, heartbeats, approvals)
"""
from __future__ import annotations
import os
import httpx
from typing import Any
import logging

from .models import AgentExport, CredentialReference

logger = logging.getLogger(__name__)


class ConfigurationError(Exception):
    """Raised when configuration loading fails."""
    pass


class ConfigLoader:
    """Loads and resolves agent configuration from Admin Portal."""

    def __init__(self, admin_portal_url: str, agent_name: str, api_key: str = ""):
        """
        Initialize config loader.

        Args:
            admin_portal_url: Base URL of Admin Portal (e.g., http://localhost:5000)
            agent_name: Name of the agent to load
            api_key: API key for portal authentication (from LUCID_API_KEY env var)
        """
        self.admin_portal_url = admin_portal_url.rstrip('/')
        self.agent_name = agent_name
        self.api_key = api_key
        self._export: AgentExport | None = None
        self._resolved_credentials: dict[str, dict[str, str]] = {}

    def _auth_headers(self) -> dict[str, str]:
        """Return auth headers if API key is configured."""
        if self.api_key:
            return {"X-API-Key": self.api_key}
        return {}

    async def load(self) -> AgentExport:
        """
        Fetch agent export from Admin Portal.

        Returns:
            Parsed AgentExport model

        Raises:
            ConfigurationError: If fetch fails or response is invalid
        """
        url = f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/export"
        logger.info(f"Loading agent configuration from {url}")

        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(
                    url, headers=self._auth_headers(), timeout=30.0
                )
                response.raise_for_status()
            except httpx.HTTPStatusError as e:
                raise ConfigurationError(
                    f"Failed to fetch agent config: HTTP {e.response.status_code}"
                ) from e
            except httpx.RequestError as e:
                raise ConfigurationError(
                    f"Failed to connect to Admin Portal: {e}"
                ) from e

        try:
            data = response.json()
            self._export = AgentExport.model_validate(data)
            logger.info(f"Loaded agent '{self._export.agent.name}' with workflow "
                       f"'{self._export.workflow.name if self._export.workflow else 'none'}'")
            return self._export
        except Exception as e:
            raise ConfigurationError(f"Failed to parse agent export: {e}") from e

    async def _fetch_credentials_from_portal(
        self, service_account_id: str
    ) -> dict[str, str]:
        """
        Fetch decrypted credentials from the portal's credential API.

        Args:
            service_account_id: GUID of the service account

        Returns:
            Dict of credential key-value pairs (e.g., username, password)

        Raises:
            ConfigurationError: If the API call fails
        """
        url = (
            f"{self.admin_portal_url}/api/v1/service-accounts"
            f"/{service_account_id}/credentials"
        )
        logger.debug(f"Fetching credentials for service account {service_account_id}")

        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(
                    url, headers=self._auth_headers(), timeout=15.0
                )
                response.raise_for_status()
            except httpx.HTTPStatusError as e:
                raise ConfigurationError(
                    f"Failed to fetch credentials: HTTP {e.response.status_code}"
                ) from e
            except httpx.RequestError as e:
                raise ConfigurationError(
                    f"Failed to connect to Admin Portal for credentials: {e}"
                ) from e

        data = response.json()
        creds = data.get("credentials", {})
        logger.info(
            f"Fetched credentials from portal for service account "
            f"{data.get('serviceAccountName', service_account_id)}"
        )
        return creds

    async def resolve_credentials(
        self,
        ref: CredentialReference,
        service_account_id: str | None = None,
    ) -> dict[str, str]:
        """
        Resolve credential reference to actual values.

        For 'database' storage: attempts to fetch from portal API first
        (requires API key and service_account_id), falls back to env vars.

        Args:
            ref: Credential reference with storage type and key names
            service_account_id: Service account GUID (for portal API fetch)

        Returns:
            Dict with resolved credential values (username, password, api_key, etc.)
        """
        if ref.storage == "none":
            return {}

        if ref.storage == "environment":
            result = {}

            if ref.username_key:
                value = os.environ.get(ref.username_key)
                if not value:
                    raise ConfigurationError(
                        f"Environment variable '{ref.username_key}' not set"
                    )
                result["username"] = value

            if ref.password_key:
                value = os.environ.get(ref.password_key)
                if not value:
                    raise ConfigurationError(
                        f"Environment variable '{ref.password_key}' not set"
                    )
                result["password"] = value

            if ref.api_key_key:
                value = os.environ.get(ref.api_key_key)
                if not value:
                    raise ConfigurationError(
                        f"Environment variable '{ref.api_key_key}' not set"
                    )
                result["api_key"] = value

            return result

        if ref.storage == "database":
            # Try portal API first if we have auth + service account ID
            if self.api_key and service_account_id:
                try:
                    result = await self._fetch_credentials_from_portal(
                        service_account_id
                    )
                    if result:
                        logger.info("Credentials resolved via portal API")
                        return result
                except Exception as e:
                    logger.warning(
                        f"Portal credential fetch failed, falling back to env vars: {e}"
                    )
            elif not self.api_key:
                logger.debug(
                    "No API key configured — using env var fallback for credentials"
                )
            elif not service_account_id:
                logger.debug(
                    "No service account ID in export — using env var fallback"
                )

            # Fall back to environment variables
            result = {}
            if ref.username_key:
                result["username"] = os.environ.get(ref.username_key, "")
            if ref.password_key:
                result["password"] = os.environ.get(ref.password_key, "")

            # Fall back to conventional env var names
            if not result.get("username"):
                result["username"] = os.environ.get("SERVICENOW_USERNAME",
                                     os.environ.get("SNOW_USERNAME", ""))
            if not result.get("password"):
                result["password"] = os.environ.get("SERVICENOW_PASSWORD",
                                     os.environ.get("SNOW_PASSWORD", ""))

            if not result.get("password"):
                logger.warning("Database credential storage used but no password found. "
                               "Set LUCID_API_KEY for portal fetch, or "
                               "set SERVICENOW_PASSWORD env var as fallback.")
            logger.info("Credentials resolved via environment variable fallback")
            return result

        raise ConfigurationError(
            f"Unsupported credential storage: {ref.storage}. "
            f"Supported types: 'environment', 'database', 'none'."
        )

    async def get_llm_credentials(self) -> dict[str, str]:
        """Get resolved LLM provider credentials."""
        if not self._export or not self._export.llm_provider:
            raise ConfigurationError("No LLM provider configured")

        if not self._export.llm_provider.credentials:
            return {}  # Some providers (Ollama) don't need credentials

        return await self.resolve_credentials(
            self._export.llm_provider.credentials,
            service_account_id=self._export.llm_provider.service_account_id,
        )

    async def get_servicenow_credentials(self) -> dict[str, str]:
        """Get resolved ServiceNow credentials."""
        if not self._export or not self._export.service_now:
            raise ConfigurationError("No ServiceNow provider configured")

        if not self._export.service_now.credentials:
            raise ConfigurationError("ServiceNow credentials not configured")

        return await self.resolve_credentials(
            self._export.service_now.credentials,
            service_account_id=self._export.service_now.service_account_id,
        )

    @property
    def export(self) -> AgentExport:
        """Get loaded export (must call load() first)."""
        if not self._export:
            raise ConfigurationError("Configuration not loaded. Call load() first.")
        return self._export
