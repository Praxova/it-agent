"""Execute step executor - calls Tool Server."""
from __future__ import annotations
import logging
from typing import Any

import httpx

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class ExecuteExecutor(BaseStepExecutor):
    """
    Executes Execute steps - calls Tool Server APIs.

    Uses capability routing to find the appropriate Tool Server,
    then calls its API to perform the action.
    """

    @property
    def step_type(self) -> str:
        return StepType.EXECUTE.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute tool server action.

        Configuration options:
        - capability: The capability to invoke (e.g., "ad-password-reset")
        - action_params: Static parameters for the action
        - param_mapping: Map context variables to action parameters
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}
        capability = config.get("capability")

        if not capability:
            result.fail("No capability specified in step configuration")
            return result

        try:
            # Get Tool Server URL for this capability
            tool_server_url = await self._get_tool_server_url(capability, context)

            if not tool_server_url:
                result.fail(f"No Tool Server found for capability: {capability}")
                return result

            # Build action parameters
            params = self._build_action_params(config, context)

            # Execute the action
            action_result = await self._call_tool_server(
                tool_server_url=tool_server_url,
                capability=capability,
                params=params,
            )

            result.complete({
                "success": action_result.get("success", False),
                "message": action_result.get("message", ""),
                "capability": capability,
                "tool_server": tool_server_url,
                "action_result": action_result,
            })

            if action_result.get("success"):
                logger.info(f"Action '{capability}' succeeded: {action_result.get('message')}")
            else:
                logger.warning(f"Action '{capability}' failed: {action_result.get('message')}")

        except Exception as e:
            logger.error(f"Execute step failed: {e}")
            result.fail(str(e))

        return result

    async def _get_tool_server_url(
        self,
        capability: str,
        context: ExecutionContext,
    ) -> str | None:
        """Query Admin Portal for Tool Server URL."""
        if not context.admin_portal_url:
            logger.warning("No Admin Portal URL configured, using mock")
            return "http://localhost:8080"  # Default for testing

        url = f"{context.admin_portal_url}/api/capabilities/{capability}/servers"

        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(url, timeout=10.0)

                if response.status_code == 404:
                    return None

                response.raise_for_status()
                data = response.json()
                servers = data.get("servers", []) if isinstance(data, dict) else data

                if servers and len(servers) > 0:
                    # Return first healthy server
                    return servers[0].get("url")

                return None

        except Exception as e:
            logger.error(f"Failed to query capability routing: {e}")
            return None

    def _build_action_params(
        self,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> dict[str, Any]:
        """Build action parameters from config and context."""
        params = dict(config.get("action_params", {}))

        # Map context variables to parameters
        param_mapping = config.get("param_mapping", {
            "username": "affected_user",
            "user": "affected_user",
            "group_name": "target_group",
            "path": "target_resource",
        })

        for param_name, var_name in param_mapping.items():
            value = context.get_variable(var_name)
            if value is not None:
                params[param_name] = value

        return params

    async def _call_tool_server(
        self,
        tool_server_url: str,
        capability: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        """Call the Tool Server API."""
        # Map capability to endpoint
        endpoint_map = {
            "ad-password-reset": "/api/v1/password/reset",
            "ad-group-add": "/api/v1/groups/add-member",
            "ad-group-remove": "/api/v1/groups/remove-member",
            "ntfs-permission-grant": "/api/v1/permissions/grant",
            "ntfs-permission-revoke": "/api/v1/permissions/revoke",
        }

        endpoint = endpoint_map.get(capability, f"/api/v1/{capability}")
        url = f"{tool_server_url.rstrip('/')}{endpoint}"

        logger.info(f"Calling Tool Server: POST {url}")
        logger.debug(f"Parameters: {params}")

        try:
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    url,
                    json=params,
                    timeout=30.0,
                )

                if response.status_code >= 400:
                    return {
                        "success": False,
                        "message": f"Tool Server returned {response.status_code}: {response.text}",
                    }

                return response.json()

        except httpx.TimeoutException:
            return {"success": False, "message": "Tool Server request timed out"}
        except Exception as e:
            return {"success": False, "message": str(e)}
