"""Admin Portal API client for agent configuration."""

import os
import logging
from dataclasses import dataclass, field
from typing import Optional, Dict, Any
import httpx

logger = logging.getLogger(__name__)


class OperationTokenError(Exception):
    """Raised when the portal denies an operation token request."""

    def __init__(self, error_code: str, message: str):
        self.error_code = error_code
        self.message = message
        super().__init__(f"[{error_code}] {message}")


@dataclass
class LlmProviderConfig:
    """LLM provider configuration from Admin Portal."""
    service_account_id: str
    service_account_name: str
    provider_type: str
    account_type: str
    config: Dict[str, Any] = field(default_factory=dict)
    credentials: Dict[str, str] = field(default_factory=dict)

    @property
    def model(self) -> Optional[str]:
        """Get the model name from configuration."""
        return self.config.get("model") or self.config.get("Model")

    @property
    def base_url(self) -> Optional[str]:
        """Get the base URL from configuration."""
        return (self.config.get("base_url") or
                self.config.get("BaseUrl") or
                self.config.get("endpoint") or
                self.config.get("Endpoint"))

    @property
    def api_key(self) -> Optional[str]:
        """Get API key from credentials."""
        return self.credentials.get("api_key") or self.credentials.get("ApiKey")

    @property
    def temperature(self) -> float:
        """Get temperature setting."""
        temp = self.config.get("temperature") or self.config.get("Temperature")
        if temp is not None:
            return float(temp)
        return 0.1


@dataclass
class ServiceNowConfig:
    """ServiceNow configuration from Admin Portal."""
    service_account_id: str
    service_account_name: str
    provider_type: str
    account_type: str
    config: Dict[str, Any] = field(default_factory=dict)
    credential_storage: str = "none"
    credential_reference: Optional[str] = None
    credentials: Dict[str, str] = field(default_factory=dict)

    @property
    def instance_url(self) -> Optional[str]:
        """Get ServiceNow instance URL."""
        return (self.config.get("instanceUrl") or
                self.config.get("InstanceUrl") or
                self.config.get("instance_url"))

    @property
    def username(self) -> Optional[str]:
        """Get username from configuration or credentials."""
        # Try config first (for display), then credentials (actual value)
        return (self.config.get("username") or
                self.config.get("Username") or
                self.credentials.get("username"))

    @property
    def password(self) -> Optional[str]:
        """Get password from credentials."""
        return self.credentials.get("password") or self.credentials.get("Password")


@dataclass
class AgentInfo:
    """Agent metadata from Admin Portal."""
    id: str
    name: str
    display_name: Optional[str] = None
    description: Optional[str] = None
    is_enabled: bool = True


@dataclass
class AgentConfiguration:
    """Complete agent configuration from Admin Portal."""
    agent: AgentInfo
    llm_provider: LlmProviderConfig
    servicenow: ServiceNowConfig
    assignment_group: Optional[str] = None

    @classmethod
    def from_api_response(cls, data: Dict[str, Any]) -> "AgentConfiguration":
        """Create from API response JSON.

        Args:
            data: JSON response from /api/agents/{name}/configuration

        Returns:
            AgentConfiguration instance.

        Raises:
            ValueError: If required fields are missing.
        """
        agent_data = data.get("agent", {})
        if not agent_data:
            raise ValueError("Missing 'agent' in response")

        llm_data = data.get("llmProvider", {})
        if not llm_data:
            raise ValueError("Missing 'llmProvider' in response")

        snow_data = data.get("serviceNow", {})
        if not snow_data:
            raise ValueError("Missing 'serviceNow' in response")

        agent = AgentInfo(
            id=agent_data.get("id", ""),
            name=agent_data.get("name", ""),
            display_name=agent_data.get("displayName"),
            description=agent_data.get("description"),
            is_enabled=agent_data.get("isEnabled", True)
        )

        llm_provider = LlmProviderConfig(
            service_account_id=llm_data.get("serviceAccountId", ""),
            service_account_name=llm_data.get("serviceAccountName", ""),
            provider_type=llm_data.get("providerType", ""),
            account_type=llm_data.get("accountType", ""),
            config=llm_data.get("config", {}),
            credentials=llm_data.get("credentials", {})
        )

        servicenow = ServiceNowConfig(
            service_account_id=snow_data.get("serviceAccountId", ""),
            service_account_name=snow_data.get("serviceAccountName", ""),
            provider_type=snow_data.get("providerType", ""),
            account_type=snow_data.get("accountType", ""),
            config=snow_data.get("config", {}),
            credential_storage=snow_data.get("credentialStorage", "none"),
            credential_reference=snow_data.get("credentialReference"),
            credentials=snow_data.get("credentials", {})
        )

        return cls(
            agent=agent,
            llm_provider=llm_provider,
            servicenow=servicenow,
            assignment_group=data.get("assignmentGroup")
        )


class AdminPortalClient:
    """Client for Admin Portal API.

    Handles fetching agent configuration and sending heartbeat updates.
    """

    def __init__(
        self,
        base_url: Optional[str] = None,
        agent_name: Optional[str] = None,
        api_key: Optional[str] = None,
        timeout: float = 30.0,
    ) -> None:
        """Initialize Admin Portal client.

        Args:
            base_url: Base URL of Admin Portal API. Defaults to LUCID_ADMIN_URL env var.
            agent_name: Name of the agent. Defaults to LUCID_AGENT_NAME env var.
            api_key: API key for authentication. Defaults to LUCID_API_KEY env var.
            timeout: Request timeout in seconds.

        Raises:
            ValueError: If required configuration is missing.
        """
        self.base_url = (base_url or os.environ.get("LUCID_ADMIN_URL", "")).rstrip("/")
        self.agent_name = agent_name or os.environ.get("LUCID_AGENT_NAME", "")
        self.api_key = api_key or os.environ.get("LUCID_API_KEY", "")
        self.timeout = timeout

        if not self.base_url:
            raise ValueError(
                "Admin Portal URL not configured. Set LUCID_ADMIN_URL environment variable."
            )

        if not self.agent_name:
            raise ValueError(
                "Agent name not configured. Set LUCID_AGENT_NAME environment variable."
            )

        if not self.api_key:
            logger.warning(
                "API key not configured. Set LUCID_API_KEY for authenticated requests."
            )

        # Build headers
        headers = {
            "Content-Type": "application/json",
            "User-Agent": "PraxovaAgent/1.0"
        }
        if self.api_key:
            headers["X-API-Key"] = self.api_key

        self._client = httpx.AsyncClient(
            base_url=self.base_url,
            timeout=self.timeout,
            headers=headers
        )

        logger.info(f"Admin Portal client initialized for {self.base_url}")

    async def get_configuration(self) -> AgentConfiguration:
        """Fetch agent configuration from Admin Portal.

        Returns:
            AgentConfiguration with LLM, ServiceNow, and agent settings.

        Raises:
            ValueError: If agent not found or configuration invalid.
            httpx.HTTPStatusError: If API request fails.
        """
        url = f"/api/agents/{self.agent_name}/configuration"

        logger.info(f"Fetching configuration from: {self.base_url}{url}")

        try:
            response = await self._client.get(url)
            response.raise_for_status()

            data = response.json()
            logger.debug(f"Received configuration: {data}")

            config = AgentConfiguration.from_api_response(data)

            logger.info(
                f"Configuration loaded for agent '{config.agent.name}' "
                f"(LLM: {config.llm_provider.provider_type}, "
                f"ServiceNow: {config.servicenow.provider_type})"
            )

            return config

        except httpx.HTTPStatusError as e:
            if e.response.status_code == 404:
                raise ValueError(f"Agent '{self.agent_name}' not found in Admin Portal")
            elif e.response.status_code == 400:
                raise ValueError(f"Agent '{self.agent_name}' is disabled")
            elif e.response.status_code == 422:
                error_data = e.response.json() if e.response.content else {}
                missing = error_data.get("missingConfiguration", [])
                raise ValueError(
                    f"Agent '{self.agent_name}' configuration incomplete: {', '.join(missing)}"
                )
            else:
                logger.error(f"HTTP {e.response.status_code}: {e.response.text}")
                raise

        except httpx.RequestError as e:
            logger.error(f"Failed to connect to Admin Portal: {e}")
            raise ValueError(f"Failed to connect to Admin Portal: {e}")

    async def send_heartbeat(
        self,
        host_name: Optional[str] = None,
        status: str = "Unknown",
        last_poll: Optional[str] = None,
        tickets_processed: Optional[int] = None,
    ) -> None:
        """Send agent heartbeat to Admin Portal.

        Args:
            host_name: Hostname where agent is running.
            status: Agent status (Running, Stopped, Error, etc.).
            last_poll: ISO format timestamp of last poll.
            tickets_processed: Number of tickets processed.
        """
        import socket

        url = f"/api/agents/{self.agent_name}/runtime/heartbeat"

        payload = {
            "hostName": host_name or socket.gethostname(),
            "status": status,
            "lastPoll": last_poll,
            "ticketsProcessed": tickets_processed
        }

        logger.debug(f"Sending heartbeat to: {self.base_url}{url}")

        try:
            response = await self._client.post(url, json=payload)
            response.raise_for_status()

            logger.debug(f"Heartbeat sent successfully for agent '{self.agent_name}'")

        except httpx.HTTPStatusError as e:
            logger.warning(
                f"Heartbeat failed: HTTP {e.response.status_code} - {e.response.text}"
            )
            # Don't raise - heartbeat failures shouldn't stop agent operation

        except httpx.RequestError as e:
            logger.warning(f"Heartbeat connection failed: {e}")
            # Don't raise - heartbeat failures shouldn't stop agent operation

    async def request_operation_token(
        self,
        capability: str,
        target: str,
        target_type: str,
        tool_server_url: str,
        ticket_number: str | None = None,
        workflow_execution_id: str | None = None,
        approval_id: str | None = None,
    ) -> str:
        """Request an operation authorization token from the portal.

        Args:
            capability: The capability name (e.g., "ad-password-reset").
            target: The target entity (username, group name, or path).
            target_type: Type of target: "user", "group", or "path".
            tool_server_url: The tool server URL this token will be used against.
            ticket_number: ServiceNow ticket number for audit trail.
            workflow_execution_id: Workflow execution ID if tracked.
            approval_id: Approval ID if an approval step was involved.

        Returns:
            JWT token string.

        Raises:
            OperationTokenError: If the portal denies the token or returns an error.
            httpx.RequestError: If the portal is unreachable.
        """
        url = "/api/authz/operation-token"

        payload = {
            "agent_name": self.agent_name,
            "capability": capability,
            "target": target,
            "target_type": target_type,
            "tool_server_url": tool_server_url,
            "workflow_context": {
                "ticket_number": ticket_number,
                "workflow_execution_id": workflow_execution_id,
                "approval_id": approval_id,
            }
        }

        logger.debug(f"Requesting operation token: capability={capability}, target={target}")

        try:
            response = await self._client.post(url, json=payload, timeout=10.0)

            if response.status_code == 200:
                data = response.json()
                token = data.get("token")
                if not token:
                    raise OperationTokenError("internal_error", "Portal returned success but no token")
                logger.debug(f"Operation token received for {capability}/{target}")
                return token

            elif response.status_code == 429:
                raise OperationTokenError("rate_limited", "Token request rate limit exceeded")

            else:
                try:
                    error_data = response.json()
                    error_code = error_data.get("error", "unknown")
                    error_msg = error_data.get("message", f"HTTP {response.status_code}")
                except Exception:
                    error_code = "http_error"
                    error_msg = f"HTTP {response.status_code}: {response.text[:200]}"

                raise OperationTokenError(error_code, error_msg)

        except httpx.TimeoutException:
            # Retry once
            logger.warning("Operation token request timed out, retrying...")
            try:
                response = await self._client.post(url, json=payload, timeout=5.0)
                if response.status_code == 200:
                    data = response.json()
                    token = data.get("token")
                    if not token:
                        raise OperationTokenError("internal_error", "Portal returned success but no token")
                    return token
                else:
                    raise OperationTokenError("portal_unavailable",
                        "Authorization service unavailable after retry")
            except httpx.RequestError as e:
                raise OperationTokenError("portal_unavailable",
                    f"Authorization service unreachable: {e}")

    async def close(self) -> None:
        """Close the HTTP client."""
        await self._client.aclose()

    async def __aenter__(self) -> "AdminPortalClient":
        """Async context manager entry."""
        return self

    async def __aexit__(self, *args) -> None:
        """Async context manager exit."""
        await self.close()
