"""Loads agent configuration from Admin Portal export API."""
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

    def __init__(self, admin_portal_url: str, agent_name: str):
        """
        Initialize config loader.

        Args:
            admin_portal_url: Base URL of Admin Portal (e.g., http://localhost:5000)
            agent_name: Name of the agent to load
        """
        self.admin_portal_url = admin_portal_url.rstrip('/')
        self.agent_name = agent_name
        self._export: AgentExport | None = None
        self._resolved_credentials: dict[str, dict[str, str]] = {}

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
                response = await client.get(url, timeout=30.0)
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

    def resolve_credentials(self, ref: CredentialReference) -> dict[str, str]:
        """
        Resolve credential reference to actual values.

        Currently supports 'environment' storage - reads from env vars.

        Args:
            ref: Credential reference with storage type and key names

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
            # Database storage: credentials are encrypted in Admin Portal DB.
            # Fall back to environment variables for the agent runtime.
            result = {}
            # Try specific env var keys if provided
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
                               "Set SERVICENOW_PASSWORD or SNOW_PASSWORD env var, "
                               "or add credentials to .env file.")
            logger.debug(f"Resolved database credentials: username={result.get('username')!r}, "
                         f"password={'****' if result.get('password') else '(empty)'}")
            return result

        raise ConfigurationError(
            f"Unsupported credential storage: {ref.storage}. "
            f"Supported types: 'environment', 'database', 'none'."
        )

    def get_llm_credentials(self) -> dict[str, str]:
        """Get resolved LLM provider credentials."""
        if not self._export or not self._export.llm_provider:
            raise ConfigurationError("No LLM provider configured")

        if not self._export.llm_provider.credentials:
            return {}  # Some providers (Ollama) don't need credentials

        return self.resolve_credentials(self._export.llm_provider.credentials)

    def get_servicenow_credentials(self) -> dict[str, str]:
        """Get resolved ServiceNow credentials."""
        if not self._export or not self._export.service_now:
            raise ConfigurationError("No ServiceNow provider configured")

        if not self._export.service_now.credentials:
            raise ConfigurationError("ServiceNow credentials not configured")

        return self.resolve_credentials(self._export.service_now.credentials)

    @property
    def export(self) -> AgentExport:
        """Get loaded export (must call load() first)."""
        if not self._export:
            raise ConfigurationError("Configuration not loaded. Call load() first.")
        return self._export
