"""Integration test for Group Management Tool.

Tests the full stack:
1. Tool Server (FastAPI + ldap3)
2. Agent Tool (Griptape wrapper)
3. Group management operations

Requirements:
- Tool Server running on localhost:8000
- Active Directory accessible and configured in .env
- Test user and test group exist in AD
- svc-lucid-agent account has group membership delegation

Environment Variables:
- TEST_USER: Username for testing (default: testuser1)
- TEST_GROUP: Group name for testing (default: Test-Group)
- TEST_TICKET: Ticket number for audit (default: INC0012345)

Usage:
    # Terminal 1: Start Tool Server
    cd tool-server/python
    python -m tool_server.main

    # Terminal 2: Run integration test
    python scripts/test_groups_integration.py
"""

import asyncio
import os
import sys

# Add parent directory to path for imports
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../.."))

from agent.tools import GroupManagementTool, ToolServerConfig


async def test_get_group_info():
    """Test getting group information."""
    print("\n" + "=" * 60)
    print("TEST 1: Get Group Information")
    print("=" * 60)

    test_group = os.getenv("TEST_GROUP", "Test-Group")
    print(f"Target group: {test_group}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.get_group_info({"values": {"group_name": test_group}})
        print(f"✓ Get group info result:\n{result.value}\n")
        return True
    except Exception as e:
        print(f"✗ Get group info failed: {e}\n")
        return False


async def test_get_user_groups():
    """Test getting user's group memberships."""
    print("\n" + "=" * 60)
    print("TEST 2: Get User's Groups")
    print("=" * 60)

    test_user = os.getenv("TEST_USER", "testuser1")
    print(f"Target user: {test_user}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.get_user_groups({"values": {"username": test_user}})
        print(f"✓ Get user groups result:\n{result.value}\n")
        return True
    except Exception as e:
        print(f"✗ Get user groups failed: {e}\n")
        return False


async def test_add_user_to_group():
    """Test adding user to group."""
    print("\n" + "=" * 60)
    print("TEST 3: Add User to Group")
    print("=" * 60)

    test_user = os.getenv("TEST_USER", "testuser1")
    test_group = os.getenv("TEST_GROUP", "Test-Group")
    test_ticket = os.getenv("TEST_TICKET", "INC0012345")

    print(f"User: {test_user}")
    print(f"Group: {test_group}")
    print(f"Ticket: {test_ticket}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.add_user_to_group(
            {
                "values": {
                    "username": test_user,
                    "group_name": test_group,
                    "ticket_number": test_ticket,
                }
            }
        )

        if "Successfully added" in result.value or "already" in result.value.lower():
            print(f"✓ Add user to group succeeded:\n{result.value}\n")
            return True
        else:
            print(f"✗ Add user to group failed:\n{result.value}\n")
            return False

    except Exception as e:
        print(f"✗ Add user to group error: {e}\n")
        return False


async def test_remove_user_from_group():
    """Test removing user from group."""
    print("\n" + "=" * 60)
    print("TEST 4: Remove User from Group")
    print("=" * 60)

    test_user = os.getenv("TEST_USER", "testuser1")
    test_group = os.getenv("TEST_GROUP", "Test-Group")
    test_ticket = os.getenv("TEST_TICKET", "INC0012345")

    print(f"User: {test_user}")
    print(f"Group: {test_group}")
    print(f"Ticket: {test_ticket}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.remove_user_from_group(
            {
                "values": {
                    "username": test_user,
                    "group_name": test_group,
                    "ticket_number": test_ticket,
                }
            }
        )

        if "Successfully removed" in result.value or "not a member" in result.value.lower():
            print(f"✓ Remove user from group succeeded:\n{result.value}\n")
            return True
        else:
            print(f"✗ Remove user from group failed:\n{result.value}\n")
            return False

    except Exception as e:
        print(f"✗ Remove user from group error: {e}\n")
        return False


async def test_protected_group():
    """Test that protected groups cannot be modified."""
    print("\n" + "=" * 60)
    print("TEST 5: Protected Group (Expected Failure)")
    print("=" * 60)

    test_user = os.getenv("TEST_USER", "testuser1")
    test_ticket = os.getenv("TEST_TICKET", "INC0012345")

    print(f"User: {test_user}")
    print(f"Group: Domain Admins (protected)")
    print(f"Ticket: {test_ticket}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.add_user_to_group(
            {
                "values": {
                    "username": test_user,
                    "group_name": "Domain Admins",
                    "ticket_number": test_ticket,
                }
            }
        )

        # Should be an error
        if "protected" in result.value.lower() or "failed" in result.value.lower():
            print(f"✓ Correctly blocked protected group:\n{result.value}\n")
            return True
        else:
            print(f"✗ Protected group was not blocked:\n{result.value}\n")
            return False

    except Exception as e:
        print(f"✓ Exception raised as expected: {e}\n")
        return True


async def test_nonexistent_group():
    """Test operations with non-existent group."""
    print("\n" + "=" * 60)
    print("TEST 6: Non-Existent Group (Expected Failure)")
    print("=" * 60)

    test_user = os.getenv("TEST_USER", "testuser1")
    test_ticket = os.getenv("TEST_TICKET", "INC0012345")

    print(f"User: {test_user}")
    print(f"Group: NonExistentGroup12345")
    print(f"Ticket: {test_ticket}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.add_user_to_group(
            {
                "values": {
                    "username": test_user,
                    "group_name": "NonExistentGroup12345",
                    "ticket_number": test_ticket,
                }
            }
        )

        # Should be an error
        if "not found" in result.value.lower() or "failed" in result.value.lower():
            print(f"✓ Correctly handled non-existent group:\n{result.value}\n")
            return True
        else:
            print(f"✗ Unexpected result:\n{result.value}\n")
            return False

    except Exception as e:
        print(f"✓ Exception raised as expected: {e}\n")
        return True


async def test_nonexistent_user():
    """Test operations with non-existent user."""
    print("\n" + "=" * 60)
    print("TEST 7: Non-Existent User (Expected Failure)")
    print("=" * 60)

    test_group = os.getenv("TEST_GROUP", "Test-Group")
    test_ticket = os.getenv("TEST_TICKET", "INC0012345")

    print(f"User: nonexistentuser12345")
    print(f"Group: {test_group}")
    print(f"Ticket: {test_ticket}\n")

    config = ToolServerConfig(base_url="http://localhost:8000/api/v1")
    tool = GroupManagementTool(tool_server_config=config)

    try:
        result = await tool.add_user_to_group(
            {
                "values": {
                    "username": "nonexistentuser12345",
                    "group_name": test_group,
                    "ticket_number": test_ticket,
                }
            }
        )

        # Should be an error
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
    print("GROUP MANAGEMENT TOOL - INTEGRATION TEST")
    print("=" * 60)
    print("\nThis test requires:")
    print("1. Tool Server running on http://localhost:8000")
    print("2. Active Directory accessible")
    print("3. Test user and group exist (set TEST_USER, TEST_GROUP env vars)")
    print("4. svc-lucid-agent account has group membership delegation")
    print("\nEnvironment:")
    print(f"  TEST_USER={os.getenv('TEST_USER', 'testuser1')}")
    print(f"  TEST_GROUP={os.getenv('TEST_GROUP', 'Test-Group')}")
    print(f"  TEST_TICKET={os.getenv('TEST_TICKET', 'INC0012345')}")
    print("\nStarting tests...\n")

    results = []

    # Test 1: Get group info
    results.append(await test_get_group_info())

    # Test 2: Get user groups
    results.append(await test_get_user_groups())

    # Test 3: Add user to group
    results.append(await test_add_user_to_group())

    # Test 4: Remove user from group
    results.append(await test_remove_user_from_group())

    # Test 5: Protected group (should fail)
    results.append(await test_protected_group())

    # Test 6: Non-existent group (should fail)
    results.append(await test_nonexistent_group())

    # Test 7: Non-existent user (should fail)
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
