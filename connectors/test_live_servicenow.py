#!/usr/bin/env python3
"""Test script for verifying ServiceNow connector against live PDI instance.

This script tests basic connectivity and operations against your ServiceNow PDI.
Run with: python connectors/test_live_servicenow.py
"""

import asyncio
import logging
import os
import sys
from datetime import datetime, timedelta

from dotenv import load_dotenv

# Add parent directory to path so we can import connectors
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from connectors import ServiceNowConnector

# Set up logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


async def test_health_check(connector: ServiceNowConnector) -> bool:
    """Test basic connectivity to ServiceNow."""
    logger.info("Testing health check...")
    try:
        is_healthy = await connector.client.health_check()
        if is_healthy:
            logger.info("✓ Health check passed - ServiceNow is reachable")
            return True
        else:
            logger.error("✗ Health check failed - ServiceNow is not reachable")
            return False
    except Exception as e:
        logger.error(f"✗ Health check failed with error: {e}")
        return False


async def test_poll_queue(connector: ServiceNowConnector) -> bool:
    """Test polling the incident queue."""
    logger.info("Testing queue polling...")
    try:
        # Poll for tickets updated in the last 30 days
        since = datetime.now() - timedelta(days=30)
        tickets = await connector.poll_queue(since=since)

        logger.info(f"✓ Successfully polled queue - Found {len(tickets)} open tickets")

        if tickets:
            logger.info("\nFirst few tickets:")
            for ticket in tickets[:3]:  # Show first 3
                logger.info(f"  - {ticket.number}: {ticket.short_description}")
                logger.info(f"    State: {ticket.state.name}, Priority: {ticket.priority}")
                logger.info(f"    Caller: {ticket.caller_username}")
        else:
            logger.info("  No open tickets found in queue")

        return True
    except Exception as e:
        logger.error(f"✗ Queue polling failed with error: {e}")
        return False


async def test_get_ticket(connector: ServiceNowConnector) -> bool:
    """Test fetching a specific ticket (if any exist)."""
    logger.info("\nTesting get_ticket...")
    try:
        # First poll to get a ticket ID
        tickets = await connector.poll_queue()

        if not tickets:
            logger.info("  ⊘ Skipping - no tickets available to test")
            return True

        # Get the first ticket details
        ticket_id = tickets[0].id
        ticket = await connector.get_ticket(ticket_id)

        logger.info(f"✓ Successfully fetched ticket {ticket.number}")
        logger.info(f"  Short description: {ticket.short_description}")
        logger.info(f"  Description: {ticket.description or '(none)'}")
        logger.info(f"  State: {ticket.state.name}")
        logger.info(f"  Priority: {ticket.priority}")
        logger.info(f"  Created: {ticket.created_at}")
        logger.info(f"  Updated: {ticket.updated_at}")

        return True
    except Exception as e:
        logger.error(f"✗ Get ticket failed with error: {e}")
        return False


async def test_add_work_note(connector: ServiceNowConnector, dry_run: bool = True) -> bool:
    """Test adding a work note to a ticket."""
    logger.info("\nTesting add_work_note...")

    if dry_run:
        logger.info("  ⊘ Skipping (dry run mode) - would add work note to a ticket")
        return True

    try:
        # First poll to get a ticket ID
        tickets = await connector.poll_queue()

        if not tickets:
            logger.info("  ⊘ Skipping - no tickets available to test")
            return True

        # Add work note to first ticket
        ticket_id = tickets[0].id
        test_note = f"[TEST] ServiceNow connector test at {datetime.now().isoformat()}"

        await connector.add_work_note(ticket_id, test_note)
        logger.info(f"✓ Successfully added work note to ticket {tickets[0].number}")

        return True
    except Exception as e:
        logger.error(f"✗ Add work note failed with error: {e}")
        return False


async def main():
    """Run all tests against live ServiceNow instance."""
    # Load environment variables
    load_dotenv()

    # Get configuration from environment
    instance = os.getenv("SERVICENOW_INSTANCE")
    username = os.getenv("SERVICENOW_USERNAME")
    password = os.getenv("SERVICENOW_PASSWORD")
    assignment_group = os.getenv("SERVICENOW_ASSIGNMENT_GROUP", "Helpdesk")

    # Validate configuration
    if not all([instance, username, password]):
        logger.error("Missing required environment variables!")
        logger.error("Please ensure .env file contains:")
        logger.error("  - SERVICENOW_INSTANCE")
        logger.error("  - SERVICENOW_USERNAME")
        logger.error("  - SERVICENOW_PASSWORD")
        sys.exit(1)

    logger.info("=" * 70)
    logger.info("ServiceNow Connector Live Test")
    logger.info("=" * 70)
    logger.info(f"Instance: {instance}")
    logger.info(f"Username: {username}")
    logger.info(f"Assignment Group: {assignment_group}")
    logger.info("=" * 70)

    # Create connector
    async with ServiceNowConnector(
        instance=instance,
        username=username,
        password=password,
        assignment_group=assignment_group,
    ) as connector:
        results = []

        # Run tests
        results.append(("Health Check", await test_health_check(connector)))
        results.append(("Poll Queue", await test_poll_queue(connector)))
        results.append(("Get Ticket", await test_get_ticket(connector)))
        results.append(("Add Work Note", await test_add_work_note(connector, dry_run=True)))

        # Summary
        logger.info("\n" + "=" * 70)
        logger.info("Test Summary")
        logger.info("=" * 70)

        passed = sum(1 for _, result in results if result)
        total = len(results)

        for test_name, result in results:
            status = "✓ PASS" if result else "✗ FAIL"
            logger.info(f"{status:8} {test_name}")

        logger.info("=" * 70)
        logger.info(f"Results: {passed}/{total} tests passed")
        logger.info("=" * 70)

        if passed == total:
            logger.info("\n🎉 All tests passed! ServiceNow connector is working correctly.")
            return 0
        else:
            logger.error(f"\n⚠️  {total - passed} test(s) failed. Check the logs above for details.")
            return 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
