 Phase 4B-3 - Integration Layer
Context
Phase 4B-1 created the core runtime infrastructure (models, engine, context, conditions).
Phase 4B-2 implemented all 9 step executors (Trigger, Classify, Validate, Execute, etc.).
Now we wire the runtime with existing Lucid IT Agent components to enable real ticket processing.
Project Location: /home/alton/Documents/lucid-it-agent
Runtime Location: /home/alton/Documents/lucid-it-agent/agent/src/agent/runtime
Overview
Integrate the workflow runtime with:

ServiceNow Connector - Poll tickets, update state, add comments
Driver Factory - Create Griptape LLM drivers from export config
Capability Router - Query Admin Portal for Tool Server URLs
Main Entry Point - CLI to start the agent

Task 1: Create ServiceNow Integration (servicenow_client.py)
Create agent/src/agent/runtime/integrations/servicenow_client.py:
python"""ServiceNow client for workflow runtime."""
from __future__ import annotations
import logging
from typing import Any
from dataclasses import dataclass

import httpx

logger = logging.getLogger(__name__)


@dataclass
class ServiceNowCredentials:
    """ServiceNow connection credentials."""
    instance_url: str
    username: str
    password: str


@dataclass
class Ticket:
    """ServiceNow incident ticket."""
    sys_id: str
    number: str
    short_description: str
    description: str
    caller_id: str
    state: str
    assignment_group: str
    
    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Ticket":
        """Create Ticket from ServiceNow API response."""
        return cls(
            sys_id=data.get("sys_id", ""),
            number=data.get("number", ""),
            short_description=data.get("short_description", ""),
            description=data.get("description", ""),
            caller_id=data.get("caller_id", {}).get("value", "") if isinstance(data.get("caller_id"), dict) else data.get("caller_id", ""),
            state=data.get("state", ""),
            assignment_group=data.get("assignment_group", {}).get("value", "") if isinstance(data.get("assignment_group"), dict) else data.get("assignment_group", ""),
        )
    
    def to_ticket_data(self) -> dict[str, Any]:
        """Convert to ticket_data format for ExecutionContext."""
        return {
            "sys_id": self.sys_id,
            "number": self.number,
            "short_description": self.short_description,
            "description": self.description,
            "caller_id": self.caller_id,
            "state": self.state,
            "assignment_group": self.assignment_group,
        }


class ServiceNowClient:
    """
    Client for ServiceNow REST API.
    
    Handles:
    - Polling for new tickets
    - Updating ticket state
    - Adding work notes and comments
    """
    
    def __init__(self, credentials: ServiceNowCredentials):
        self.credentials = credentials
        self.base_url = credentials.instance_url.rstrip("/")
        self._auth = (credentials.username, credentials.password)
    
    async def poll_queue(
        self,
        assignment_group: str,
        state: str = "1",  # New
        limit: int = 10,
    ) -> list[Ticket]:
        """
        Poll for new tickets assigned to a group.
        
        Args:
            assignment_group: ServiceNow assignment group sys_id or name
            state: Ticket state to filter (1=New, 2=In Progress)
            limit: Maximum tickets to return
        
        Returns:
            List of Ticket objects
        """
        url = f"{self.base_url}/api/now/table/incident"
        params = {
            "sysparm_query": f"assignment_group={assignment_group}^state={state}",
            "sysparm_limit": str(limit),
            "sysparm_display_value": "false",
        }
        
        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    params=params,
                    auth=self._auth,
                    headers={"Accept": "application/json"},
                    timeout=30.0,
                )
                response.raise_for_status()
                
                data = response.json()
                tickets = [Ticket.from_dict(t) for t in data.get("result", [])]
                
                logger.info(f"Polled {len(tickets)} tickets from queue")
                return tickets
                
        except Exception as e:
            logger.error(f"Failed to poll ServiceNow: {e}")
            return []
    
    async def get_ticket(self, sys_id: str) -> Ticket | None:
        """Get a single ticket by sys_id."""
        url = f"{self.base_url}/api/now/table/incident/{sys_id}"
        
        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    auth=self._auth,
                    headers={"Accept": "application/json"},
                    timeout=30.0,
                )
                
                if response.status_code == 404:
                    return None
                
                response.raise_for_status()
                data = response.json()
                return Ticket.from_dict(data.get("result", {}))
                
        except Exception as e:
            logger.error(f"Failed to get ticket {sys_id}: {e}")
            return None
    
    async def update_ticket(
        self,
        sys_id: str,
        updates: dict[str, Any],
    ) -> bool:
        """
        Update a ticket.
        
        Args:
            sys_id: Ticket sys_id
            updates: Fields to update
        
        Returns:
            True if successful
        """
        url = f"{self.base_url}/api/now/table/incident/{sys_id}"
        
        try:
            async with httpx.AsyncClient() as client:
                response = await client.patch(
                    url,
                    json=updates,
                    auth=self._auth,
                    headers={
                        "Accept": "application/json",
                        "Content-Type": "application/json",
                    },
                    timeout=30.0,
                )
                response.raise_for_status()
                
                logger.info(f"Updated ticket {sys_id}")
                return True
                
        except Exception as e:
            logger.error(f"Failed to update ticket {sys_id}: {e}")
            return False
    
    async def add_work_note(self, sys_id: str, note: str) -> bool:
        """Add a work note to a ticket."""
        return await self.update_ticket(sys_id, {"work_notes": note})
    
    async def add_comment(self, sys_id: str, comment: str) -> bool:
        """Add a customer-visible comment to a ticket."""
        return await self.update_ticket(sys_id, {"comments": comment})
    
    async def set_state(
        self,
        sys_id: str,
        state: str,
        close_code: str | None = None,
        close_notes: str | None = None,
    ) -> bool:
        """
        Set ticket state.
        
        Args:
            sys_id: Ticket sys_id
            state: New state (1=New, 2=In Progress, 6=Resolved, 7=Closed)
            close_code: Resolution code (for resolved/closed)
            close_notes: Resolution notes
        """
        updates = {"state": state}
        
        if close_code:
            updates["close_code"] = close_code
        if close_notes:
            updates["close_notes"] = close_notes
        
        return await self.update_ticket(sys_id, updates)
    
    async def assign_to_group(self, sys_id: str, group: str) -> bool:
        """Assign ticket to a different group."""
        return await self.update_ticket(sys_id, {"assignment_group": group})
Task 2: Create Driver Factory Integration (driver_factory.py)
Create agent/src/agent/runtime/integrations/driver_factory.py:
python"""LLM driver factory for workflow runtime."""
from __future__ import annotations
import logging
import os
from typing import Any

from griptape.drivers.prompt import BasePromptDriver

from ..models import ProviderExportInfo, CredentialReference

logger = logging.getLogger(__name__)


class DriverFactoryError(Exception):
    """Error creating LLM driver."""
    pass


def resolve_credential(ref: CredentialReference) -> dict[str, str]:
    """
    Resolve credential reference to actual values.
    
    Supports storage types:
    - environment: Read from environment variables
    - vault: (future) Read from HashiCorp Vault
    """
    if ref.storage == "environment":
        result = {}
        
        if ref.username_key:
            result["username"] = os.environ.get(ref.username_key, "")
        if ref.password_key:
            result["password"] = os.environ.get(ref.password_key, "")
        if ref.api_key_key:
            result["api_key"] = os.environ.get(ref.api_key_key, "")
        
        return result
    
    elif ref.storage == "vault":
        # Future: HashiCorp Vault integration
        raise DriverFactoryError("Vault credential storage not yet implemented")
    
    elif ref.storage == "none":
        # No credentials needed (e.g., local Ollama)
        return {}
    
    else:
        raise DriverFactoryError(f"Unknown credential storage type: {ref.storage}")


def create_prompt_driver(provider: ProviderExportInfo) -> BasePromptDriver:
    """
    Create a Griptape PromptDriver from provider configuration.
    
    Args:
        provider: Provider configuration from export
    
    Returns:
        Configured BasePromptDriver instance
    
    Raises:
        DriverFactoryError: If driver cannot be created
    """
    provider_type = provider.provider_type
    config = provider.provider_config or {}
    
    # Resolve credentials
    credentials = {}
    if provider.credentials:
        credentials = resolve_credential(provider.credentials)
    
    try:
        if provider_type == "llm-ollama":
            from griptape.drivers.prompt.ollama import OllamaPromptDriver
            
            return OllamaPromptDriver(
                model=config.get("model", "llama3.1"),
                host=config.get("endpoint", "http://localhost:11434"),
                options={"temperature": config.get("temperature", 0.1)},
            )
        
        elif provider_type == "llm-openai":
            from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
            
            api_key = credentials.get("api_key") or os.environ.get("OPENAI_API_KEY")
            if not api_key:
                raise DriverFactoryError("OpenAI API key not found")
            
            return OpenAiChatPromptDriver(
                model=config.get("model", "gpt-4"),
                api_key=api_key,
                temperature=config.get("temperature", 0.1),
            )
        
        elif provider_type == "llm-anthropic":
            from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
            
            api_key = credentials.get("api_key") or os.environ.get("ANTHROPIC_API_KEY")
            if not api_key:
                raise DriverFactoryError("Anthropic API key not found")
            
            return AnthropicPromptDriver(
                model=config.get("model", "claude-3-sonnet-20240229"),
                api_key=api_key,
            )
        
        elif provider_type == "llm-azure-openai":
            from griptape.drivers.prompt.openai import AzureOpenAiChatPromptDriver
            
            api_key = credentials.get("api_key") or os.environ.get("AZURE_OPENAI_API_KEY")
            if not api_key:
                raise DriverFactoryError("Azure OpenAI API key not found")
            
            return AzureOpenAiChatPromptDriver(
                azure_endpoint=config.get("endpoint"),
                azure_deployment=config.get("deployment_name"),
                api_key=api_key,
                model=config.get("model", "gpt-4"),
            )
        
        else:
            raise DriverFactoryError(f"Unsupported LLM provider type: {provider_type}")
    
    except ImportError as e:
        raise DriverFactoryError(f"Missing driver dependency: {e}")
    except Exception as e:
        raise DriverFactoryError(f"Failed to create driver: {e}")
Task 3: Create Capability Router Integration (capability_router.py)
Create agent/src/agent/runtime/integrations/capability_router.py:
python"""Capability router for workflow runtime."""
from __future__ import annotations
import logging
import time
from dataclasses import dataclass
from typing import Any

import httpx

logger = logging.getLogger(__name__)


@dataclass
class ToolServerInfo:
    """Information about a Tool Server."""
    id: str
    name: str
    url: str
    status: str
    capabilities: list[str]


@dataclass
class CacheEntry:
    """Cache entry for capability routing."""
    servers: list[ToolServerInfo]
    timestamp: float


class NoCapableServerError(Exception):
    """No server found that can handle the capability."""
    pass


class CapabilityRouter:
    """
    Routes capability requests to appropriate Tool Servers.
    
    Queries Admin Portal for Tool Server URLs and caches results
    to reduce API calls.
    """
    
    def __init__(
        self,
        admin_portal_url: str,
        cache_ttl: int = 60,
    ):
        self._admin_url = admin_portal_url.rstrip("/")
        self._cache: dict[str, CacheEntry] = {}
        self._cache_ttl = cache_ttl
    
    async def get_server_for_capability(
        self,
        capability: str,
        prefer_healthy: bool = True,
    ) -> ToolServerInfo:
        """
        Get a Tool Server that provides the specified capability.
        
        Args:
            capability: Capability name (e.g., "ad-password-reset")
            prefer_healthy: Only return healthy servers
        
        Returns:
            ToolServerInfo with URL and details
        
        Raises:
            NoCapableServerError: If no server provides this capability
        """
        # Check cache
        if self._is_cached(capability):
            servers = self._cache[capability].servers
            if servers:
                return servers[0]  # Return first (highest priority)
        
        # Query Admin Portal
        servers = await self._query_servers(capability, prefer_healthy)
        
        if not servers:
            raise NoCapableServerError(f"No server provides capability: {capability}")
        
        # Cache result
        self._cache[capability] = CacheEntry(
            servers=servers,
            timestamp=time.time(),
        )
        
        return servers[0]
    
    async def get_all_servers_for_capability(
        self,
        capability: str,
    ) -> list[ToolServerInfo]:
        """Get all servers that provide a capability."""
        if self._is_cached(capability):
            return self._cache[capability].servers
        
        servers = await self._query_servers(capability, prefer_healthy=False)
        
        self._cache[capability] = CacheEntry(
            servers=servers,
            timestamp=time.time(),
        )
        
        return servers
    
    def _is_cached(self, capability: str) -> bool:
        """Check if capability is cached and not expired."""
        if capability not in self._cache:
            return False
        
        entry = self._cache[capability]
        age = time.time() - entry.timestamp
        return age < self._cache_ttl
    
    async def _query_servers(
        self,
        capability: str,
        prefer_healthy: bool,
    ) -> list[ToolServerInfo]:
        """Query Admin Portal for servers."""
        url = f"{self._admin_url}/api/capabilities/{capability}/servers"
        params = {}
        if prefer_healthy:
            params["status"] = "online"
        
        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(
                    url,
                    params=params,
                    timeout=10.0,
                )
                
                if response.status_code == 404:
                    return []
                
                response.raise_for_status()
                data = response.json()
                
                servers = []
                for item in data:
                    servers.append(ToolServerInfo(
                        id=item.get("id", ""),
                        name=item.get("name", ""),
                        url=item.get("url", ""),
                        status=item.get("status", "unknown"),
                        capabilities=item.get("capabilities", []),
                    ))
                
                logger.debug(f"Found {len(servers)} servers for capability '{capability}'")
                return servers
                
        except Exception as e:
            logger.error(f"Failed to query capability routing: {e}")
            return []
    
    def clear_cache(self):
        """Clear the routing cache."""
        self._cache.clear()
Task 4: Create integrations/init.py
Create agent/src/agent/runtime/integrations/__init__.py:
python"""Integration modules for workflow runtime."""
from .servicenow_client import ServiceNowClient, ServiceNowCredentials, Ticket
from .driver_factory import create_prompt_driver, resolve_credential, DriverFactoryError
from .capability_router import CapabilityRouter, ToolServerInfo, NoCapableServerError

__all__ = [
    "ServiceNowClient",
    "ServiceNowCredentials", 
    "Ticket",
    "create_prompt_driver",
    "resolve_credential",
    "DriverFactoryError",
    "CapabilityRouter",
    "ToolServerInfo",
    "NoCapableServerError",
]
Task 5: Create Agent Runner (runner.py)
Create agent/src/agent/runtime/runner.py:
python"""Agent runner - main entry point for workflow execution."""
from __future__ import annotations
import asyncio
import logging
import signal
from datetime import datetime
from typing import Any

from .config_loader import ConfigLoader
from .workflow_engine import WorkflowEngine
from .execution_context import ExecutionContext, ExecutionStatus
from .integrations.servicenow_client import ServiceNowClient, ServiceNowCredentials
from .integrations.driver_factory import create_prompt_driver
from .integrations.capability_router import CapabilityRouter

logger = logging.getLogger(__name__)


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
    ):
        self.admin_portal_url = admin_portal_url
        self.agent_name = agent_name
        self.poll_interval = poll_interval
        
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
    
    async def initialize(self):
        """Initialize the agent with configuration from Admin Portal."""
        logger.info(f"Initializing agent '{self.agent_name}'...")
        
        # Load configuration
        self._config_loader = ConfigLoader(self.admin_portal_url, self.agent_name)
        export = await self._config_loader.load()
        
        logger.info(f"Loaded configuration: {export.agent.display_name or export.agent.name}")
        
        # Create LLM driver
        if not export.llm_provider:
            raise RuntimeError("No LLM provider configured for agent")
        
        llm_driver = create_prompt_driver(export.llm_provider)
        logger.info(f"Created LLM driver: {export.llm_provider.provider_type}")
        
        # Create ServiceNow client
        if export.service_now:
            snow_creds = self._config_loader.get_servicenow_credentials()
            if snow_creds:
                self._snow_client = ServiceNowClient(ServiceNowCredentials(
                    instance_url=export.service_now.provider_config.get("instance_url", ""),
                    username=snow_creds.get("username", ""),
                    password=snow_creds.get("password", ""),
                ))
                logger.info(f"Created ServiceNow client")
        
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
        
        while self._running:
            try:
                await self._poll_and_process()
            except Exception as e:
                logger.error(f"Error in main loop: {e}", exc_info=True)
            
            if self._running:
                logger.debug(f"Sleeping {self.poll_interval}s before next poll...")
                await asyncio.sleep(self.poll_interval)
        
        logger.info("Agent stopped")
    
    async def _poll_and_process(self):
        """Poll for tickets and process them."""
        if not self._snow_client or not self._engine:
            logger.warning("Agent not fully initialized, skipping poll")
            return
        
        # Get assignment group from config
        export = self._config_loader._export
        assignment_group = export.agent.configuration.get("assignment_group", "")
        
        if not assignment_group:
            logger.warning("No assignment group configured")
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
                
            elif context.status == ExecutionStatus.ESCALATED:
                self._tickets_escalated += 1
                logger.info(f"Ticket {ticket.number} escalated: {context.escalation_reason}")
                
            else:
                self._tickets_failed += 1
                logger.warning(f"Ticket {ticket.number} ended with status: {context.status}")
            
        except Exception as e:
            self._tickets_failed += 1
            logger.error(f"Failed to process ticket {ticket.number}: {e}", exc_info=True)
            
            # Add error work note
            await self._snow_client.add_work_note(
                ticket.sys_id,
                f"Automated processing failed: {e}\nEscalating to human operator."
            )
    
    def stop(self):
        """Stop the agent."""
        logger.info("Stopping agent...")
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
):
    """
    Run the agent with graceful shutdown handling.
    
    Args:
        admin_portal_url: URL of Admin Portal API
        agent_name: Name of agent to run
        poll_interval: Seconds between queue polls
    """
    runner = AgentRunner(
        admin_portal_url=admin_portal_url,
        agent_name=agent_name,
        poll_interval=poll_interval,
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
Task 6: Create CLI Entry Point (cli.py)
Create agent/src/agent/runtime/cli.py:
python"""Command-line interface for Lucid IT Agent."""
from __future__ import annotations
import argparse
import asyncio
import logging
import os
import sys

from .runner import run_agent


def setup_logging(level: str = "INFO"):
    """Configure logging."""
    logging.basicConfig(
        level=getattr(logging, level.upper()),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )


def main():
    """Main CLI entry point."""
    parser = argparse.ArgumentParser(
        description="Lucid IT Agent - AI-powered IT helpdesk automation"
    )
    
    parser.add_argument(
        "--admin-url",
        default=os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000"),
        help="Admin Portal URL (default: $ADMIN_PORTAL_URL or http://localhost:5000)",
    )
    
    parser.add_argument(
        "--agent-name",
        default=os.environ.get("AGENT_NAME", ""),
        required=not os.environ.get("AGENT_NAME"),
        help="Agent name to run (default: $AGENT_NAME)",
    )
    
    parser.add_argument(
        "--poll-interval",
        type=int,
        default=int(os.environ.get("POLL_INTERVAL", "30")),
        help="Seconds between queue polls (default: 30)",
    )
    
    parser.add_argument(
        "--log-level",
        default=os.environ.get("LOG_LEVEL", "INFO"),
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        help="Log level (default: INFO)",
    )
    
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Load configuration but don't start processing",
    )
    
    args = parser.parse_args()
    
    setup_logging(args.log_level)
    logger = logging.getLogger(__name__)
    
    logger.info(f"Lucid IT Agent starting...")
    logger.info(f"  Admin Portal: {args.admin_url}")
    logger.info(f"  Agent Name: {args.agent_name}")
    logger.info(f"  Poll Interval: {args.poll_interval}s")
    
    if args.dry_run:
        logger.info("Dry run mode - will load config and exit")
        from .runner import AgentRunner
        runner = AgentRunner(args.admin_url, args.agent_name)
        asyncio.run(runner.initialize())
        logger.info("Configuration loaded successfully. Exiting.")
        return 0
    
    try:
        asyncio.run(run_agent(
            admin_portal_url=args.admin_url,
            agent_name=args.agent_name,
            poll_interval=args.poll_interval,
        ))
    except KeyboardInterrupt:
        logger.info("Interrupted by user")
    except Exception as e:
        logger.error(f"Agent failed: {e}", exc_info=True)
        return 1
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
Task 7: Update runtime/init.py
Update agent/src/agent/runtime/__init__.py:
python"""Lucid IT Agent workflow runtime."""
from .models import (
    AgentExport,
    AgentExportInfo,
    WorkflowExportInfo,
    WorkflowStepExportInfo,
    WorkflowTransitionExportInfo,
    RulesetExportInfo,
    RuleExportInfo,
    ExampleSetExportInfo,
    ExampleExportInfo,
    ProviderExportInfo,
    CredentialReference,
    StepType,
)
from .config_loader import ConfigLoader, ConfigurationError
from .execution_context import ExecutionContext, StepResult, ExecutionStatus
from .condition_evaluator import ConditionEvaluator
from .workflow_engine import WorkflowEngine
from .runner import AgentRunner, run_agent
from .integrations import (
    ServiceNowClient,
    ServiceNowCredentials,
    Ticket,
    create_prompt_driver,
    CapabilityRouter,
)

__all__ = [
    # Models
    "AgentExport",
    "AgentExportInfo",
    "WorkflowExportInfo",
    "WorkflowStepExportInfo",
    "WorkflowTransitionExportInfo",
    "RulesetExportInfo",
    "RuleExportInfo",
    "ExampleSetExportInfo",
    "ExampleExportInfo",
    "ProviderExportInfo",
    "CredentialReference",
    "StepType",
    # Core
    "ConfigLoader",
    "ConfigurationError",
    "ExecutionContext",
    "StepResult",
    "ExecutionStatus",
    "ConditionEvaluator",
    "WorkflowEngine",
    # Runner
    "AgentRunner",
    "run_agent",
    # Integrations
    "ServiceNowClient",
    "ServiceNowCredentials",
    "Ticket",
    "create_prompt_driver",
    "CapabilityRouter",
]
Task 8: Update pyproject.toml
Add CLI entry point to agent/pyproject.toml:
toml[project.scripts]
lucid-agent = "agent.runtime.cli:main"
Also ensure these dependencies are present:
toml[project]
dependencies = [
    "griptape>=1.8.0",
    "httpx>=0.25.0",
    "pydantic>=2.0",
    "python-dotenv>=1.0",
]
Task 9: Create Integration Tests
Create agent/tests/runtime/test_integrations.py:
python"""Tests for integration modules."""
import pytest
from unittest.mock import AsyncMock, MagicMock, patch
import os

from agent.runtime.integrations.servicenow_client import (
    ServiceNowClient,
    ServiceNowCredentials,
    Ticket,
)
from agent.runtime.integrations.driver_factory import (
    create_prompt_driver,
    resolve_credential,
    DriverFactoryError,
)
from agent.runtime.integrations.capability_router import (
    CapabilityRouter,
    NoCapableServerError,
)
from agent.runtime.models import ProviderExportInfo, CredentialReference


class TestServiceNowClient:
    def test_ticket_from_dict(self):
        data = {
            "sys_id": "abc123",
            "number": "INC0001234",
            "short_description": "Password reset",
            "description": "User forgot password",
            "caller_id": {"value": "user123"},
            "state": "1",
            "assignment_group": {"value": "group123"},
        }
        
        ticket = Ticket.from_dict(data)
        
        assert ticket.sys_id == "abc123"
        assert ticket.number == "INC0001234"
        assert ticket.caller_id == "user123"
    
    def test_ticket_to_ticket_data(self):
        ticket = Ticket(
            sys_id="abc123",
            number="INC0001234",
            short_description="Test",
            description="Test desc",
            caller_id="user123",
            state="1",
            assignment_group="group123",
        )
        
        data = ticket.to_ticket_data()
        
        assert data["number"] == "INC0001234"
        assert data["short_description"] == "Test"


class TestDriverFactory:
    def test_resolve_credential_environment(self):
        ref = CredentialReference(
            storage="environment",
            username_key="TEST_USER",
            password_key="TEST_PASS",
        )
        
        with patch.dict(os.environ, {"TEST_USER": "myuser", "TEST_PASS": "mypass"}):
            creds = resolve_credential(ref)
        
        assert creds["username"] == "myuser"
        assert creds["password"] == "mypass"
    
    def test_resolve_credential_none(self):
        ref = CredentialReference(storage="none")
        creds = resolve_credential(ref)
        assert creds == {}
    
    def test_create_ollama_driver(self):
        provider = ProviderExportInfo(
            name="test-llm",
            provider_type="llm-ollama",
            provider_config={
                "model": "llama3.1",
                "endpoint": "http://localhost:11434",
            },
        )
        
        # This will fail if Ollama driver not installed, which is OK for test
        try:
            driver = create_prompt_driver(provider)
            assert driver is not None
        except ImportError:
            pytest.skip("Ollama driver not installed")
    
    def test_create_unknown_driver_raises(self):
        provider = ProviderExportInfo(
            name="test-llm",
            provider_type="llm-unknown",
            provider_config={},
        )
        
        with pytest.raises(DriverFactoryError) as exc_info:
            create_prompt_driver(provider)
        
        assert "Unsupported" in str(exc_info.value)


class TestCapabilityRouter:
    @pytest.mark.asyncio
    async def test_no_servers_raises(self):
        router = CapabilityRouter("http://localhost:5000")
        
        with patch("httpx.AsyncClient.get") as mock_get:
            mock_response = MagicMock()
            mock_response.status_code = 404
            mock_get.return_value.__aenter__.return_value.get = AsyncMock(return_value=mock_response)
            
            with pytest.raises(NoCapableServerError):
                await router.get_server_for_capability("nonexistent")
    
    def test_cache_clear(self):
        router = CapabilityRouter("http://localhost:5000")
        router._cache["test"] = MagicMock()
        
        router.clear_cache()
        
        assert len(router._cache) == 0
Task 10: Update Workflow Engine for Integration
Update agent/src/agent/runtime/workflow_engine.py to support ServiceNow client and capability router injection:
Add these instance variables after __init__:
python# Integration points (set by runner)
self.servicenow_client: ServiceNowClient | None = None
self.capability_router: CapabilityRouter | None = None
And update execute() to pass these to context:
pythonasync def execute(
    self,
    ticket_id: str,
    ticket_data: dict[str, Any],
) -> ExecutionContext:
    """Execute workflow for a ticket."""
    context = ExecutionContext(
        ticket_id=ticket_id,
        ticket_data=ticket_data,
        llm_driver=self._llm_driver,
        admin_portal_url=self._admin_portal_url,
    )
    
    # Inject integrations
    context.servicenow_client = self.servicenow_client
    context.capability_router = self.capability_router
    
    # ... rest of existing execute code ...
Also update ExecutionContext in execution_context.py to include these optional attributes:
python@dataclass
class ExecutionContext:
    # ... existing fields ...
    
    # Integration points (optional, set by runner)
    servicenow_client: Any = None  # ServiceNowClient
    capability_router: Any = None  # CapabilityRouter
Verification
bashcd /home/alton/Documents/lucid-it-agent/agent

# Install in development mode
pip install -e ".[dev]"

# Run all tests
pytest tests/runtime/ -v

# Test CLI help
python -m agent.runtime.cli --help

# Test dry run (requires Admin Portal running)
python -m agent.runtime.cli --agent-name test-agent --dry-run

# Verify imports
python -c "
from agent.runtime import (
    AgentRunner, run_agent,
    ServiceNowClient, ServiceNowCredentials,
    create_prompt_driver, CapabilityRouter
)
print('All integrations imported successfully!')
"
Summary
Phase 4B-3 integrates the workflow runtime with:
ComponentPurposeKey MethodsServiceNowClientPoll tickets, update statepoll_queue(), update_ticket(), add_work_note()DriverFactoryCreate LLM driverscreate_prompt_driver(), resolve_credential()CapabilityRouterFind Tool Serversget_server_for_capability()AgentRunnerMain loop orchestrationinitialize(), run(), stop()CLICommand-line interfacelucid-agent --agent-name X
Next phase (4B-4) will perform end-to-end testing with real tickets.
