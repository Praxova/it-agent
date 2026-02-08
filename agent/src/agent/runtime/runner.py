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
from .triggers import TriggerProvider, TriggerProviderFactory, WorkItem

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
        self._trigger: TriggerProvider | None = None

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

        # Determine trigger type and assignment group from workflow config
        trigger_type = "servicenow"  # default for backward compatibility
        assignment_group = ""
        if export.workflow and export.workflow.trigger_type:
            trigger_type = export.workflow.trigger_type
        if export.service_now:
            assignment_group = export.service_now.assignment_group or ""

        # Create trigger provider
        self._trigger = TriggerProviderFactory.create(
            trigger_type=trigger_type,
            snow_client=self._snow_client,
            assignment_group=assignment_group,
            admin_portal_url=self.admin_portal_url,
            agent_name=self.agent_name,
        )
        logger.info(f"Created trigger provider: {self._trigger.display_name}")

        # Call trigger startup
        await self._trigger.startup()

        # Create workflow engine
        self._engine = WorkflowEngine(
            export=export,
            llm_driver=llm_driver,
            admin_portal_url=self.admin_portal_url,
            agent_name=self.agent_name,
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

        # Shutdown trigger provider
        if self._trigger:
            await self._trigger.shutdown()

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
        """Poll for work items and process them."""
        if not self._trigger or not self._engine:
            logger.warning("Agent not fully initialized, skipping poll")
            return

        self._last_poll_time = datetime.utcnow()

        # Poll for new work items (trigger-agnostic)
        items = await self._trigger.poll()

        if not items:
            logger.debug("No new work items")
            # Still poll for approval decisions even when no new tickets
            await self._poll_approvals()
            # Poll for clarification replies
            await self._poll_clarifications()
            return

        logger.info(f"Found {len(items)} new work items")

        for item in items:
            await self._process_work_item(item)

        # Poll for approval decisions
        await self._poll_approvals()
        # Poll for clarification replies
        await self._poll_clarifications()

    async def _process_work_item(self, item: WorkItem):
        """Process a single work item through the workflow."""
        logger.info(f"Processing {item.display_id}: {item.title[:50]}...")

        try:
            # Acknowledge pickup
            await self._trigger.acknowledge(item)

            # Execute workflow
            context = await self._engine.execute(
                ticket_id=item.display_id or item.id,
                ticket_data=item.data,
            )

            # Handle result
            self._tickets_processed += 1

            if context.status == ExecutionStatus.COMPLETED:
                self._tickets_succeeded += 1
                logger.info(f"{item.display_id} completed successfully")
                await self._trigger.complete(item, context)

            elif context.status == ExecutionStatus.SUSPENDED:
                logger.info(f"{item.display_id} suspended (approval pending)")
                # Don't count as processed yet — will resume after approval

            elif context.status == ExecutionStatus.ESCALATED:
                self._tickets_escalated += 1
                logger.info(f"{item.display_id} escalated: {context.escalation_reason}")
                await self._trigger.escalate(item, context)

            else:
                self._tickets_failed += 1
                logger.warning(f"{item.display_id} ended with status: {context.status}")
                # Build a meaningful error from context
                completed_steps = len([
                    r for r in context.step_results.values()
                    if r.status == ExecutionStatus.COMPLETED
                ])
                error_msg = (
                    f"Processing ended with status {context.status}. "
                    f"Error: {context.escalation_reason}. "
                    f"Steps completed: {completed_steps}"
                )
                await self._trigger.fail(item, error_msg)

        except Exception as e:
            self._tickets_processed += 1
            self._tickets_failed += 1
            self._last_error = str(e)
            logger.error(f"Failed to process {item.display_id}: {e}", exc_info=True)
            await self._trigger.fail(item, str(e))

    async def _poll_approvals(self):
        """Poll Admin Portal for actionable approval decisions."""
        if not self._engine:
            return

        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/approvals/actionable"
                params = {"agentName": self.agent_name}
                response = await client.get(url, params=params, timeout=10.0)

                if response.status_code == 404:
                    # Endpoint not available (older portal), skip silently
                    return
                response.raise_for_status()
                decisions = response.json()

            if not decisions:
                return

            logger.info(f"Found {len(decisions)} approval decisions to process")

            for decision in decisions:
                await self._process_approval_decision(decision)

        except httpx.HTTPStatusError as e:
            if e.response.status_code != 404:
                logger.warning(f"Approval poll failed: {e}")
        except Exception as e:
            logger.debug(f"Approval poll error (portal may not support approvals): {e}")

    async def _process_approval_decision(self, decision: dict):
        """Process a single approval decision."""
        import json as _json

        approval_id = decision.get("id")
        status = decision.get("status", "")
        ticket_id = decision.get("ticketId", "")
        resume_after = decision.get("resumeAfterStep", "")
        workflow_name = decision.get("workflowName", "")
        context_json = decision.get("contextSnapshotJson") or decision.get("contextSnapshot", "{}")

        # Parse context snapshot
        if isinstance(context_json, str):
            context_snapshot = _json.loads(context_json)
        else:
            context_snapshot = context_json

        # Get ticket data from snapshot or use minimal
        ticket_data = context_snapshot.get("_ticket_data", {})

        try:
            if status in ("Approved", "AutoApproved"):
                logger.info(f"Resuming workflow for ticket {ticket_id} (approval {approval_id}: {status})")

                # Determine which engine to use for resume
                engine = self._get_resume_engine(workflow_name, context_snapshot)

                context = await engine.resume(
                    context_snapshot=context_snapshot,
                    ticket_id=ticket_id,
                    ticket_data=ticket_data,
                    resume_after_step=resume_after,
                    outcome="approved",
                )
                # Track stats and update ServiceNow
                self._tickets_processed += 1
                work_item = self._build_work_item_from_approval(decision)

                if context.status == ExecutionStatus.COMPLETED:
                    self._tickets_succeeded += 1
                    logger.info(f"{ticket_id} completed successfully after approval")
                    await self._trigger.complete(work_item, context)

                elif context.status == ExecutionStatus.SUSPENDED:
                    # Another approval step encountered — don't count yet
                    logger.info(f"{ticket_id} suspended again (another approval pending)")

                elif context.status == ExecutionStatus.ESCALATED:
                    self._tickets_escalated += 1
                    logger.info(f"{ticket_id} escalated after approval resume")
                    await self._trigger.escalate(work_item, context)

                else:
                    self._tickets_failed += 1
                    logger.warning(f"{ticket_id} failed after approval resume: {context.status}")
                    error_msg = f"Workflow failed after approval with status {context.status}"
                    await self._trigger.fail(work_item, error_msg)

            elif status in ("Rejected", "TimedOut"):
                logger.info(f"Approval {status.lower()} for ticket {ticket_id}, escalating")
                self._tickets_processed += 1
                self._tickets_escalated += 1
                # Update ServiceNow with rejection/timeout
                work_item = self._build_work_item_from_approval(decision)
                rejection_context = ExecutionContext(ticket_id=ticket_id, ticket_data=ticket_data)
                rejection_context.escalation_reason = (
                    f"Approval {status.lower()}: {decision.get('decision', 'No reason provided')}"
                )
                rejection_context.set_variable("ticket_type", context_snapshot.get("ticket_type", "unknown"))
                rejection_context.set_variable("confidence", context_snapshot.get("confidence", "N/A"))
                await self._trigger.escalate(work_item, rejection_context)

            # Acknowledge the approval so it doesn't appear again
            await self._acknowledge_approval(approval_id)

        except Exception as e:
            logger.error(f"Failed to process approval {approval_id}: {e}", exc_info=True)
            self._tickets_failed += 1

    def _get_resume_engine(self, workflow_name: str, context_snapshot: dict) -> WorkflowEngine:
        """
        Get the correct WorkflowEngine for resuming after approval.

        If the approval came from the main dispatcher workflow, return self._engine.
        If it came from a sub-workflow, build a child engine for that sub-workflow
        so resume() can find the correct steps.
        """
        from .models import AgentExport

        main_wf_name = ""
        if self._engine and self._engine.export and self._engine.export.workflow:
            main_wf_name = self._engine.export.workflow.name

        # If workflow matches main engine, use it directly
        if workflow_name == main_wf_name or not workflow_name:
            return self._engine

        # Sub-workflow: look up definition from export
        export = self._engine.export
        sub_workflow_def = export.sub_workflows.get(workflow_name)

        if not sub_workflow_def:
            # Try case-insensitive match
            for name, wf in export.sub_workflows.items():
                if name.lower() == workflow_name.lower():
                    sub_workflow_def = wf
                    break

        if not sub_workflow_def:
            logger.warning(
                f"Sub-workflow '{workflow_name}' not found in export, "
                f"falling back to main engine. "
                f"Available sub-workflows: {list(export.sub_workflows.keys())}"
            )
            return self._engine

        logger.info(f"Building child engine for sub-workflow '{workflow_name}' resume")

        # Build a temporary export with the sub-workflow as the main workflow
        child_export = AgentExport(
            version=export.version,
            exported_at=export.exported_at,
            agent=export.agent,
            llm_provider=export.llm_provider,
            service_now=export.service_now,
            workflow=sub_workflow_def,
            rulesets=export.rulesets,
            example_sets=export.example_sets,
            required_capabilities=export.required_capabilities,
            sub_workflows=export.sub_workflows,
        )

        child_engine = WorkflowEngine(
            export=child_export,
            llm_driver=self._engine.llm_driver,
            admin_portal_url=self.admin_portal_url,
            agent_name=self.agent_name,
        )

        # Share integration points
        child_engine.servicenow_client = self._engine.servicenow_client
        child_engine.capability_router = self._engine.capability_router

        return child_engine

    async def _acknowledge_approval(self, approval_id: str):
        """Acknowledge an approval decision so it won't be returned again."""
        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/approvals/{approval_id}/acknowledge"
                response = await client.post(url, json={"agentName": self.agent_name}, timeout=10.0)
                if response.status_code < 400:
                    logger.debug(f"Acknowledged approval {approval_id}")
        except Exception as e:
            logger.warning(f"Failed to acknowledge approval {approval_id}: {e}")

    def _build_work_item_from_approval(self, decision: dict) -> WorkItem:
        """Reconstruct a WorkItem from an approval decision for trigger callbacks."""
        import json as _json

        ticket_id = decision.get("ticketId", "")
        context_json = decision.get("contextSnapshotJson") or decision.get("contextSnapshot", "{}")
        if isinstance(context_json, str):
            context_snapshot = _json.loads(context_json)
        else:
            context_snapshot = context_json

        ticket_data = context_snapshot.get("_ticket_data", {})

        return WorkItem(
            id=ticket_data.get("sys_id", ticket_id),
            source_type=self._trigger.trigger_type if self._trigger else "servicenow",
            data=ticket_data,
            raw=None,
            title=decision.get("ticketShortDescription", ""),
            description=ticket_data.get("description", ""),
            requester=ticket_data.get("caller_id", ""),
            display_id=ticket_id,
        )

    async def _poll_clarifications(self):
        """Poll for resolved clarifications (user replied on ServiceNow ticket)."""
        if not self._engine or not self._snow_client:
            return

        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/clarifications/pending"
                params = {"agentName": self.agent_name}
                response = await client.get(url, params=params, timeout=10.0)

                if response.status_code == 404:
                    return  # Endpoint not available
                response.raise_for_status()
                clarifications = response.json()

            if not clarifications:
                return

            logger.info(f"Checking {len(clarifications)} pending clarifications for replies")

            for clarification in clarifications:
                await self._check_clarification_reply(clarification)

        except Exception as e:
            logger.debug(f"Clarification poll error: {e}")

    async def _check_clarification_reply(self, clarification: dict):
        """Check if user has replied to a clarification on ServiceNow."""
        ticket_sys_id = clarification.get("ticketSysId")
        posted_at = clarification.get("postedAt")
        clarification_id = clarification.get("id")

        if not ticket_sys_id or not posted_at:
            return

        # Query ServiceNow for customer comments after our question was posted
        comments = await self._snow_client.get_customer_comments(
            sys_id=ticket_sys_id,
            since=posted_at,
        )

        if not comments:
            return  # No reply yet

        # Take the first reply (oldest customer comment after our question)
        user_reply = comments[0].get("value", "")

        logger.info(f"Got clarification reply for ticket "
                    f"{clarification.get('ticketId')}: {user_reply[:100]}...")

        # Resolve the clarification in portal
        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/clarifications/{clarification_id}/resolve"
                response = await client.post(
                    url,
                    json={"userReply": user_reply},
                    timeout=10.0,
                )
                response.raise_for_status()
        except Exception as e:
            logger.error(f"Failed to resolve clarification {clarification_id}: {e}")
            return

        # Resume the workflow
        await self._resume_from_clarification(clarification, user_reply)

    async def _resume_from_clarification(self, clarification: dict, user_reply: str):
        """Resume a workflow after user replied to clarification."""
        import json as _json

        clarification_id = clarification.get("id")
        ticket_id = clarification.get("ticketId")
        resume_after = clarification.get("resumeAfterStep", "")
        workflow_name = clarification.get("workflowName", "")
        context_json = clarification.get("contextSnapshotJson", "{}")

        if isinstance(context_json, str):
            context_snapshot = _json.loads(context_json)
        else:
            context_snapshot = context_json

        ticket_data = context_snapshot.get("_ticket_data", {})

        # Inject user reply into context
        context_snapshot["user_reply"] = user_reply
        context_snapshot["clarification_resolved"] = True

        try:
            logger.info(f"Resuming workflow for ticket {ticket_id} after clarification reply")

            # Use same engine selection logic as approval resume
            engine = self._get_resume_engine(workflow_name, context_snapshot)

            context = await engine.resume(
                context_snapshot=context_snapshot,
                ticket_id=ticket_id,
                ticket_data=ticket_data,
                resume_after_step=resume_after,
                outcome="resolved",
            )

            # Track stats and handle result
            self._tickets_processed += 1
            work_item = self._build_work_item_from_clarification(clarification)

            if context.status == ExecutionStatus.COMPLETED:
                self._tickets_succeeded += 1
                logger.info(f"{ticket_id} completed after clarification")
                await self._trigger.complete(work_item, context)
            elif context.status == ExecutionStatus.SUSPENDED:
                logger.info(f"{ticket_id} suspended again after clarification")
            elif context.status == ExecutionStatus.ESCALATED:
                self._tickets_escalated += 1
                await self._trigger.escalate(work_item, context)
            else:
                self._tickets_failed += 1
                await self._trigger.fail(work_item, f"Failed after clarification: {context.status}")

            # Acknowledge clarification
            await self._acknowledge_clarification(clarification_id)

        except Exception as e:
            logger.error(f"Failed to resume from clarification {clarification_id}: {e}", exc_info=True)
            self._tickets_failed += 1

    def _build_work_item_from_clarification(self, clarification: dict) -> WorkItem:
        """Reconstruct a WorkItem from a clarification for trigger callbacks."""
        import json as _json

        ticket_id = clarification.get("ticketId", "")
        context_json = clarification.get("contextSnapshotJson", "{}")
        if isinstance(context_json, str):
            context_snapshot = _json.loads(context_json)
        else:
            context_snapshot = context_json

        ticket_data = context_snapshot.get("_ticket_data", {})

        return WorkItem(
            id=ticket_data.get("sys_id", ticket_id),
            source_type=self._trigger.trigger_type if self._trigger else "servicenow",
            data=ticket_data,
            raw=None,
            title=ticket_data.get("short_description", ""),
            description=ticket_data.get("description", ""),
            requester=ticket_data.get("caller_id", ""),
            display_id=ticket_id,
        )

    async def _acknowledge_clarification(self, clarification_id: str):
        """Acknowledge a clarification so it won't be returned again."""
        try:
            async with httpx.AsyncClient() as client:
                url = f"{self.admin_portal_url}/api/clarifications/{clarification_id}/acknowledge"
                response = await client.post(
                    url,
                    json={"agentName": self.agent_name},
                    timeout=10.0,
                )
                if response.status_code < 400:
                    logger.debug(f"Acknowledged clarification {clarification_id}")
        except Exception as e:
            logger.warning(f"Failed to acknowledge clarification {clarification_id}: {e}")

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
