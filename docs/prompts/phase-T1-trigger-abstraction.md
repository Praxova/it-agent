# Claude Code Prompt: Phase T1 — Trigger Provider Abstraction

## Context

Read `/home/alton/Documents/lucid-it-agent/docs/adr/ADR-011-composable-workflows-pluggable-triggers.md` for the full architecture vision. This is Phase 1 (T1): extracting a `TriggerProvider` interface so the agent runner is no longer hardcoded to ServiceNow.

**This is a pure refactoring task.** No new user-facing functionality. All existing behavior must be preserved exactly. The agent should continue to poll ServiceNow and process tickets identically to today — the only change is structural.

**Project Location**: `/home/alton/Documents/lucid-it-agent`
**Agent Code Location**: `/home/alton/Documents/lucid-it-agent/agent/src/agent/runtime`

## What Exists Today

The `AgentRunner` in `runner.py` has ServiceNow logic directly embedded in `_poll_and_process()` and `_process_ticket()`:

1. `_poll_and_process()` reads the assignment group from the export config, calls `self._snow_client.poll_queue()`, and iterates over returned tickets
2. `_process_ticket()` calls `self._snow_client.set_state()` to acknowledge, runs `self._engine.execute()`, then calls various ServiceNow methods to post results

The `WorkflowEngine.execute()` already takes generic `ticket_id: str` and `ticket_data: dict` — it does NOT care where the data came from. The ServiceNow coupling is entirely in `AgentRunner`.

The `ServiceNowClient` in `integrations/servicenow_client.py` has a `Ticket` dataclass with a `to_ticket_data()` method that converts to a generic dict.

## Goal

Extract the ServiceNow-specific logic from `AgentRunner` into a `ServiceNowTriggerProvider` class that implements a `TriggerProvider` abstract interface. The runner should work with any `TriggerProvider` implementation, selected based on the workflow's trigger type from the export config.

Also create a `ManualTriggerProvider` stub — this will be the first new trigger type (implemented in Phase T2), but the class skeleton and registration should exist now.

## File Structure

Create these new files:

```
agent/src/agent/runtime/
├── triggers/                     # NEW directory
│   ├── __init__.py
│   ├── base.py                   # TriggerProvider ABC + WorkItem model
│   ├── servicenow_provider.py    # ServiceNowTriggerProvider
│   ├── manual_provider.py        # ManualTriggerProvider (stub)
│   └── registry.py               # TriggerProviderRegistry
```

Modify these existing files:
- `runner.py` — Refactor to use TriggerProvider instead of direct ServiceNow calls
- `models.py` — Add `TriggerType` to the `AgentExport` or leverage existing `trigger_type` field on `WorkflowExportInfo`

## Task 1: Create WorkItem and TriggerProvider (triggers/base.py)

```python
"""Base classes for trigger providers."""
from __future__ import annotations
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

from ..execution_context import ExecutionContext


class TriggerType(str, Enum):
    """Supported trigger types."""
    SERVICENOW = "servicenow"
    MANUAL = "manual"
    WEBHOOK = "webhook"  # Future
    EMAIL = "email"      # Future
    JIRA = "jira"        # Future


@dataclass
class WorkItem:
    """
    Universal work item from any trigger source.
    
    This is the common interface between trigger providers and the workflow engine.
    Each trigger provider maps its source-specific data into this normalized format.
    The `data` dict is what gets passed to engine.execute() as ticket_data.
    """
    id: str                          # Unique ID in source system (sys_id, issue key, etc.)
    source_type: TriggerType         # Where this came from
    data: dict[str, Any]             # Source-specific fields (becomes ticket_data)
    raw: Any = None                  # Original object from source API (for advanced use)
    
    # Normalized fields (same regardless of source)
    title: str = ""                  # short_description / summary / subject
    description: str = ""            # description / body / content  
    requester: str | None = None     # caller / reporter / from
    priority: str | None = None      # priority level
    created_at: datetime | None = None
    
    # Display identifier (ticket number, issue key, etc.)
    display_id: str = ""             # INC0010001, PROJ-123, etc.
    
    def __post_init__(self):
        if not self.display_id:
            self.display_id = self.id


class TriggerProvider(ABC):
    """
    Abstract interface for trigger providers.
    
    A trigger provider is responsible for:
    1. Discovering new work items from a source system
    2. Acknowledging that work has been picked up
    3. Reporting completion/failure/escalation back to the source
    
    The provider is created by the AgentRunner based on the workflow's
    declared trigger type and the agent's configured service accounts.
    """
    
    @property
    @abstractmethod
    def trigger_type(self) -> TriggerType:
        """The type of trigger this provider handles."""
        ...
    
    @property
    @abstractmethod
    def display_name(self) -> str:
        """Human-readable name for logging."""
        ...
    
    @abstractmethod
    async def poll(self) -> list[WorkItem]:
        """
        Check for new work items.
        
        For polling sources (ServiceNow, Jira, Email): query for new items.
        For push sources (Webhook, Manual): return items from internal queue.
        
        Returns:
            List of new work items to process. Empty list if none available.
        """
        ...
    
    @abstractmethod
    async def acknowledge(self, item: WorkItem) -> None:
        """
        Acknowledge that a work item has been picked up for processing.
        
        Typically: set status to "In Progress", add a work note, etc.
        Called BEFORE workflow execution begins.
        """
        ...
    
    @abstractmethod
    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """
        Report successful completion of a work item.
        
        Typically: set status to "Resolved", add completion notes, etc.
        Called when workflow execution completes successfully.
        """
        ...
    
    @abstractmethod
    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """
        Report that a work item was escalated to a human.
        
        Typically: add escalation notes, leave in an appropriate state.
        Called when workflow execution results in escalation.
        """
        ...
    
    @abstractmethod
    async def fail(self, item: WorkItem, error: str) -> None:
        """
        Report that processing of a work item failed.
        
        Typically: add error notes, leave accessible for retry/human review.
        Called when workflow execution fails unexpectedly.
        """
        ...
    
    async def startup(self) -> None:
        """
        Optional: Called when the agent starts up.
        
        Use for one-time initialization (e.g., starting an HTTP server for webhooks).
        Default implementation does nothing.
        """
        pass
    
    async def shutdown(self) -> None:
        """
        Optional: Called when the agent shuts down.
        
        Use for cleanup (e.g., stopping HTTP server, closing connections).
        Default implementation does nothing.
        """
        pass
```

## Task 2: Create ServiceNowTriggerProvider (triggers/servicenow_provider.py)

This extracts the ServiceNow-specific logic currently in `AgentRunner._poll_and_process()` and `AgentRunner._process_ticket()`.

```python
"""ServiceNow trigger provider."""
from __future__ import annotations
import logging
from typing import Any

from .base import TriggerProvider, TriggerType, WorkItem
from ..execution_context import ExecutionContext, ExecutionStatus
from ..integrations.servicenow_client import ServiceNowClient, Ticket

logger = logging.getLogger(__name__)


class ServiceNowTriggerProvider(TriggerProvider):
    """
    Trigger provider that polls ServiceNow for new incidents.
    
    This is the extraction of logic previously embedded in AgentRunner.
    """
    
    def __init__(
        self,
        client: ServiceNowClient,
        assignment_group: str,
        poll_limit: int = 5,
    ):
        self._client = client
        self._assignment_group = assignment_group
        self._poll_limit = poll_limit
    
    @property
    def trigger_type(self) -> TriggerType:
        return TriggerType.SERVICENOW
    
    @property
    def display_name(self) -> str:
        return f"ServiceNow ({self._assignment_group})"
    
    async def poll(self) -> list[WorkItem]:
        """Poll ServiceNow for new tickets in the assignment group."""
        if not self._assignment_group:
            logger.warning("No assignment group configured on ServiceNow provider")
            return []
        
        tickets = await self._client.poll_queue(
            assignment_group=self._assignment_group,
            state="1",  # New
            limit=self._poll_limit,
        )
        
        return [self._ticket_to_work_item(t) for t in tickets]
    
    async def acknowledge(self, item: WorkItem) -> None:
        """Set ticket to In Progress and add work note."""
        ticket = self._get_ticket(item)
        await self._client.set_state(ticket.sys_id, "2")  # In Progress
        await self._client.add_work_note(
            ticket.sys_id,
            "Ticket picked up by Lucid IT Agent for automated processing."
        )
    
    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """Post completion notes and resolve the ticket."""
        ticket = self._get_ticket(item)
        
        try:
            if not context.get_variable("ticket_updated"):
                success_note = self._build_completion_note(context)
                await self._client.add_work_note(ticket.sys_id, success_note)
                await self._client.set_state(
                    ticket.sys_id, "6",  # Resolved
                    close_notes=f"Resolved by Lucid IT Agent - {context.get_variable('ticket_type', 'automated')}"
                )
        except Exception as e:
            logger.error(f"Failed to write completion notes for {item.display_id}: {e}")
    
    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """Add escalation notes. Leave ticket in In Progress for human pickup."""
        ticket = self._get_ticket(item)
        
        try:
            escalation_notes = context.get_variable("escalation_work_notes")
            if not escalation_notes:
                escalation_notes = (
                    f"Lucid IT Agent escalation:\n"
                    f"Reason: {context.escalation_reason}\n"
                    f"Ticket Type: {context.get_variable('ticket_type', 'Unknown')}\n"
                    f"Confidence: {context.get_variable('confidence', 'N/A')}"
                )
            await self._client.add_work_note(ticket.sys_id, escalation_notes)
        except Exception as e:
            logger.error(f"Failed to write escalation notes for {item.display_id}: {e}")
    
    async def fail(self, item: WorkItem, error: str) -> None:
        """Add error notes to the ticket."""
        ticket = self._get_ticket(item)
        
        try:
            await self._client.add_work_note(
                ticket.sys_id,
                f"Automated processing failed: {error}\nEscalating to human operator."
            )
        except Exception as e:
            logger.error(f"Failed to write error notes for {item.display_id}: {e}")
    
    def _ticket_to_work_item(self, ticket: Ticket) -> WorkItem:
        """Convert a ServiceNow Ticket to a generic WorkItem."""
        return WorkItem(
            id=ticket.sys_id,
            source_type=TriggerType.SERVICENOW,
            data=ticket.to_ticket_data(),
            raw=ticket,
            title=ticket.short_description,
            description=ticket.description,
            requester=ticket.caller_id,
            display_id=ticket.number,
        )
    
    def _get_ticket(self, item: WorkItem) -> Ticket:
        """Extract the original Ticket from a WorkItem."""
        if isinstance(item.raw, Ticket):
            return item.raw
        # Fallback: reconstruct minimal ticket from work item data
        return Ticket(
            sys_id=item.id,
            number=item.display_id,
            short_description=item.title,
            description=item.description,
            caller_id=item.requester or "",
            state="",
            assignment_group="",
        )
    
    def _build_completion_note(self, context: ExecutionContext) -> str:
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
        
        exec_msg = context.get_step_output("execute-reset", "message")
        if exec_msg:
            lines.extend(["", f"Result: {exec_msg}"])
        
        return "\n".join(lines)
```

## Task 3: Create ManualTriggerProvider Stub (triggers/manual_provider.py)

This is a skeleton for Phase T2. It should be a valid class that can be instantiated but returns empty results from poll().

```python
"""Manual trigger provider — submit work items via Admin Portal or API."""
from __future__ import annotations
import asyncio
import logging
from typing import Any

from .base import TriggerProvider, TriggerType, WorkItem
from ..execution_context import ExecutionContext

logger = logging.getLogger(__name__)


class ManualTriggerProvider(TriggerProvider):
    """
    Trigger provider for manually submitted work items.
    
    Work items are pushed into an internal queue via submit_work_item().
    This will be called from an API endpoint or the Admin Portal UI in Phase T2.
    
    For now, this is a stub that returns no items from poll().
    """
    
    def __init__(self):
        self._pending_items: list[WorkItem] = []
        self._lock = asyncio.Lock()
    
    @property
    def trigger_type(self) -> TriggerType:
        return TriggerType.MANUAL
    
    @property
    def display_name(self) -> str:
        return "Manual Trigger"
    
    async def poll(self) -> list[WorkItem]:
        """Return any pending manually-submitted items."""
        async with self._lock:
            items = self._pending_items.copy()
            return items
    
    async def acknowledge(self, item: WorkItem) -> None:
        """Remove item from pending queue."""
        async with self._lock:
            self._pending_items = [i for i in self._pending_items if i.id != item.id]
        logger.info(f"Manual work item {item.display_id} acknowledged")
    
    async def complete(self, item: WorkItem, context: ExecutionContext) -> None:
        """Log completion. Phase T2 will add result storage/callback."""
        logger.info(f"Manual work item {item.display_id} completed successfully")
    
    async def escalate(self, item: WorkItem, context: ExecutionContext) -> None:
        """Log escalation. Phase T2 will add result storage/callback."""
        logger.info(
            f"Manual work item {item.display_id} escalated: {context.escalation_reason}"
        )
    
    async def fail(self, item: WorkItem, error: str) -> None:
        """Log failure. Phase T2 will add result storage/callback."""
        logger.warning(f"Manual work item {item.display_id} failed: {error}")
    
    async def submit_work_item(
        self,
        title: str,
        description: str,
        requester: str | None = None,
        extra_data: dict[str, Any] | None = None,
    ) -> WorkItem:
        """
        Submit a work item manually.
        
        This will be called by an API endpoint in Phase T2.
        
        Args:
            title: Short description of the request
            description: Full description
            requester: Who is requesting (optional)
            extra_data: Additional fields to include in ticket_data
            
        Returns:
            The created WorkItem
        """
        import uuid
        
        item_id = str(uuid.uuid4())
        display_id = f"MANUAL-{item_id[:8].upper()}"
        
        data = {
            "short_description": title,
            "description": description,
            "caller_id": requester or "manual",
            "number": display_id,
            "sys_id": item_id,
            **(extra_data or {}),
        }
        
        item = WorkItem(
            id=item_id,
            source_type=TriggerType.MANUAL,
            data=data,
            title=title,
            description=description,
            requester=requester,
            display_id=display_id,
        )
        
        async with self._lock:
            self._pending_items.append(item)
        
        logger.info(f"Manual work item submitted: {display_id}")
        return item
```

## Task 4: Create Trigger Registry (triggers/registry.py)

```python
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
            return ManualTriggerProvider()
        
        else:
            raise ValueError(
                f"Trigger type '{trigger_type}' is not yet implemented. "
                f"Supported types: {[t.value for t in TriggerType if t in (TriggerType.SERVICENOW, TriggerType.MANUAL)]}"
            )
```

## Task 5: Create triggers/__init__.py

```python
"""Trigger providers for the Lucid IT Agent workflow runtime."""
from .base import TriggerProvider, TriggerType, WorkItem
from .servicenow_provider import ServiceNowTriggerProvider
from .manual_provider import ManualTriggerProvider
from .registry import TriggerProviderFactory

__all__ = [
    "TriggerProvider",
    "TriggerType",
    "WorkItem",
    "ServiceNowTriggerProvider",
    "ManualTriggerProvider",
    "TriggerProviderFactory",
]
```

## Task 6: Refactor AgentRunner (runner.py)

This is the critical task. Refactor `AgentRunner` to use `TriggerProvider` instead of direct ServiceNow calls. **Preserve all existing behavior exactly.**

Key changes:
1. `initialize()` — After creating the ServiceNow client, create the appropriate `TriggerProvider` via `TriggerProviderFactory`
2. `_poll_and_process()` — Replace direct ServiceNow polling with `self._trigger.poll()`
3. `_process_ticket()` — Replace with `_process_work_item()` that uses trigger provider methods
4. Keep all stats tracking, heartbeat, config change detection, etc. unchanged

Here's the refactored structure for the key methods:

```python
# In __init__, add:
self._trigger: TriggerProvider | None = None

# In initialize(), after creating snow_client and before creating engine:
# Determine trigger type from workflow
trigger_type = "servicenow"  # default for backward compatibility
if export.workflow and export.workflow.trigger_type:
    trigger_type = export.workflow.trigger_type

# Create trigger provider
self._trigger = TriggerProviderFactory.create(
    trigger_type=trigger_type,
    snow_client=self._snow_client,
    assignment_group=assignment_group,  # extracted from export.service_now
)
logger.info(f"Created trigger provider: {self._trigger.display_name}")

# Call startup
await self._trigger.startup()

# In _poll_and_process(), replace the entire body with:
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
        return
    
    logger.info(f"Found {len(items)} new work items")
    
    for item in items:
        await self._process_work_item(item)

# Replace _process_ticket with _process_work_item:
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
```

**IMPORTANT**: The refactored `_process_work_item()` must produce the SAME ServiceNow state changes and work notes as the current `_process_ticket()` method. The ServiceNowTriggerProvider's `complete()`, `escalate()`, and `fail()` methods contain the logic that was previously in `_process_ticket()`. Verify that no ServiceNow update logic is lost in the refactoring.

Also in the `run()` method, add cleanup:
```python
# In the finally block or after the while loop:
if self._trigger:
    await self._trigger.shutdown()
```

Also update the `_send_shutdown_heartbeat` to still work, and update `initialize()` so `assignment_group` is extracted before the trigger provider creation (it's currently extracted inside `_poll_and_process`, move it to `initialize()`).

## Task 7: Remove _build_completion_note from AgentRunner

The `_build_completion_note()` method should be REMOVED from `AgentRunner` since it now lives in `ServiceNowTriggerProvider._build_completion_note()`. Make sure it's not referenced anywhere else.

## Task 8: Update Imports

Update `agent/src/agent/runtime/__init__.py` to export the new trigger types:

```python
# Add to existing exports:
from .triggers import TriggerProvider, TriggerType, WorkItem, TriggerProviderFactory
```

## Verification Checklist

After the refactoring is complete, verify:

1. **No direct ServiceNow calls remain in runner.py** — All ServiceNow interaction goes through the trigger provider
2. **The `_snow_client` is still created in `initialize()`** — It's passed to the trigger provider factory, not used directly by the runner
3. **Stats tracking unchanged** — `_tickets_processed`, `_tickets_succeeded`, etc. still increment in the same scenarios
4. **Heartbeat unchanged** — Still reports to Admin Portal on the same schedule
5. **Config change detection unchanged** — Still checks every 5 polls and reinitializes if needed
6. **Enabled/disabled check unchanged** — Still queries Admin Portal for agent status
7. **The `capability_router` and `servicenow_client` are still injected into the engine** — Sub-executors (like UpdateTicketExecutor) may still need direct ServiceNow access
8. **Backward compatibility** — If `trigger_type` is null/empty in the export, default to ServiceNow
9. **No circular imports** — The triggers module imports from integrations and execution_context, not from runner

## What NOT To Change

- `workflow_engine.py` — No changes needed
- `execution_context.py` — No changes needed  
- `executors/` — No changes to any executor
- `integrations/servicenow_client.py` — No changes needed
- `config_loader.py` — No changes needed
- `cli.py` — No changes needed
- Any Admin Portal (.NET) code — No changes needed for this phase

## Commit Message

```
refactor: extract trigger provider interface from AgentRunner

- Create TriggerProvider ABC with poll/acknowledge/complete/escalate/fail
- Create WorkItem as universal work item model (replaces direct Ticket coupling)
- Extract ServiceNowTriggerProvider from runner's _poll_and_process/_process_ticket
- Add ManualTriggerProvider stub for Phase T2
- Add TriggerProviderFactory for type-based provider creation
- AgentRunner now works with any TriggerProvider implementation
- All existing ServiceNow behavior preserved exactly

Part of ADR-011: Composable Workflows & Pluggable Triggers (Phase T1)
```
