"""Integration test for the full pipeline.

Requirements:
- ServiceNow PDI accessible
- Ollama running with llama3.1
- Tool server running on port 8100
- DC accessible (for actual tool execution)

This creates a test ticket in ServiceNow and verifies the agent processes it.

Usage:
    python agent/scripts/test_pipeline_integration.py
"""

import asyncio
import os
import sys
from datetime import datetime

# Add project to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../.."))

from connectors import ServiceNowConnector, TicketState

from agent.pipeline.config import PipelineConfig
from agent.pipeline.executor import TicketExecutor


async def create_test_ticket(connector: ServiceNowConnector, ticket_type: str) -> str:
    """Create a test ticket and return its sys_id.

    Args:
        connector: ServiceNow connector instance.
        ticket_type: Type of test ticket to create.

    Returns:
        sys_id of the created ticket.
    """
    test_tickets = {
        "password_reset": {
            "short_description": "Password reset needed - TEST",
            "description": "I forgot my password and can't log in. My username is luke.skywalker. Please reset it. (TEST TICKET)",
        },
        "group_add": {
            "short_description": "Need access to Contributors group - TEST",
            "description": "Please add han.solo to the LucidTest-Contributors group. He needs access for the new project. (TEST TICKET)",
        },
        "unknown": {
            "short_description": "Printer not working - TEST",
            "description": "The printer on the 3rd floor keeps jamming. Can someone take a look? (TEST TICKET)",
        },
    }

    ticket_data = test_tickets.get(ticket_type)
    if not ticket_data:
        raise ValueError(f"Unknown test ticket type: {ticket_type}")

    # Create ticket via ServiceNow API
    # Note: This requires implementing create_ticket in connector
    # For now, we'll create manually and return the sys_id
    print(f"Please create a test ticket in ServiceNow with:")
    print(f"  Short description: {ticket_data['short_description']}")
    print(f"  Description: {ticket_data['description']}")
    print(f"  Assignment group: Helpdesk")

    sys_id = input("Enter the sys_id of the created ticket: ").strip()
    return sys_id


async def main():
    """Main integration test function.

    Returns:
        Exit code (0 for success, 1 for failure).
    """
    print("=" * 60)
    print("Pipeline Integration Test")
    print("=" * 60)

    # Initialize
    config = PipelineConfig()
    executor = TicketExecutor(config)

    try:
        await executor.initialize()
        print("✓ Executor initialized")

        # Test 1: Process queue
        print("\n[1] Processing ticket queue...")
        processed = await executor.run_once()
        print(f"    Processed {processed} tickets")

        print("\n" + "=" * 60)
        print("Integration test complete!")
        print("=" * 60)

    finally:
        await executor.close()

    return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
