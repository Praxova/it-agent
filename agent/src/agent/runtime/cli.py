"""Command-line interface for Lucid IT Agent."""
from __future__ import annotations
import argparse
import asyncio
import logging
import os
import sys
from pathlib import Path

from dotenv import load_dotenv

from .runner import run_agent


def setup_logging(level: str = "INFO"):
    """Configure logging."""
    logging.basicConfig(
        level=getattr(logging, level.upper()),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )


def main():
    """Main CLI entry point."""
    # Load .env file (search current dir and parent dirs)
    load_dotenv()

    parser = argparse.ArgumentParser(
        description="Lucid IT Agent - AI-powered IT helpdesk automation"
    )

    parser.add_argument(
        "--admin-url",
        default=os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000"),
        help="Admin Portal URL (default: $ADMIN_PORTAL_URL or http://localhost:5000)",
    )

    parser.add_argument(
        "--agent-name",
        default=os.environ.get("AGENT_NAME", ""),
        required=not os.environ.get("AGENT_NAME"),
        help="Agent name to run (default: $AGENT_NAME)",
    )

    parser.add_argument(
        "--poll-interval",
        type=int,
        default=int(os.environ.get("POLL_INTERVAL", "30")),
        help="Seconds between queue polls (default: 30)",
    )

    parser.add_argument(
        "--log-level",
        default=os.environ.get("LOG_LEVEL", "INFO"),
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        help="Log level (default: INFO)",
    )

    parser.add_argument(
        "--heartbeat-interval",
        type=int,
        default=int(os.environ.get("HEARTBEAT_INTERVAL", "60")),
        help="Heartbeat reporting interval in seconds (default: 60)",
    )

    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Load configuration but don't start processing",
    )

    args = parser.parse_args()

    setup_logging(args.log_level)
    logger = logging.getLogger(__name__)

    logger.info(f"Lucid IT Agent starting...")
    logger.info(f"  Admin Portal: {args.admin_url}")
    logger.info(f"  Agent Name: {args.agent_name}")
    logger.info(f"  Poll Interval: {args.poll_interval}s")
    logger.info(f"  Heartbeat Interval: {args.heartbeat_interval}s")

    if args.dry_run:
        logger.info("Dry run mode - will load config and exit")
        from .runner import AgentRunner
        runner = AgentRunner(args.admin_url, args.agent_name)
        asyncio.run(runner.initialize())
        logger.info("Configuration loaded successfully. Exiting.")
        return 0

    try:
        asyncio.run(run_agent(
            admin_portal_url=args.admin_url,
            agent_name=args.agent_name,
            poll_interval=args.poll_interval,
            heartbeat_interval=args.heartbeat_interval,
        ))
    except KeyboardInterrupt:
        logger.info("Interrupted by user")
    except Exception as e:
        logger.error(f"Agent failed: {e}", exc_info=True)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
