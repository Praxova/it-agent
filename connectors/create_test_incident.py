#!/usr/bin/env python3
"""Create a test incident in ServiceNow for testing the connector.

This script creates a sample incident in your ServiceNow PDI instance
that can be used to test the full connector functionality.

Run with: python connectors/create_test_incident.py
"""

import asyncio
import logging
import os
import sys
from datetime import datetime

from dotenv import load_dotenv

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from connectors.servicenow.client import ServiceNowClient

# Set up logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


async def create_test_incident(client: ServiceNowClient) -> str | None:
    """Create a test incident in ServiceNow.

    Returns:
        Incident sys_id if successful, None otherwise.
    """
    try:
        # Create incident data
        incident_data = {
            "short_description": f"[TEST] Password reset request - {datetime.now().strftime('%Y-%m-%d %H:%M')}",
            "description": "User reports they forgot their password and cannot log in to their workstation. This is a test incident created by the Lucid IT Agent connector.",
            "caller_id": "admin",  # Using admin as caller for testing
            "assignment_group": "Helpdesk",
            "category": "inquiry",
            "urgency": "3",
            "impact": "3",
        }

        logger.info("Creating test incident...")
        logger.info(f"  Short description: {incident_data['short_description']}")
        logger.info(f"  Assignment group: {incident_data['assignment_group']}")

        # Make POST request to create incident
        response = await client._client.request(
            "POST",
            f"{client.base_url}/incident",
            params={"sysparm_display_value": "true"},
            json=incident_data,
        )
        response.raise_for_status()

        data = response.json()
        if isinstance(data, dict) and "result" in data:
            result = data["result"]
            sys_id = result["sys_id"]
            number = result["number"]

            logger.info(f"✓ Successfully created incident {number}")
            logger.info(f"  sys_id: {sys_id}")
            logger.info(f"  State: {result.get('state')}")

            return sys_id

        return None

    except Exception as e:
        logger.error(f"✗ Failed to create incident: {e}")
        return None


async def main():
    """Create test incident."""
    # Load environment variables
    load_dotenv()

    # Get configuration
    instance = os.getenv("SERVICENOW_INSTANCE")
    username = os.getenv("SERVICENOW_USERNAME")
    password = os.getenv("SERVICENOW_PASSWORD")

    if not all([instance, username, password]):
        logger.error("Missing required environment variables!")
        logger.error("Please ensure .env file contains SERVICENOW_* variables")
        sys.exit(1)

    logger.info("=" * 70)
    logger.info("Create Test Incident in ServiceNow")
    logger.info("=" * 70)
    logger.info(f"Instance: {instance}")
    logger.info("=" * 70)

    # Create client
    async with ServiceNowClient(instance, username, password) as client:
        sys_id = await create_test_incident(client)

        if sys_id:
            logger.info("\n" + "=" * 70)
            logger.info("Next Steps:")
            logger.info("=" * 70)
            logger.info("1. Run the live test script to see the incident:")
            logger.info("   python connectors/test_live_servicenow.py")
            logger.info("")
            logger.info("2. Or use the interactive example:")
            logger.info("   python connectors/example_usage.py")
            logger.info("=" * 70)
            return 0
        else:
            return 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
