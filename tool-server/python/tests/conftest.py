"""Pytest configuration and fixtures."""

import os
from unittest.mock import patch

import pytest


@pytest.fixture(scope="session", autouse=True)
def mock_env_vars():
    """Mock environment variables for all tests."""
    env_vars = {
        "TOOL_SERVER_LDAP_SERVER": "dc.example.com",
        "TOOL_SERVER_LDAP_BASE_DN": "DC=example,DC=com",
        "TOOL_SERVER_LDAP_BIND_USER": "CN=admin,DC=example,DC=com",
        "TOOL_SERVER_LDAP_BIND_PASSWORD": "password",
    }

    with patch.dict(os.environ, env_vars, clear=False):
        yield
