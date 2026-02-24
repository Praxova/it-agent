#!/usr/bin/env python3
"""Praxova IT Agent - Main Entry Point.

Usage:
    python -m agent.main --once     # Process queue once and exit
    python -m agent.main --daemon   # Run continuously
    python -m agent.main --help     # Show help
"""

import argparse
import asyncio
import logging
import signal
import sys

from agent.pipeline.config import PipelineConfig
from agent.pipeline.executor import TicketExecutor

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


def parse_args():
    """Parse command-line arguments.

    Returns:
        Parsed arguments.
    """
    parser = argparse.ArgumentParser(
        description="Praxova IT Agent - Automated IT helpdesk",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )

    mode_group = parser.add_mutually_exclusive_group(required=True)
    mode_group.add_argument(
        "--once",
        action="store_true",
        help="Process ticket queue once and exit",
    )
    mode_group.add_argument(
        "--daemon",
        action="store_true",
        help="Run continuously, polling at configured interval",
    )

    parser.add_argument(
        "--config",
        type=str,
        help="Path to config file (optional, uses env vars by default)",
    )
    parser.add_argument(
        "-v",
        "--verbose",
        action="store_true",
        help="Enable debug logging",
    )

    return parser.parse_args()


async def main():
    """Main entry point for the application.

    Returns:
        Exit code (0 for success, 1 for failure).
    """
    args = parse_args()

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    # Load config
    try:
        config = PipelineConfig()
    except Exception as e:
        logger.error(f"Failed to load configuration: {e}")
        logger.error("Ensure all required environment variables are set. See .env.example")
        return 1

    # Create executor
    executor = TicketExecutor(config)

    # Set up signal handlers for graceful shutdown
    def signal_handler(sig, frame):
        logger.info("Shutdown signal received")
        executor.stop()

    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)

    try:
        await executor.initialize()

        if args.once:
            logger.info("Running in single-pass mode")
            processed = await executor.run_once()
            logger.info(f"Processed {processed} tickets")
        else:
            logger.info("Running in daemon mode")
            await executor.run_daemon()

    except KeyboardInterrupt:
        logger.info("Interrupted by user")
    except Exception as e:
        logger.exception(f"Fatal error: {e}")
        return 1
    finally:
        await executor.close()

    return 0


def run():
    """Entry point for console script."""
    sys.exit(asyncio.run(main()))


if __name__ == "__main__":
    run()
