"""Registry for trigger providers."""
from __future__ import annotations
import logging
from typing import Any

from .base import TriggerProvider, TriggerType
from .servicenow_provider import ServiceNowTriggerProvider
from .manual_provider import ManualTriggerProvider
from ..integrations.servicenow_client import ServiceNowClient

logger = logging.getLogger(__name__)


class TriggerProviderFactory:
    """
    Factory that creates the appropriate TriggerProvider based on
    workflow trigger type and agent configuration.
    """

    @staticmethod
    def create(
        trigger_type: str | TriggerType,
        snow_client: ServiceNowClient | None = None,
        assignment_group: str = "",
        **kwargs: Any,
    ) -> TriggerProvider:
        """
        Create a trigger provider based on type.

        Args:
            trigger_type: The trigger type from the workflow definition.
                          Can be a TriggerType enum or string like "servicenow".
            snow_client: ServiceNow client (required for servicenow type)
            assignment_group: ServiceNow assignment group (for servicenow type)
            **kwargs: Additional provider-specific configuration

        Returns:
            Configured TriggerProvider instance

        Raises:
            ValueError: If trigger type is unsupported or missing required config
        """
        # Normalize to enum
        if isinstance(trigger_type, str):
            try:
                trigger_type = TriggerType(trigger_type.lower())
            except ValueError:
                # Default to servicenow for backward compatibility
                logger.warning(
                    f"Unknown trigger type '{trigger_type}', defaulting to ServiceNow"
                )
                trigger_type = TriggerType.SERVICENOW

        if trigger_type == TriggerType.SERVICENOW:
            if not snow_client:
                raise ValueError("ServiceNow trigger requires a ServiceNow client")
            return ServiceNowTriggerProvider(
                client=snow_client,
                assignment_group=assignment_group,
                poll_limit=kwargs.get("poll_limit", 5),
            )

        elif trigger_type == TriggerType.MANUAL:
            admin_portal_url = kwargs.get("admin_portal_url", "")
            agent_name = kwargs.get("agent_name", "")
            if not admin_portal_url or not agent_name:
                raise ValueError("Manual trigger requires admin_portal_url and agent_name")
            return ManualTriggerProvider(
                admin_portal_url=admin_portal_url,
                agent_name=agent_name,
            )

        else:
            raise ValueError(
                f"Trigger type '{trigger_type}' is not yet implemented. "
                f"Supported types: {[t.value for t in TriggerType if t in (TriggerType.SERVICENOW, TriggerType.MANUAL)]}"
            )
