"""Base class for agent tools that interact with Tool Server."""

import logging
from typing import Any

from attrs import Factory, define, field

import httpx
from griptape.artifacts import ErrorArtifact, TextArtifact
from griptape.tools import BaseTool
from pydantic import BaseModel, Field as PydanticField

logger = logging.getLogger(__name__)

# Forward declaration for type hints (avoid circular import)
try:
    from agent.routing import CapabilityRouter
except ImportError:
    CapabilityRouter = None  # type: ignore

try:
    from agent.config.admin_client import AdminPortalClient, OperationTokenError
except ImportError:
    AdminPortalClient = None  # type: ignore
    OperationTokenError = Exception  # type: ignore


class ToolServerConfig(BaseModel):
    """Configuration for Tool Server connection.

    Attributes:
        base_url: Base URL of the Tool Server API.
        timeout: Request timeout in seconds.
        verify_ssl: Whether to verify SSL certificates.
        client_cert_path: Path to agent mTLS client certificate PEM.
        client_key_path: Path to agent mTLS client private key PEM.
    """

    base_url: str = PydanticField(
        default="http://localhost:8000/api/v1",
        description="Base URL of Tool Server API",
    )
    timeout: float = PydanticField(default=30.0, description="Request timeout in seconds")
    verify_ssl: bool = PydanticField(
        default=True, description="Whether to verify SSL certificates"
    )
    client_cert_path: str | None = PydanticField(
        default=None,
        description="Path to agent mTLS client certificate PEM (set by entrypoint from portal)",
    )
    client_key_path: str | None = PydanticField(
        default=None,
        description="Path to agent mTLS client private key PEM (set by entrypoint from portal)",
    )


@define
class BaseToolServerTool(BaseTool):
    """Base class for tools that interact with the Tool Server.

    Supports two modes:
    1. Admin Portal mode: Uses CapabilityRouter to discover Tool Servers dynamically (ADR-007)
    2. Legacy mode: Uses fixed tool_server_url from configuration

    Attributes:
        tool_server_config: Tool Server configuration (legacy mode).
        capability_router: Capability router for dynamic Tool Server discovery (Admin Portal mode).
        capability_name: Name of the capability this tool requires (e.g., "ad-password-reset").
    """

    tool_server_config: ToolServerConfig = field(
        default=Factory(lambda: ToolServerConfig()),
        kw_only=True,
    )

    capability_router: Any = field(default=None, kw_only=True)  # CapabilityRouter | None
    capability_name: str | None = field(default=None, kw_only=True)

    # Admin Portal client for operation token requests.
    # If None, token request is skipped (legacy/testing mode).
    admin_client: Any = field(default=None, kw_only=True)  # AdminPortalClient | None

    async def _make_request(
        self,
        method: str,
        endpoint: str,
        data: dict[str, Any] | None = None,
        target: str | None = None,
        target_type: str = "user",
        ticket_number: str | None = None,
    ) -> dict[str, Any]:
        """Make HTTP request to Tool Server.

        If admin_client is set, requests an operation token from the portal
        before calling the tool server. The token is included in the
        Authorization header.

        Args:
            method: HTTP method (GET, POST, etc.).
            endpoint: API endpoint (relative to base_url).
            data: Optional request body data.
            target: The target entity for the operation token (username, group, path).
            target_type: Type of target: "user", "group", or "path".
            ticket_number: ServiceNow ticket number for audit trail.

        Returns:
            Response JSON as dictionary.

        Raises:
            OperationTokenError: If the portal denies the token.
            Exception: If request fails or no Tool Server available.
        """
        # Resolve base URL
        if self.capability_router and self.capability_name:
            # Admin Portal mode: resolve capability to Tool Server
            logger.debug(f"Resolving capability: {self.capability_name}")

            server_info = await self.capability_router.get_server_for_capability(
                self.capability_name
            )

            if not server_info:
                raise Exception(
                    f"No Tool Server available for capability: {self.capability_name}. "
                    f"Ensure at least one Tool Server is registered and healthy."
                )

            base_url = server_info.url.rstrip("/")
            tool_server_url = base_url
            logger.info(
                f"Resolved {self.capability_name} to Tool Server: {server_info.name} ({base_url})"
            )
        else:
            # Legacy mode: use fixed base_url
            base_url = self.tool_server_config.base_url.rstrip("/")
            tool_server_url = base_url

        url = f"{base_url}/{endpoint.lstrip('/')}"

        # Request operation token if admin_client is available
        headers: dict[str, str] = {}
        if self.admin_client is not None and self.capability_name and target:
            try:
                logger.debug(
                    f"Requesting operation token: cap={self.capability_name}, "
                    f"target={target}, target_type={target_type}"
                )
                operation_token = await self.admin_client.request_operation_token(
                    capability=self.capability_name,
                    target=target,
                    target_type=target_type,
                    tool_server_url=tool_server_url,
                    ticket_number=ticket_number,
                )
                headers["Authorization"] = f"Bearer {operation_token}"
                logger.debug("Operation token obtained, proceeding with tool server call")
            except OperationTokenError:
                raise  # Let handler escalate the ticket
            except Exception as e:
                logger.error(f"Failed to request operation token: {e}")
                raise OperationTokenError(
                    "portal_unavailable",
                    f"Authorization service unavailable: {e}"
                ) from e
        elif self.admin_client is not None and not target:
            logger.warning(
                f"admin_client is set but no target provided for {endpoint} — "
                f"proceeding without operation token (read-only or health endpoint)"
            )

        logger.info(f"Making {method} request to {url}")

        # Build mTLS tuple if both cert and key are configured
        mtls_cert = None
        if self.tool_server_config.client_cert_path and self.tool_server_config.client_key_path:
            mtls_cert = (
                self.tool_server_config.client_cert_path,
                self.tool_server_config.client_key_path,
            )
            logger.debug("mTLS: presenting client cert %s", self.tool_server_config.client_cert_path)

        async with httpx.AsyncClient(
            timeout=self.tool_server_config.timeout,
            verify=self.tool_server_config.verify_ssl,
            cert=mtls_cert,
        ) as client:
            try:
                if method.upper() == "GET":
                    response = await client.get(url, headers=headers)
                elif method.upper() == "POST":
                    response = await client.post(url, json=data, headers=headers)
                else:
                    raise ValueError(f"Unsupported HTTP method: {method}")

                # Handle 403 from tool server (token validation failed)
                if response.status_code == 403:
                    try:
                        error_data = response.json()
                        error_code = error_data.get("error", "token_invalid")
                        error_detail = error_data.get("detail", "Token validation failed")
                    except Exception:
                        error_code = "token_invalid"
                        error_detail = "Token validation failed"
                    error_msg = f"Tool server rejected request: [{error_code}] {error_detail}"
                    logger.error(error_msg)
                    # Don't retry — token failures are not transient
                    raise OperationTokenError(error_code, error_detail)

                # Raise for HTTP errors
                response.raise_for_status()

                # Parse JSON response
                result = response.json()
                logger.debug(f"Response: {result}")

                return result

            except httpx.HTTPStatusError as e:
                error_msg = f"HTTP {e.response.status_code}: {e.response.text}"
                logger.error(f"Request failed: {error_msg}")
                raise Exception(error_msg)

            except httpx.RequestError as e:
                error_msg = f"Request error: {str(e)}"
                logger.error(error_msg)
                raise Exception(error_msg)

    def _handle_error(self, operation: str, error: Exception) -> ErrorArtifact:
        """Create error artifact for tool errors.

        Args:
            operation: Name of the operation that failed.
            error: The exception that occurred.

        Returns:
            ErrorArtifact with error details.
        """
        error_msg = f"Failed to {operation}: {str(error)}"
        logger.error(error_msg)
        return ErrorArtifact(error_msg)
