#!/usr/bin/env python3
"""Interactive examples of using the ServiceNow connector.

This script demonstrates common usage patterns for the ServiceNow connector.
Run with: python connectors/example_usage.py
"""

import asyncio
import logging
import os
import sys
from datetime import datetime, timedelta

from dotenv import load_dotenv

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from connectors import ServiceNowConnector, TicketUpdate, TicketState

# Set up logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


async def example_1_poll_recent_tickets(connector: ServiceNowConnector):
    """Example 1: Poll for tickets updated in the last hour."""
    print("\n" + "=" * 70)
    print("Example 1: Poll for Recent Tickets")
    print("=" * 70)

    since = datetime.now() - timedelta(hours=1)
    tickets = await connector.poll_queue(since=since)

    print(f"Found {len(tickets)} tickets updated in the last hour\n")

    for ticket in tickets:
        print(f"Ticket: {ticket.number}")
        print(f"  Subject: {ticket.short_description}")
        print(f"  State: {ticket.state.name} ({ticket.state.value})")
        print(f"  Priority: {ticket.priority}")
        print(f"  Caller: {ticket.caller_username}")
        print(f"  Updated: {ticket.updated_at}")
        print()


async def example_2_get_ticket_details(connector: ServiceNowConnector):
    """Example 2: Get detailed information for a specific ticket."""
    print("\n" + "=" * 70)
    print("Example 2: Get Ticket Details")
    print("=" * 70)

    # First get any ticket
    tickets = await connector.poll_queue()

    if not tickets:
        print("No tickets available to demonstrate")
        return

    ticket_id = tickets[0].id
    ticket = await connector.get_ticket(ticket_id)

    print(f"Ticket: {ticket.number}")
    print(f"  ID: {ticket.id}")
    print(f"  Short Description: {ticket.short_description}")
    print(f"  Description: {ticket.description or '(none)'}")
    print(f"  State: {ticket.state.name}")
    print(f"  Priority: {ticket.priority}")
    print(f"  Caller: {ticket.caller_username}")
    print(f"  Assignment Group: {ticket.assignment_group}")
    print(f"  Created: {ticket.created_at}")
    print(f"  Updated: {ticket.updated_at}")


async def example_3_add_work_note(connector: ServiceNowConnector, dry_run: bool = True):
    """Example 3: Add a work note (internal comment) to a ticket."""
    print("\n" + "=" * 70)
    print("Example 3: Add Work Note")
    print("=" * 70)

    tickets = await connector.poll_queue()

    if not tickets:
        print("No tickets available to demonstrate")
        return

    ticket = tickets[0]
    work_note = f"[AGENT] Investigating issue - {datetime.now().isoformat()}"

    print(f"Ticket: {ticket.number}")
    print(f"  Work note: {work_note}")

    if dry_run:
        print("  (DRY RUN - not actually adding)")
    else:
        await connector.add_work_note(ticket.id, work_note)
        print("  ✓ Work note added")


async def example_4_add_comment(connector: ServiceNowConnector, dry_run: bool = True):
    """Example 4: Add a customer-visible comment to a ticket."""
    print("\n" + "=" * 70)
    print("Example 4: Add Customer Comment")
    print("=" * 70)

    tickets = await connector.poll_queue()

    if not tickets:
        print("No tickets available to demonstrate")
        return

    ticket = tickets[0]
    comment = "We are investigating your issue and will provide an update shortly."

    print(f"Ticket: {ticket.number}")
    print(f"  Comment: {comment}")

    if dry_run:
        print("  (DRY RUN - not actually adding)")
    else:
        await connector.add_comment(ticket.id, comment)
        print("  ✓ Comment added")


async def example_5_update_ticket_state(connector: ServiceNowConnector, dry_run: bool = True):
    """Example 5: Update ticket state to In Progress."""
    print("\n" + "=" * 70)
    print("Example 5: Update Ticket State")
    print("=" * 70)

    tickets = await connector.poll_queue()

    if not tickets:
        print("No tickets available to demonstrate")
        return

    ticket = tickets[0]

    print(f"Ticket: {ticket.number}")
    print(f"  Current state: {ticket.state.name}")
    print(f"  New state: IN_PROGRESS")

    if dry_run:
        print("  (DRY RUN - not actually updating)")
    else:
        update = TicketUpdate(
            state=TicketState.IN_PROGRESS,
            work_notes="Agent has started working on this ticket",
        )
        updated_ticket = await connector.update_ticket(ticket.id, update)
        print(f"  ✓ State updated to {updated_ticket.state.name}")


async def example_6_close_ticket(connector: ServiceNowConnector, dry_run: bool = True):
    """Example 6: Close a ticket with resolution notes."""
    print("\n" + "=" * 70)
    print("Example 6: Close Ticket")
    print("=" * 70)

    tickets = await connector.poll_queue()

    if not tickets:
        print("No tickets available to demonstrate")
        return

    ticket = tickets[0]
    resolution = "Password reset successfully. User can now log in."

    print(f"Ticket: {ticket.number}")
    print(f"  Current state: {ticket.state.name}")
    print(f"  Resolution: {resolution}")

    if dry_run:
        print("  (DRY RUN - not actually closing)")
    else:
        closed_ticket = await connector.close_ticket(ticket.id, resolution)
        print(f"  ✓ Ticket closed (state: {closed_ticket.state.name})")


async def main():
    """Run all examples."""
    # Load environment variables
    load_dotenv()

    instance = os.getenv("SERVICENOW_INSTANCE")
    username = os.getenv("SERVICENOW_USERNAME")
    password = os.getenv("SERVICENOW_PASSWORD")
    assignment_group = os.getenv("SERVICENOW_ASSIGNMENT_GROUP", "Helpdesk")

    if not all([instance, username, password]):
        logger.error("Missing required environment variables!")
        sys.exit(1)

    print("=" * 70)
    print("ServiceNow Connector Usage Examples")
    print("=" * 70)
    print(f"Instance: {instance}")
    print(f"Assignment Group: {assignment_group}")
    print("=" * 70)
    print("\nNOTE: Examples 3-6 are in DRY RUN mode and won't modify tickets.")
    print("      Set dry_run=False in the code to actually make changes.")
    print("=" * 70)

    # Create connector
    async with ServiceNowConnector(
        instance=instance,
        username=username,
        password=password,
        assignment_group=assignment_group,
    ) as connector:
        # Run examples
        await example_1_poll_recent_tickets(connector)
        await example_2_get_ticket_details(connector)
        await example_3_add_work_note(connector, dry_run=True)
        await example_4_add_comment(connector, dry_run=True)
        await example_5_update_ticket_state(connector, dry_run=True)
        await example_6_close_ticket(connector, dry_run=True)

        print("\n" + "=" * 70)
        print("Examples completed!")
        print("=" * 70)


if __name__ == "__main__":
    asyncio.run(main())
