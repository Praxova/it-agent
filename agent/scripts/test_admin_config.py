#!/usr/bin/env python3
"""Test script for Admin Portal configuration integration.

Usage:
    # Set environment variables:
    export LUCID_ADMIN_URL=http://localhost:5000
    export LUCID_AGENT_NAME=test-agent
    export LUCID_API_KEY=lk_your_api_key_here  # Optional for now

    # Run the test:
    python scripts/test_admin_config.py
"""

import os
import sys
import logging

# Add src to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from agent.config.admin_client import AdminPortalClient

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)

logger = logging.getLogger(__name__)


def main():
    """Test the Admin Portal client."""
    print("\n" + "=" * 70)
    print("Admin Portal Configuration Test")
    print("=" * 70)

    # Check environment variables
    admin_url = os.environ.get("LUCID_ADMIN_URL")
    agent_name = os.environ.get("LUCID_AGENT_NAME")
    api_key = os.environ.get("LUCID_API_KEY")

    print(f"\nConfiguration:")
    print(f"  Admin Portal URL: {admin_url or '(not set)'}")
    print(f"  Agent Name: {agent_name or '(not set)'}")
    print(f"  API Key: {'(set)' if api_key else '(not set)'}")

    if not admin_url or not agent_name:
        print("\n❌ Missing required environment variables!")
        print("   Set LUCID_ADMIN_URL and LUCID_AGENT_NAME")
        return 1

    try:
        # Create client
        print("\n" + "-" * 70)
        print("Creating Admin Portal client...")
        client = AdminPortalClient()
        print(f"✓ Client created successfully")

        # Fetch configuration
        print("\n" + "-" * 70)
        print("Fetching agent configuration...")
        config = client.get_configuration()
        print(f"✓ Configuration retrieved successfully")

        # Display agent info
        print("\n" + "-" * 70)
        print("Agent Information:")
        print(f"  ID: {config.agent.id}")
        print(f"  Name: {config.agent.name}")
        print(f"  Display Name: {config.agent.display_name or '(not set)'}")
        print(f"  Enabled: {config.agent.is_enabled}")
        print(f"  Assignment Group: {config.assignment_group or '(not set)'}")

        # Display LLM provider info
        print("\n" + "-" * 70)
        print("LLM Provider:")
        print(f"  Service Account: {config.llm_provider.service_account_name}")
        print(f"  Provider Type: {config.llm_provider.provider_type}")
        print(f"  Account Type: {config.llm_provider.account_type}")
        print(f"  Model: {config.llm_provider.model or '(not configured)'}")
        print(f"  Base URL: {config.llm_provider.base_url or '(default)'}")
        print(f"  Temperature: {config.llm_provider.temperature}")
        print(f"  Has API Key: {bool(config.llm_provider.api_key)}")
        print(f"  Credentials: {list(config.llm_provider.credentials.keys())}")

        # Display ServiceNow info
        print("\n" + "-" * 70)
        print("ServiceNow:")
        print(f"  Service Account: {config.servicenow.service_account_name}")
        print(f"  Provider Type: {config.servicenow.provider_type}")
        print(f"  Account Type: {config.servicenow.account_type}")
        print(f"  Instance URL: {config.servicenow.instance_url or '(not configured)'}")
        print(f"  Username: {config.servicenow.username or '(not configured)'}")
        print(f"  Has Password: {bool(config.servicenow.password)}")
        print(f"  Credential Storage: {config.servicenow.credential_storage}")
        print(f"  Credentials: {list(config.servicenow.credentials.keys())}")

        # Test heartbeat
        print("\n" + "-" * 70)
        print("Testing heartbeat...")
        client.send_heartbeat(status="Running", tickets_processed=0)
        print(f"✓ Heartbeat sent successfully")

        # Close client
        client.close()

        print("\n" + "=" * 70)
        print("✓ All tests passed!")
        print("=" * 70)

        return 0

    except Exception as e:
        print("\n" + "=" * 70)
        print(f"✗ Error: {e}")
        print("=" * 70)

        import traceback
        print("\nTraceback:")
        traceback.print_exc()

        return 1


if __name__ == "__main__":
    sys.exit(main())
