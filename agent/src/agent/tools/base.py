"""Base class for agent tools that interact with Tool Server."""

import logging
from typing import Any

from attrs import Factory, define, field

import httpx
from griptape.artifacts import ErrorArtifact, TextArtifact
from griptape.tools import BaseTool
from pydantic import BaseModel, Field as PydanticField

logger = logging.getLogger(__name__)


class ToolServerConfig(BaseModel):
    """Configuration for Tool Server connection.

    Attributes:
        base_url: Base URL of the Tool Server API.
        timeout: Request timeout in seconds.
        verify_ssl: Whether to verify SSL certificates.
    """

    base_url: str = PydanticField(
        default="http://localhost:8000/api/v1",
        description="Base URL of Tool Server API",
    )
    timeout: float = PydanticField(default=30.0, description="Request timeout in seconds")
    verify_ssl: bool = PydanticField(
        default=True, description="Whether to verify SSL certificates"
    )


@define
class BaseToolServerTool(BaseTool):
    """Base class for tools that interact with the Tool Server.

    Provides common HTTP client functionality and error handling.

    Attributes:
        tool_server_config: Tool Server configuration.
    """

    tool_server_config: ToolServerConfig = field(
        default=Factory(lambda: ToolServerConfig()),
        kw_only=True,
    )

    async def _make_request(
        self,
        method: str,
        endpoint: str,
        data: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        """Make HTTP request to Tool Server.

        Args:
            method: HTTP method (GET, POST, etc.).
            endpoint: API endpoint (relative to base_url).
            data: Optional request body data.

        Returns:
            Response JSON as dictionary.

        Raises:
            Exception: If request fails.
        """
        url = f"{self.tool_server_config.base_url}/{endpoint.lstrip('/')}"

        logger.info(f"Making {method} request to {url}")

        async with httpx.AsyncClient(
            timeout=self.tool_server_config.timeout, verify=self.tool_server_config.verify_ssl
        ) as client:
            try:
                if method.upper() == "GET":
                    response = await client.get(url)
                elif method.upper() == "POST":
                    response = await client.post(url, json=data)
                else:
                    raise ValueError(f"Unsupported HTTP method: {method}")

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

            except Exception as e:
                error_msg = f"Unexpected error: {str(e)}"
                logger.exception(error_msg)
                raise

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
