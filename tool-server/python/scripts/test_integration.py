"""Integration test for Password Reset Tool.

Tests the full stack:
1. Tool Server (FastAPI + ldap3)
2. Agent Tool (Griptape wrapper)

Requirements:
- Tool Server running on localhost:8000
- Active Directory accessible and configured in .env
- Test user exists in AD

Usage:
    # Terminal 1: Start Tool Server
    cd tool-server/python
    python -m tool_server.main

    # Terminal 2: Run integration test
    python scripts/test_integration.py
"""

import asyncio
import os
import sys

# Add parent directory to path for imports
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../.."))

from agent.tools import PasswordResetTool, ToolServerConfig


async def test_health_check():
    """Test health check endpoint."""
    print("\n" + "=" * 60)
    print("TEST 1: Health Check")
    print("=" * 60)

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = PasswordResetTool(config=config)

    try:
        result = await tool.check_health({"values": {}})
        print(f"✓ Health check result:\n{result.value}\n")
        return True
    except Exception as e:
        print(f"✗ Health check failed: {e}\n")
        return False


async def test_password_reset():
    """Test password reset with test user."""
    print("\n" + "=" * 60)
    print("TEST 2: Password Reset")
    print("=" * 60)

    # Get test user from environment (or use default)
    test_user = os.getenv("TEST_USER", "testuser1")
    test_password = "TempPass123!@#"

    print(f"Target user: {test_user}")
    print(f"New password: {test_password}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = PasswordResetTool(config=config)

    try:
        result = await tool.reset_password(
            {"values": {"username": test_user, "new_password": test_password}}
        )

        if "Password reset successful" in result.value:
            print(f"✓ Password reset succeeded:\n{result.value}\n")
            return True
        else:
            print(f"✗ Password reset failed:\n{result.value}\n")
            return False

    except Exception as e:
        print(f"✗ Password reset error: {e}\n")
        return False


async def test_nonexistent_user():
    """Test password reset with non-existent user (should fail gracefully)."""
    print("\n" + "=" * 60)
    print("TEST 3: Non-Existent User (Expected Failure)")
    print("=" * 60)

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = PasswordResetTool(config=config)

    try:
        result = await tool.reset_password(
            {"values": {"username": "nonexistent_user_12345", "new_password": "Pass123!"}}
        )

        # Should be an error artifact
        if "not found" in result.value.lower() or "failed" in result.value.lower():
            print(f"✓ Correctly handled non-existent user:\n{result.value}\n")
            return True
        else:
            print(f"✗ Unexpected result:\n{result.value}\n")
            return False

    except Exception as e:
        print(f"✓ Exception raised as expected: {e}\n")
        return True


async def main():
    """Run all integration tests."""
    print("\n" + "=" * 60)
    print("PASSWORD RESET TOOL - INTEGRATION TEST")
    print("=" * 60)
    print("\nThis test requires:")
    print("1. Tool Server running on http://localhost:8000")
    print("2. Active Directory accessible")
    print("3. Test user exists (set TEST_USER env var)")
    print("\nStarting tests...\n")

    results = []

    # Test 1: Health check
    results.append(await test_health_check())

    # Test 2: Password reset
    results.append(await test_password_reset())

    # Test 3: Non-existent user
    results.append(await test_nonexistent_user())

    # Summary
    print("\n" + "=" * 60)
    print("TEST SUMMARY")
    print("=" * 60)
    passed = sum(results)
    total = len(results)
    print(f"\nPassed: {passed}/{total}")
    print(f"Failed: {total - passed}/{total}\n")

    if passed == total:
        print("✓ All tests passed!")
        return 0
    else:
        print("✗ Some tests failed")
        return 1


if __name__ == "__main__":
    exit_code = asyncio.run(main())
    sys.exit(exit_code)
