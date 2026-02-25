#!/usr/bin/env python3
"""Automated API integration tests for ADR-013 Approval endpoints.

Prerequisites:
  - Admin Portal running on http://localhost:5000
  - Fresh database (rm lucid-admin.db && dotnet run)

Usage:
  python scripts/test_approval_api.py [--base-url http://localhost:5000]
"""

import argparse
import json
import sys
import time
from dataclasses import dataclass, field
from typing import Any

try:
    import httpx
except ImportError:
    print("httpx not installed. Run: pip install httpx")
    sys.exit(1)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

@dataclass
class TestResult:
    name: str
    passed: bool
    detail: str = ""


@dataclass
class TestSuite:
    results: list[TestResult] = field(default_factory=list)
    approval_ids: dict[str, str] = field(default_factory=dict)  # label → id

    def record(self, name: str, passed: bool, detail: str = ""):
        self.results.append(TestResult(name, passed, detail))
        icon = "✓" if passed else "✗"
        colour = "\033[92m" if passed else "\033[91m"
        reset = "\033[0m"
        print(f"  {colour}{icon}{reset} {name}")
        if detail and not passed:
            print(f"      {detail}")

    def summary(self) -> int:
        passed = sum(1 for r in self.results if r.passed)
        total = len(self.results)
        print()
        print("=" * 60)
        if passed == total:
            print(f"\033[92mAll {total} tests passed.\033[0m")
        else:
            print(f"\033[91m{passed}/{total} tests passed, "
                  f"{total - passed} failed.\033[0m")
            for r in self.results:
                if not r.passed:
                    print(f"  FAIL: {r.name}  — {r.detail}")
        print("=" * 60)
        return 0 if passed == total else 1


def post(client: httpx.Client, path: str, body: dict) -> httpx.Response:
    return client.post(path, json=body, timeout=15)


def get(client: httpx.Client, path: str, **params: Any) -> httpx.Response:
    return client.get(path, params=params, timeout=15)


def put(client: httpx.Client, path: str, body: dict) -> httpx.Response:
    return client.put(path, json=body, timeout=15)


# ---------------------------------------------------------------------------
# Individual tests
# ---------------------------------------------------------------------------


def test_submit_pending(client: httpx.Client, suite: TestSuite):
    """2.1 — Submit approval that should stay Pending (confidence < threshold)."""
    r = post(client, "/api/approvals", {
        "workflowName": "it-dispatch",
        "stepName": "approve-classification",
        "agentName": "test-agent",
        "ticketId": "INC0010001",
        "ticketShortDescription": "Cannot log in - luke.skywalker",
        "proposedAction": "Classified as password-reset (confidence: 0.85)",
        "contextSnapshot": {
            "ticket_type": "password-reset",
            "confidence": 0.85,
            "affected_user": "luke.skywalker",
            "caller_name": "Leia Organa",
        },
        "resumeAfterStep": "approve-classification",
        "autoApproveThreshold": 0.99,
        "confidence": 0.85,
        "timeoutMinutes": 60,
    })
    ok = r.status_code == 201
    data = r.json() if ok else {}
    status_ok = data.get("status") == "Pending"
    has_expiry = data.get("expiresAt") is not None

    if ok and status_ok:
        suite.approval_ids["pending1"] = data["id"]

    suite.record(
        "2.1  Submit pending approval (confidence 0.85 < threshold 0.99)",
        ok and status_ok and has_expiry,
        f"HTTP {r.status_code}, status={data.get('status')}, "
        f"expiresAt={'set' if has_expiry else 'MISSING'}",
    )


def test_submit_auto_approve(client: httpx.Client, suite: TestSuite):
    """2.2 — Submit approval where confidence >= threshold → AutoApproved."""
    r = post(client, "/api/approvals", {
        "workflowName": "pw-reset-sub",
        "stepName": "approve-reset",
        "agentName": "test-agent",
        "ticketId": "INC0010002",
        "ticketShortDescription": "Password reset for padme.amidala",
        "proposedAction": "Reset password for padme.amidala",
        "contextSnapshot": {
            "affected_user": "padme.amidala",
            "confidence": 0.99,
        },
        "resumeAfterStep": "approve-reset",
        "autoApproveThreshold": 0.99,
        "confidence": 0.99,
        "timeoutMinutes": 30,
    })
    ok = r.status_code == 201
    data = r.json() if ok else {}
    suite.approval_ids["auto1"] = data.get("id", "")

    suite.record(
        "2.2  Submit auto-approved (confidence 0.99 == threshold)",
        ok
        and data.get("status") == "AutoApproved"
        and data.get("wasAutoApproved") is True
        and data.get("decidedBy") == "system",
        f"status={data.get('status')}, wasAutoApproved={data.get('wasAutoApproved')}, "
        f"decidedBy={data.get('decidedBy')}",
    )


def test_list_all(client: httpx.Client, suite: TestSuite):
    """2.3a — List all approvals (no filter)."""
    r = get(client, "/api/approvals")
    data = r.json()
    total = data.get("totalCount", 0)
    suite.record(
        "2.3a List all approvals (expect >= 2)",
        r.status_code == 200 and total >= 2,
        f"totalCount={total}",
    )


def test_list_pending_filter(client: httpx.Client, suite: TestSuite):
    """2.3b — List approvals filtered to Pending only."""
    r = get(client, "/api/approvals", status="Pending")
    data = r.json()
    items = list(data.get("items", []))
    all_pending = all(i.get("status") == "Pending" for i in items)
    suite.record(
        "2.3b List with status=Pending filter",
        r.status_code == 200 and len(items) >= 1 and all_pending,
        f"count={len(items)}, all_pending={all_pending}",
    )


def test_list_by_agent(client: httpx.Client, suite: TestSuite):
    """2.3c — List approvals filtered by agentName."""
    r = get(client, "/api/approvals", agentName="test-agent")
    data = r.json()
    total = data.get("totalCount", 0)
    suite.record(
        "2.3c List with agentName=test-agent filter",
        r.status_code == 200 and total >= 2,
        f"totalCount={total}",
    )


def test_get_detail(client: httpx.Client, suite: TestSuite):
    """2.4 — Get full approval detail including contextSnapshot."""
    aid = suite.approval_ids.get("pending1")
    if not aid:
        suite.record("2.4  Get detail", False, "No pending approval id available")
        return

    r = get(client, f"/api/approvals/{aid}")
    data = r.json()
    has_snapshot = isinstance(data.get("contextSnapshot"), dict)
    snap_keys = list(data.get("contextSnapshot", {}).keys()) if has_snapshot else []
    suite.record(
        "2.4  Get approval detail (contextSnapshot present)",
        r.status_code == 200 and has_snapshot and "ticket_type" in snap_keys,
        f"has_snapshot={has_snapshot}, keys={snap_keys}",
    )


def test_approve_pending(client: httpx.Client, suite: TestSuite):
    """2.5 — Approve a pending request."""
    aid = suite.approval_ids.get("pending1")
    if not aid:
        suite.record("2.5  Approve pending", False, "No pending approval id")
        return

    r = put(client, f"/api/approvals/{aid}/decide", {
        "status": "Approved",
        "decision": "Looks correct, proceed with reset",
        "decidedBy": "admin@montanifarms.com",
    })
    data = r.json()
    has_decided_at = data.get("decidedAt") is not None
    suite.record(
        "2.5  Approve a pending request",
        r.status_code == 200
        and data.get("status") == "Approved"
        and has_decided_at,
        f"status={data.get('status')}, decidedAt={'set' if has_decided_at else 'MISSING'}",
    )


def test_reject_request(client: httpx.Client, suite: TestSuite):
    """2.6 — Submit a new request, then reject it."""
    # Submit a new pending request
    r1 = post(client, "/api/approvals", {
        "workflowName": "it-dispatch",
        "stepName": "approve-classification",
        "agentName": "test-agent",
        "ticketId": "INC0010003",
        "ticketShortDescription": "Add han.solo to VPN group",
        "proposedAction": "Classified as group-membership (confidence: 0.72)",
        "contextSnapshot": {
            "ticket_type": "group-membership",
            "confidence": 0.72,
            "affected_user": "han.solo",
        },
        "resumeAfterStep": "approve-classification",
        "autoApproveThreshold": 0.99,
        "confidence": 0.72,
        "timeoutMinutes": 60,
    })
    if r1.status_code != 201:
        suite.record("2.6  Reject a request", False,
                     f"Submit failed: HTTP {r1.status_code}")
        return

    new_id = r1.json()["id"]
    suite.approval_ids["rejected1"] = new_id

    # Reject it
    r2 = put(client, f"/api/approvals/{new_id}/decide", {
        "status": "Rejected",
        "decision": "Confidence too low, needs manual review",
        "decidedBy": "admin@montanifarms.com",
    })
    data = r2.json()
    suite.record(
        "2.6  Reject a request",
        r2.status_code == 200 and data.get("status") == "Rejected",
        f"status={data.get('status')}",
    )


def test_decide_non_pending(client: httpx.Client, suite: TestSuite):
    """2.7 — Attempt to decide on already-decided approval → 400."""
    aid = suite.approval_ids.get("pending1")  # already approved in 2.5
    if not aid:
        suite.record("2.7  Decide on non-pending", False, "No approval id")
        return

    r = put(client, f"/api/approvals/{aid}/decide", {
        "status": "Rejected",
        "decidedBy": "admin",
    })
    data = r.json()
    suite.record(
        "2.7  Decide on non-pending → 400 NotPending",
        r.status_code == 400 and data.get("error") == "NotPending",
        f"HTTP {r.status_code}, error={data.get('error')}",
    )


def test_agent_poll_actionable(client: httpx.Client, suite: TestSuite):
    """2.8 — Agent polls /actionable for decided, unacknowledged items."""
    r = get(client, "/api/approvals/actionable", agentName="test-agent")
    items = r.json()
    # Should contain the Approved (2.5) and Rejected (2.6) items
    statuses = {i.get("status") for i in items}
    suite.record(
        "2.8  Agent polls actionable (Approved + Rejected present)",
        r.status_code == 200
        and len(items) >= 2
        and {"Approved", "Rejected"}.issubset(statuses),
        f"count={len(items)}, statuses={statuses}",
    )


def test_acknowledge(client: httpx.Client, suite: TestSuite):
    """2.9 — Agent acknowledges a decided approval."""
    aid = suite.approval_ids.get("pending1")
    if not aid:
        suite.record("2.9  Acknowledge", False, "No approval id")
        return

    r = post(client, f"/api/approvals/{aid}/acknowledge", {
        "agentName": "test-agent",
    })
    data = r.json()
    has_ack = data.get("acknowledgedAt") is not None
    suite.record(
        "2.9  Agent acknowledges decision",
        r.status_code == 200 and has_ack,
        f"acknowledgedAt={'set' if has_ack else 'MISSING'}",
    )


def test_re_acknowledge(client: httpx.Client, suite: TestSuite):
    """2.10 — Re-acknowledge same approval → 400."""
    aid = suite.approval_ids.get("pending1")
    if not aid:
        suite.record("2.10 Re-acknowledge", False, "No approval id")
        return

    r = post(client, f"/api/approvals/{aid}/acknowledge", {
        "agentName": "test-agent",
    })
    data = r.json()
    suite.record(
        "2.10 Re-acknowledge → 400 AlreadyAcknowledged",
        r.status_code == 400 and data.get("error") == "AlreadyAcknowledged",
        f"HTTP {r.status_code}, error={data.get('error')}",
    )


def test_stats(client: httpx.Client, suite: TestSuite):
    """2.11 — Stats endpoint returns aggregate metrics."""
    r = get(client, "/api/approvals/stats")
    data = r.json()
    required_keys = (
        "pendingCount", "autoApprovedToday", "humanApprovedToday",
        "rejectedToday", "timedOutToday",
    )
    has_keys = all(k in data for k in required_keys)
    # Verify counts match what we created
    suite.record(
        "2.11 Stats endpoint (all metric keys present)",
        r.status_code == 200 and has_keys,
        f"keys={list(data.keys())}, values={{ k: data.get(k) for k in required_keys }}",
    )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="ADR-013 Approval API integration tests",
    )
    parser.add_argument(
        "--base-url", default="http://localhost:5000",
        help="Admin Portal base URL (default: http://localhost:5000)",
    )
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    print()
    print("=" * 60)
    print("ADR-013  Approval API — Integration Tests")
    print(f"Target:  {base}")
    print("=" * 60)
    print()

    # Quick connectivity check
    try:
        r = httpx.get(f"{base}/api/approvals/stats", timeout=5)
        r.raise_for_status()
    except Exception as e:
        print(f"\033[91mERROR: Cannot reach Admin Portal at {base}\033[0m")
        print(f"  {e}")
        print()
        print("Make sure the portal is running:")
        print("  cd admin/dotnet/src/LucidAdmin.Web && dotnet run")
        return 1

    suite = TestSuite()
    client = httpx.Client(base_url=base)

    print("Running 13 tests across submit / list / decide / poll / stats...")
    print()

    # Submit
    test_submit_pending(client, suite)
    test_submit_auto_approve(client, suite)

    # List / Filter
    test_list_all(client, suite)
    test_list_pending_filter(client, suite)
    test_list_by_agent(client, suite)

    # Detail
    test_get_detail(client, suite)

    # Decide
    test_approve_pending(client, suite)
    test_reject_request(client, suite)
    test_decide_non_pending(client, suite)

    # Agent poll / acknowledge
    test_agent_poll_actionable(client, suite)
    test_acknowledge(client, suite)
    test_re_acknowledge(client, suite)

    # Stats
    test_stats(client, suite)

    client.close()
    return suite.summary()


if __name__ == "__main__":
    sys.exit(main())
