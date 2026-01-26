#!/usr/bin/env python3
"""Integration test for File Permissions with live DC/File Server.

Requirements:
- Tool server running on port 8100
- WinRM accessible on DC (172.16.119.20)
- Test share exists: \\172.16.119.20\lucidtestshare
"""

import asyncio
import sys

import httpx

BASE_URL = "http://127.0.0.1:8100"
TEST_USER = "luke.skywalker"
TEST_PATH = r"\\172.16.119.20\lucidtestshare\Department1"
TEST_TICKET = "INC0000003"


async def main():
    """Run file permissions integration tests."""
    async with httpx.AsyncClient(base_url=BASE_URL, timeout=60.0) as client:
        print("=" * 60)
        print("File Permissions Integration Tests")
        print("=" * 60)

        # Test 1: Health check (now includes WinRM)
        print("\n[1] Health Check...")
        try:
            r = await client.get("/api/v1/tools/health")
            health = r.json()
            print(f"    Status: {health['status']}")
            print(f"    LDAP Connected: {health['ldap_connected']}")
            print(f"    WinRM Connected: {health.get('winrm_connected', 'N/A')}")
            if not health.get("winrm_connected"):
                print("    ⚠ WinRM connection failed - tests may not work")
        except Exception as e:
            print(f"    ✗ Error: {e}")
            return 1

        # Test 2: List current permissions
        print(f"\n[2] List Permissions on {TEST_PATH}...")
        try:
            url_path = TEST_PATH.lstrip("\\").replace("\\", "/")
            r = await client.get(f"/api/v1/tools/permissions/{url_path}")
            if r.status_code == 200:
                perms = r.json()
                print(f"    Found {len(perms['permissions'])} permission entries")
                for perm in perms["permissions"][:3]:  # Show first 3
                    print(f"      {perm['user']}: {perm['rights']}")
            else:
                print(f"    ✗ Error: {r.json()}")
        except Exception as e:
            print(f"    ✗ Error: {e}")

        # Test 3: Grant Read permission
        print(f"\n[3] Grant Read Permission to {TEST_USER}...")
        try:
            r = await client.post(
                "/api/v1/tools/permissions/grant",
                json={
                    "username": TEST_USER,
                    "path": TEST_PATH,
                    "permission": "Read",
                    "ticket_number": TEST_TICKET,
                },
            )
            if r.status_code == 200:
                result = r.json()
                print(f"    ✓ {result['message']}")
            else:
                print(f"    ✗ Error ({r.status_code}): {r.json()}")
                return 1
        except Exception as e:
            print(f"    ✗ Error: {e}")
            return 1

        # Test 4: Verify permission was added
        print(f"\n[4] Verify Permission Added...")
        try:
            url_path = TEST_PATH.lstrip("\\").replace("\\", "/")
            r = await client.get(f"/api/v1/tools/permissions/{url_path}")
            perms = r.json()["permissions"]
            user_perms = [p for p in perms if TEST_USER.lower() in p["user"].lower()]
            if user_perms:
                print(
                    f"    ✓ Found permissions for {TEST_USER}: {user_perms[0]['rights']}"
                )
            else:
                print(f"    ✗ Permission not found!")
                return 1
        except Exception as e:
            print(f"    ✗ Error: {e}")
            return 1

        # Test 5: Revoke permission
        print(f"\n[5] Revoke Permission...")
        try:
            r = await client.post(
                "/api/v1/tools/permissions/revoke",
                json={
                    "username": TEST_USER,
                    "path": TEST_PATH,
                    "ticket_number": TEST_TICKET,
                },
            )
            if r.status_code == 200:
                result = r.json()
                print(f"    ✓ {result['message']}")
            else:
                print(f"    ✗ Error ({r.status_code}): {r.json()}")
                return 1
        except Exception as e:
            print(f"    ✗ Error: {e}")
            return 1

        # Test 6: Test denied path
        print(f"\n[6] Test Denied Path (should fail)...")
        try:
            r = await client.post(
                "/api/v1/tools/permissions/grant",
                json={
                    "username": TEST_USER,
                    "path": r"\\172.16.119.20\C$\Windows",
                    "permission": "Read",
                    "ticket_number": TEST_TICKET,
                },
            )
            if r.status_code == 403:
                print(f"    ✓ Correctly rejected (403 Forbidden)")
            else:
                print(f"    ✗ Should have been rejected: {r.status_code}")
                return 1
        except Exception as e:
            print(f"    ✗ Error: {e}")
            return 1

        print("\n" + "=" * 60)
        print("All tests passed!")
        print("=" * 60)
        return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
