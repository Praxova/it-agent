#!/usr/bin/env python
"""
Test runner script for runtime tests.

Usage:
    python run_tests.py              # Run all unit tests
    python run_tests.py --all        # Run all tests including integration
    python run_tests.py --integration # Run only integration tests
    python run_tests.py --live       # Run live integration tests
"""
import subprocess
import sys
import os


def run_tests(args: list[str]):
    """Run pytest with specified arguments."""
    cmd = ["pytest", "tests/runtime/", "-v"] + args
    print(f"Running: {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=os.path.dirname(os.path.dirname(os.path.dirname(__file__))))


def main():
    args = sys.argv[1:]

    if "--help" in args or "-h" in args:
        print(__doc__)
        return 0

    pytest_args = []

    if "--all" in args:
        # Run everything
        pass
    elif "--integration" in args:
        pytest_args.extend(["-m", "integration"])
    elif "--live" in args:
        pytest_args.extend(["-m", "integration or manual"])
    else:
        # Default: skip integration and manual tests
        pytest_args.extend(["-m", "not integration and not manual"])

    # Add any additional pytest args
    for arg in args:
        if arg not in ["--all", "--integration", "--live"]:
            pytest_args.append(arg)

    result = run_tests(pytest_args)
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
