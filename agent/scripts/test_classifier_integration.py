#!/usr/bin/env python3
"""Integration test for TicketClassifier with Ollama.

Run: python scripts/test_classifier_integration.py

Requires:
- Ollama running with llama3.1 model
- Run from agent/ directory or with PYTHONPATH set

This tests the classifier against real Ollama inference with various ticket types.
"""

import logging
import sys
from datetime import datetime
from pathlib import Path

# Add parent directory to path so we can import agent modules
sys.path.insert(0, str(Path(__file__).parent.parent / "src"))

from connectors import Ticket, TicketState

from agent.classifier import TicketClassifier, TicketType

# Set up logging
logging.basicConfig(
    level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger(__name__)


# Test cases with expected outcomes
TEST_CASES = [
    {
        "name": "Clear password reset",
        "ticket": Ticket(
            id="test1",
            number="INC0010001",
            short_description="Password reset needed",
            description="I forgot my password and can't log into my computer. My username is jsmith. Please help!",
            state=TicketState.NEW,
            priority=3,
            caller_username="jsmith",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.PASSWORD_RESET,
        "min_confidence": 0.8,
    },
    {
        "name": "Group access add",
        "ticket": Ticket(
            id="test2",
            number="INC0010002",
            short_description="Need access to Finance group",
            description="Hi, I need to be added to the Finance-Users group to access the financial reports. Username: sconnor",
            state=TicketState.NEW,
            priority=4,
            caller_username="sconnor",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.GROUP_ACCESS_ADD,
        "min_confidence": 0.75,
    },
    {
        "name": "Group access remove",
        "ticket": Ticket(
            id="test3",
            number="INC0010003",
            short_description="Remove user from group",
            description="Please remove mjones from the IT-Admins group. He moved to a different department.",
            state=TicketState.NEW,
            priority=3,
            caller_username="manager1",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.GROUP_ACCESS_REMOVE,
        "min_confidence": 0.75,
    },
    {
        "name": "File permission request",
        "ticket": Ticket(
            id="test4",
            number="INC0010004",
            short_description="Need folder access",
            description="I need read access to \\\\fileserver\\projects\\Q4Planning folder. Username: bwilson",
            state=TicketState.NEW,
            priority=4,
            caller_username="bwilson",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.FILE_PERMISSION,
        "min_confidence": 0.75,
    },
    {
        "name": "Hardware issue (should be unknown)",
        "ticket": Ticket(
            id="test5",
            number="INC0010005",
            short_description="Computer won't turn on",
            description="My laptop won't power on at all. I tried plugging it in but nothing happens.",
            state=TicketState.NEW,
            priority=2,
            caller_username="auser",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.UNKNOWN,
        "min_confidence": 0.7,
        "expect_escalation": True,
    },
    {
        "name": "Vague request (low confidence)",
        "ticket": Ticket(
            id="test6",
            number="INC0010006",
            short_description="Help needed",
            description="I need help with something",
            state=TicketState.NEW,
            priority=5,
            caller_username="vague_user",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.UNKNOWN,
        "min_confidence": 0.0,  # Any confidence
        "max_confidence": 0.6,  # But should be low
        "expect_escalation": True,
    },
    {
        "name": "Account locked (password reset variant)",
        "ticket": Ticket(
            id="test7",
            number="INC0010007",
            short_description="Account locked",
            description="My account got locked after too many failed login attempts. Can you unlock it? Username: dsmith",
            state=TicketState.NEW,
            priority=2,
            caller_username="dsmith",
            assignment_group="Helpdesk",
            created_at=datetime.now(),
            updated_at=datetime.now(),
        ),
        "expected_type": TicketType.PASSWORD_RESET,
        "min_confidence": 0.75,
    },
]


def test_classifier_with_ollama():
    """Test classifier against real Ollama instance."""
    logger.info("=" * 80)
    logger.info("TicketClassifier Integration Test with Ollama")
    logger.info("=" * 80)

    # Initialize classifier
    logger.info("Initializing TicketClassifier with Ollama (llama3.1)...")
    try:
        classifier = TicketClassifier(
            model="llama3.1", base_url="http://localhost:11434", temperature=0.1
        )
        logger.info("✓ Classifier initialized successfully")
    except Exception as e:
        logger.error(f"✗ Failed to initialize classifier: {e}")
        logger.error(
            "Make sure Ollama is running and llama3.1 model is installed: ollama pull llama3.1"
        )
        return 1

    # Run test cases
    results = []
    passed = 0
    failed = 0

    logger.info(f"\nRunning {len(TEST_CASES)} test cases...\n")

    for i, case in enumerate(TEST_CASES, 1):
        logger.info(f"Test {i}/{len(TEST_CASES)}: {case['name']}")
        logger.info(f"  Ticket: {case['ticket'].number}")
        logger.info(f"  Description: {case['ticket'].short_description}")

        try:
            # Classify the ticket
            result = classifier.classify(case["ticket"])

            # Check type match
            type_match = result.ticket_type == case["expected_type"]

            # Check confidence
            conf_ok = result.confidence >= case.get("min_confidence", 0.0)
            if "max_confidence" in case:
                conf_ok = conf_ok and result.confidence <= case["max_confidence"]

            # Check escalation
            escalation_ok = True
            if "expect_escalation" in case:
                escalation_ok = result.should_escalate == case["expect_escalation"]

            # Overall pass/fail
            test_passed = type_match and conf_ok and escalation_ok

            if test_passed:
                logger.info(
                    f"  ✓ PASS - Type: {result.ticket_type}, Confidence: {result.confidence:.2f}, "
                    f"Action: {result.action_recommended}"
                )
                passed += 1
            else:
                logger.error(
                    f"  ✗ FAIL - Expected: {case['expected_type']}, Got: {result.ticket_type}"
                )
                logger.error(
                    f"         Confidence: {result.confidence:.2f} (min: {case.get('min_confidence', 0.0)})"
                )
                if "expect_escalation" in case:
                    logger.error(
                        f"         Escalation: {result.should_escalate} (expected: {case['expect_escalation']})"
                    )
                failed += 1

            # Log extracted entities
            if result.affected_user:
                logger.info(f"  Affected user: {result.affected_user}")
            if result.target_group:
                logger.info(f"  Target group: {result.target_group}")
            if result.target_resource:
                logger.info(f"  Target resource: {result.target_resource}")
            if result.should_escalate:
                logger.info(f"  Escalation reason: {result.escalation_reason}")

            logger.info(f"  Reasoning: {result.reasoning}\n")

            results.append(
                {
                    "case": case["name"],
                    "passed": test_passed,
                    "result": result,
                }
            )

        except Exception as e:
            logger.error(f"  ✗ EXCEPTION: {e}\n")
            failed += 1
            results.append({"case": case["name"], "passed": False, "error": str(e)})

    # Summary
    logger.info("=" * 80)
    logger.info("Test Summary")
    logger.info("=" * 80)

    total = passed + failed
    logger.info(f"Total tests: {total}")
    logger.info(f"Passed: {passed}")
    logger.info(f"Failed: {failed}")
    logger.info(f"Success rate: {(passed/total*100):.1f}%")
    logger.info("=" * 80)

    if passed == total:
        logger.info("\n🎉 All tests passed! Classifier is working correctly.")
        return 0
    else:
        logger.error(
            f"\n⚠️  {failed} test(s) failed. Review the logs above for details."
        )
        logger.error(
            "\nNote: LLM outputs can vary. Some variance is expected. "
            "If most tests pass and failures are borderline cases, this may be acceptable."
        )
        return 1


def main():
    """Main entry point."""
    try:
        return test_classifier_with_ollama()
    except KeyboardInterrupt:
        logger.info("\n\nTest interrupted by user")
        return 130
    except Exception as e:
        logger.error(f"\nUnexpected error: {e}")
        import traceback

        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
