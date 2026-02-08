"""Query step executor - queries external systems."""
from __future__ import annotations
import logging
import re as _re
from typing import Any

import httpx

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from ..utils import resolve_template
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class QueryExecutor(BaseStepExecutor):
    """
    Executes Query steps - queries external systems for context.

    Can query:
    - Active Directory for user info
    - ServiceNow for related tickets
    - Other systems via configured endpoints
    """

    @property
    def step_type(self) -> str:
        return StepType.QUERY.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute query step.

        Configuration options:
        - query_type: "ad_user", "related_tickets", "custom"
        - endpoint: Custom endpoint URL
        - params: Query parameters
        - store_as: Variable name to store results
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}
        query_type = config.get("query_type", "custom")
        store_as = config.get("store_as", "query_result")

        try:
            if query_type == "ad_user":
                query_result = await self._query_ad_user(context, config)
            elif query_type == "related_tickets":
                query_result = await self._query_related_tickets(context, config)
            else:
                query_result = await self._query_custom(context, config)

            # Store result in context
            context.set_variable(store_as, query_result)

            result.complete({
                "query_type": query_type,
                "results_count": len(query_result) if isinstance(query_result, list) else 1,
                "stored_as": store_as,
            })

            logger.info(f"Query '{query_type}' completed, stored as '{store_as}'")

        except Exception as e:
            logger.error(f"Query failed: {e}")
            result.fail(str(e))

        return result

    async def _query_ad_user(
        self,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> dict[str, Any]:
        """Query AD for user information."""
        affected_user = context.get_variable("affected_user")

        if not affected_user:
            return {"found": False, "error": "No username to query"}

        # In real implementation, would query Tool Server
        # For now, return mock data
        logger.info(f"Would query AD for user: {affected_user}")

        return {
            "found": True,
            "username": affected_user,
            "display_name": f"User {affected_user}",
            "email": f"{affected_user}@example.com",
            "department": "IT",
            "manager": "manager@example.com",
        }

    async def _query_related_tickets(
        self,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> list[dict[str, Any]]:
        """Query ServiceNow for related tickets."""
        affected_user = context.get_variable("affected_user")

        # In real implementation, would query ServiceNow
        logger.info(f"Would query related tickets for: {affected_user}")

        return []  # No related tickets in mock

    async def _query_custom(
        self,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> Any:
        """Execute custom query via capability routing.

        Supports two modes:
        1. ``endpoint`` is a capability key (e.g. ``"ad-computer-lookup"``)
           — resolved via the execute executor's endpoint map + Tool Server
             capability routing.
        2. ``endpoint`` is a full URL — used directly (legacy behaviour).

        Path parameters like ``{computer_name}`` are substituted from
        ``params`` (after template resolution).
        """
        from .execute import ExecuteExecutor

        endpoint_key = config.get("endpoint")
        if not endpoint_key:
            return {"error": "No endpoint configured"}

        raw_params = config.get("params", {})
        store_as = config.get("store_as", "query_result")

        # Resolve {{variable}} templates in param values
        resolved_params: dict[str, Any] = {}
        for key, value in raw_params.items():
            if isinstance(value, str) and "{{" in value:
                resolved_params[key] = resolve_template(value, context)
            else:
                resolved_params[key] = value

        # Check if endpoint_key is a capability name in the execute executor's map
        exec_executor = ExecuteExecutor()
        tool_server_url = await exec_executor._get_tool_server_url(endpoint_key, context)

        if tool_server_url:
            # Use capability routing — look up method/path from endpoint_map
            result = await exec_executor._call_tool_server(
                tool_server_url=tool_server_url,
                capability=endpoint_key,
                params=resolved_params,
            )
            return result

        # Fallback: treat endpoint as a raw URL (legacy)
        logger.info(f"Custom query falling back to raw URL: {endpoint_key}")
        async with httpx.AsyncClient() as client:
            response = await client.get(endpoint_key, params=resolved_params, timeout=30.0)
            response.raise_for_status()
            return response.json()
