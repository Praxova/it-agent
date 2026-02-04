"""Agent runner - main entry point for workflow execution."""
from __future__ import annotations
import asyncio
import logging
import signal
import socket
import time
from datetime import datetime
from typing import Any

import httpx

from .config_loader import ConfigLoader
from .workflow_engine import WorkflowEngine
from .execution_context import ExecutionContext, ExecutionStatus
from .integrations.servicenow_client import ServiceNowClient, ServiceNowCredentials
from .integrations.driver_factory import create_prompt_driver
from .integrations.capability_router import CapabilityRouter

logger = logging.getLogger(__name__)


def _normalize_key(key: str) -> str:
    """Normalize config key for comparison (strip underscores/hyphens, lowercase)."""
    return key.lower().replace("_", "").replace("-", "")


def _get_config_value(config: dict, key: str, default: Any = None) -> Any:
    """Get config value with case-insensitive, naming-convention-agnostic lookup.

    Handles PascalCase (C#) vs snake_case (Python) keys,
    e.g. 'instance_url' matches 'InstanceUrl'.
    """
    # Try exact match first
    if key in config:
        return config[key]
    # Try normalized match (case + underscore/hyphen insensitive)
    key_norm = _normalize_key(key)
    for k, v in config.items():
        if _normalize_key(k) == key_norm:
            return v
    return default


class AgentRunner:
    """
    Main agent runner that orchestrates ticket processing.

    Lifecycle:
    1. Load configuration from Admin Portal
    2. Initialize ServiceNow client and LLM driver
    3. Poll for new tickets
    4. Execute workflow for each ticket
    5. Repeat until stopped
    """

    def __init__(
        self,
        admin_portal_url: str,
        agent_name: str,
        poll_interval: int = 30,
        heartbeat_interval: int = 60,
    ):
        self.admin_portal_url = admin_portal_url
        self.agent_name = agent_name
        self.poll_interval = poll_interval
        self.heartbeat_interval = heartbeat_interval

        self._running = False
        self._config_loader: ConfigLoader | None = None
        self._engine: WorkflowEngine | None = None
        self._snow_client: ServiceNowClient | None = None
        self._capability_router: CapabilityRouter | None = None

        # Stats
        self._tickets_processed = 0
        self._tickets_succeeded = 0
        self._tickets_escalated = 0
        self._tickets_failed = 0
        self._started_at: datetime | None = None
        self._last_poll_time: datetime | None = None
        self._last_error: str | None = None

        # Enabled/disabled cache
        self._enabled_cache: bool = True
        self._enabled_cache_time: float = 0
        self._enabled_cache_ttl: float = 30.0
        self._was_enabled: bool = True

        # Config version tracking
        self._config_version: str | None = None
        self._poll_count: int = 0

        # Heartbeat timing
        self._last_heartbeat_time: float = 0

    async def initialize(self):
        """Initialize the agent with configuration from Admin Portal."""
        logger.info(f"Initializing agent '{self.agent_name}'...")

        # Load configuration
        self._config_loader = ConfigLoader(self.admin_portal_url, self.agent_name)
        export = await self._config_loader.load()

        logger.info(f"Loaded configuration: {export.agent.display_name or export.agent.name}")

        # Capture config version for change detection
        self._config_version = export.exported_at.isoformat() if export.exported_at else None

        # Create LLM driver
        if not export.llm_provider:
            raise RuntimeError("No LLM provider configured for agent")

        llm_driver = create_prompt_driver(export.llm_provider)
        logger.info(f"Created LLM driver: {export.llm_provider.provider_type}")

        # Create ServiceNow client
        if export.service_now:
            snow_creds = self._config_loader.get_servicenow_credentials()
            if snow_creds:
                sn_config = export.service_now.config
                instance_url = _get_config_value(sn_config, "instance_url", "")
                # Use credential resolver username, fall back to config dict
                username = snow_creds.get("username", "") or _get_config_value(sn_config, "username", "")
                password = snow_creds.get("password", "")

                logger.debug(f"ServiceNow config keys: {list(sn_config.keys())}")
                logger.debug(f"Resolved ServiceNow instance_url: {instance_url!r}")
                logger.debug(f"Resolved ServiceNow username: {username!r}")

                self._snow_client = ServiceNowClient(ServiceNowCredentials(
                    instance_url=instance_url,
                    username=username,
                    password=password,
                ))
                logger.info(f"Created ServiceNow client for {instance_url}")

        # Create capability router
        self._capability_router = CapabilityRouter(self.admin_portal_url)

        # Create workflow engine
        self._engine = WorkflowEngine(
            export=export,
            llm_driver=llm_driver,
            admin_portal_url=self.admin_portal_url,
        )

        # Inject ServiceNow client into engine context (for executors)
        self._engine.servicenow_client = self._snow_client
        self._engine.capability_router = self._capability_router

        logger.info("Agent initialized successfully")

    async def run(self):
        """Run the agent main loop."""
        if not self._engine:
            await self.initialize()

        self._running = True
        self._started_at = datetime.utcnow()

        logger.info(f"Agent '{self.agent_name}' starting main loop...")

        # Send initial heartbeat
        await self._report_heartbeat()

        while self._running:
            # Check if agent is still enabled in portal
            enabled = await self._check_enabled()
            if not enabled:
                if self._was_enabled:
                    logger.info("Agent is disabled in Admin Portal, skipping poll cycle")
                    self._was_enabled = False
                # Still send heartbeats while disabled
                if (time.time() - self._last_heartbeat_time) >= self.heartbeat_interval:
                    await self._report_heartbeat()
                await asyncio.sleep(self.poll_interval)
                continue

            if not self._was_enabled:
                logger.info("Agent re-enabled, resuming")
                self._was_enabled = True

            try:
                await self._poll_and_process()
            except Exception as e:
                self._last_error = str(e)
                logger.error(f"Error in main loop: {e}", exc_info=True)

            # Check config version every 5 poll cycles
            self._poll_count += 1
            if self._poll_count % 5 == 0:
                if await self._check_config_changed():
                    logger.info("Configuration changed, reinitializing agent...")
                    await self.initialize()
                    logger.info("Agent reinitialized with new configuration")

            # Send heartbeat if interval has elapsed
            if (time.time() - self._last_heartbeat_time) >= self.heartbeat_interval:
                await self._report_heartbeat()

            if self._running:
                logger.debug(f"Sleeping {self.poll_interval}s before next poll...")
                await asyncio.sleep(self.poll_interval)

        # Send final shutdown heartbeat
        await self._send_shutdown_heartbeat()
        logger.info("Agent stopped cleanly")

    async def _check_enabled(self) -> bool:
        """
        Check if agent is enabled in Admin Portal.

        Caches the result for 30 seconds to avoid hitting the portal
        on every loop iteration. Falls back to last known state on error.

        Returns True if agent should process tickets.
        """
        now = time.time()

        # Return cached value if fresh
        if (now - self._enabled_cache_time) < self._enabled_cache_ttl:
            return self._enabled_cache

        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/export"
                response = await client.get(url, timeout=10.0)
                response.raise_for_status()
                data = response.json()

                # Navigate to the enabled field
                agent_data = data.get("agent", data)
                enabled = agent_data.get("isEnabled", agent_data.get("is_enabled", True))

                self._enabled_cache = bool(enabled)
                self._enabled_cache_time = now

                return self._enabled_cache

        except Exception as e:
            logger.warning(f"Failed to check agent enabled status: {e}. "
                           f"Using cached value: {self._enabled_cache}")
            self._enabled_cache_time = now  # Don't retry immediately on error
            return self._enabled_cache

    async def _report_heartbeat(self):
        """
        Report agent health and metrics to Admin Portal.

        This is best-effort — heartbeat failures should never crash the agent.
        """
        uptime = 0
        if self._started_at:
            uptime = (datetime.utcnow() - self._started_at).total_seconds()

        heartbeat = {
            "agentName": self.agent_name,
            "hostname": socket.gethostname(),
            "status": "running" if self._enabled_cache else "disabled",
            "uptime": int(uptime),
            "ticketsProcessed": self._tickets_processed,
            "ticketsSucceeded": self._tickets_succeeded,
            "ticketsFailed": self._tickets_failed,
            "ticketsEscalated": self._tickets_escalated,
            "lastPollTime": self._last_poll_time.isoformat() if self._last_poll_time else None,
            "lastError": self._last_error,
            "pollInterval": self.poll_interval,
            "timestamp": datetime.utcnow().isoformat(),
        }

        # Try by-name endpoint first
        urls_to_try = [
            f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/runtime/heartbeat",
            f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/heartbeat",
        ]

        for url in urls_to_try:
            try:
                async with httpx.AsyncClient() as client:
                    response = await client.post(url, json=heartbeat, timeout=10.0)
                    if response.status_code < 400:
                        logger.debug("Heartbeat sent successfully")
                        self._last_heartbeat_time = time.time()
                        return
                    elif response.status_code == 404:
                        continue  # Try next URL
                    else:
                        logger.warning(f"Heartbeat returned {response.status_code}")
                        self._last_heartbeat_time = time.time()
                        return
            except Exception as e:
                logger.debug(f"Heartbeat to {url} failed: {e}")
                continue

        logger.warning("Failed to send heartbeat to any endpoint")
        self._last_heartbeat_time = time.time()  # Don't spam retries

    async def _check_config_changed(self) -> bool:
        """
        Check if agent configuration has changed since last load.

        Compares the exportedAt timestamp from the portal with our
        stored version. Returns True if config needs reload.
        """
        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/export"
                response = await client.get(url, timeout=10.0)
                response.raise_for_status()
                data = response.json()

                new_version = data.get("exportedAt",
                              data.get("exported_at", ""))

                if new_version and new_version != self._config_version:
                    logger.info(f"Config version changed: {self._config_version} -> {new_version}")
                    return True

                return False

        except Exception as e:
            logger.debug(f"Config version check failed: {e}")
            return False

    async def _send_shutdown_heartbeat(self):
        """Send a final heartbeat with status='stopped' during shutdown."""
        final_heartbeat = {
            "agentName": self.agent_name,
            "hostname": socket.gethostname(),
            "status": "stopped",
            "ticketsProcessed": self._tickets_processed,
            "ticketsSucceeded": self._tickets_succeeded,
            "ticketsFailed": self._tickets_failed,
            "ticketsEscalated": self._tickets_escalated,
            "timestamp": datetime.utcnow().isoformat(),
        }

        url = f"{self.admin_portal_url}/api/agents/by-name/{self.agent_name}/runtime/heartbeat"
        try:
            async with httpx.AsyncClient() as client:
                await client.post(url, json=final_heartbeat, timeout=5.0)
        except Exception:
            pass  # Best effort on shutdown

    async def _poll_and_process(self):
        """Poll for tickets and process them."""
        if not self._snow_client or not self._engine:
            logger.warning("Agent not fully initialized, skipping poll")
            return

        self._last_poll_time = datetime.utcnow()

        # Get assignment group from ServiceNow config
        export = self._config_loader._export
        assignment_group = ""
        if export.service_now:
            assignment_group = export.service_now.assignment_group or ""
            if not assignment_group:
                logger.debug(f"ServiceNow config keys: {list(export.service_now.config.keys())}")

        if not assignment_group:
            logger.warning("No assignment group configured on ServiceNow provider")
            return

        # Poll for new tickets
        tickets = await self._snow_client.poll_queue(
            assignment_group=assignment_group,
            state="1",  # New
            limit=5,
        )

        if not tickets:
            logger.debug("No new tickets")
            return

        logger.info(f"Found {len(tickets)} new tickets")

        # Process each ticket
        for ticket in tickets:
            await self._process_ticket(ticket)

    async def _process_ticket(self, ticket):
        """Process a single ticket through the workflow."""
        logger.info(f"Processing ticket {ticket.number}: {ticket.short_description[:50]}...")

        try:
            # Set ticket to In Progress
            await self._snow_client.set_state(ticket.sys_id, "2")
            await self._snow_client.add_work_note(
                ticket.sys_id,
                "Ticket picked up by Lucid IT Agent for automated processing."
            )

            # Execute workflow
            context = await self._engine.execute(
                ticket_id=ticket.number,
                ticket_data=ticket.to_ticket_data(),
            )

            # Handle result
            self._tickets_processed += 1

            if context.status == ExecutionStatus.COMPLETED:
                self._tickets_succeeded += 1
                logger.info(f"Ticket {ticket.number} completed successfully")

                # Post-workflow ServiceNow updates for successful completion
                try:
                    if not context.get_variable("ticket_updated"):
                        # Write success work note and resolve
                        success_note = self._build_completion_note(context)
                        await self._snow_client.add_work_note(ticket.sys_id, success_note)
                        await self._snow_client.set_state(
                            ticket.sys_id, "6",  # Resolved
                            close_notes=f"Resolved by Lucid IT Agent - {context.get_variable('ticket_type', 'automated')}"
                        )
                except Exception as e:
                    logger.error(f"Failed to write completion notes: {e}")

            elif context.status == ExecutionStatus.ESCALATED:
                self._tickets_escalated += 1
                logger.info(f"Ticket {ticket.number} escalated: {context.escalation_reason}")

                # Ensure escalation reason is captured if executor was skipped
                try:
                    escalation_notes = context.get_variable("escalation_work_notes")
                    if not escalation_notes:
                        escalation_notes = (
                            f"Lucid IT Agent escalation:\n"
                            f"Reason: {context.escalation_reason}\n"
                            f"Ticket Type: {context.get_variable('ticket_type', 'Unknown')}\n"
                            f"Confidence: {context.get_variable('confidence', 'N/A')}"
                        )
                        await self._snow_client.add_work_note(ticket.sys_id, escalation_notes)
                except Exception as e:
                    logger.error(f"Failed to write escalation notes: {e}")
                # Leave ticket in "In Progress" state for human pickup

            else:
                self._tickets_failed += 1
                logger.warning(f"Ticket {ticket.number} ended with status: {context.status}")

                # Write failure details
                try:
                    completed_steps = len([r for r in context.step_results.values() if r.status == ExecutionStatus.COMPLETED])
                    failure_note = (
                        f"Lucid IT Agent processing failed:\n"
                        f"Error: {context.escalation_reason}\n"
                        f"Steps completed: {completed_steps}"
                    )
                    await self._snow_client.add_work_note(ticket.sys_id, failure_note)
                except Exception as e:
                    logger.error(f"Failed to write failure notes: {e}")

        except Exception as e:
            self._tickets_processed += 1
            self._tickets_failed += 1
            self._last_error = str(e)
            logger.error(f"Failed to process ticket {ticket.number}: {e}", exc_info=True)

            # Add error work note
            await self._snow_client.add_work_note(
                ticket.sys_id,
                f"Automated processing failed: {e}\nEscalating to human operator."
            )

    def _build_completion_note(self, context) -> str:
        """Build work note summarizing successful completion."""
        lines = [
            "=== Lucid IT Agent - Automated Resolution ===",
            "",
            f"Ticket Type: {context.get_variable('ticket_type', 'request')}",
            f"Affected User: {context.get_variable('affected_user', 'N/A')}",
            "",
            "Steps Completed:",
        ]
        for step_name, step_result in context.step_results.items():
            lines.append(f"  [PASS] {step_name}")

        # Include execution result if available
        exec_msg = context.get_step_output("execute-reset", "message")
        if exec_msg:
            lines.extend(["", f"Result: {exec_msg}"])

        return "\n".join(lines)

    def stop(self):
        """Stop the agent."""
        logger.info("Shutdown signal received, finishing current work...")
        self._running = False

    def get_stats(self) -> dict[str, Any]:
        """Get agent statistics."""
        return {
            "agent_name": self.agent_name,
            "started_at": self._started_at.isoformat() if self._started_at else None,
            "running": self._running,
            "tickets_processed": self._tickets_processed,
            "tickets_succeeded": self._tickets_succeeded,
            "tickets_escalated": self._tickets_escalated,
            "tickets_failed": self._tickets_failed,
        }


async def run_agent(
    admin_portal_url: str,
    agent_name: str,
    poll_interval: int = 30,
    heartbeat_interval: int = 60,
):
    """
    Run the agent with graceful shutdown handling.

    Args:
        admin_portal_url: URL of Admin Portal API
        agent_name: Name of agent to run
        poll_interval: Seconds between queue polls
        heartbeat_interval: Seconds between heartbeat reports
    """
    runner = AgentRunner(
        admin_portal_url=admin_portal_url,
        agent_name=agent_name,
        poll_interval=poll_interval,
        heartbeat_interval=heartbeat_interval,
    )

    # Handle shutdown signals
    loop = asyncio.get_event_loop()

    def signal_handler():
        runner.stop()

    for sig in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(sig, signal_handler)

    try:
        await runner.run()
    finally:
        # Print final stats
        stats = runner.get_stats()
        logger.info(f"Final stats: {stats}")
