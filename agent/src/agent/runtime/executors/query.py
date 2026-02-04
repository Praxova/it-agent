"""Query step executor - queries external systems."""
from __future__ import annotations
import logging
from typing import Any

import httpx

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
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
        """Execute custom query."""
        endpoint = config.get("endpoint")

        if not endpoint:
            return {"error": "No endpoint configured"}

        params = config.get("params", {})

        async with httpx.AsyncClient() as client:
            response = await client.get(endpoint, params=params, timeout=30.0)
            response.raise_for_status()
            return response.json()
