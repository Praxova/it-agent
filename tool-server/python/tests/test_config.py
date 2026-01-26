"""Tests for configuration settings."""

import os
from unittest.mock import patch

import pytest

from tool_server.config import Settings


class TestSettings:
    """Test cases for Settings model."""

    def test_default_values(self) -> None:
        """Test default configuration values."""
        # Set required fields
        env_vars = {
            "TOOL_SERVER_LDAP_SERVER": "dc.example.com",
            "TOOL_SERVER_LDAP_BASE_DN": "DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_USER": "CN=admin,DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_PASSWORD": "password",
        }

        with patch.dict(os.environ, env_vars, clear=True):
            settings = Settings()

            assert settings.host == "0.0.0.0"
            assert settings.port == 8000
            assert settings.reload is False
            assert settings.ldap_port == 389
            assert settings.ldap_use_ssl is False
            assert settings.timeout == 30
            assert settings.pool_size == 5
            assert settings.pool_keepalive == 60

    def test_required_ldap_fields(self) -> None:
        """Test that required LDAP fields are validated."""
        env_vars = {
            "TOOL_SERVER_LDAP_SERVER": "dc.example.com",
            "TOOL_SERVER_LDAP_BASE_DN": "DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_USER": "CN=admin,DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_PASSWORD": "password",
        }

        with patch.dict(os.environ, env_vars, clear=True):
            settings = Settings()

            assert settings.ldap_server == "dc.example.com"
            assert settings.ldap_base_dn == "DC=example,DC=com"
            assert settings.ldap_bind_user == "CN=admin,DC=example,DC=com"
            assert settings.ldap_bind_password == "password"

    def test_custom_values(self) -> None:
        """Test custom configuration values."""
        env_vars = {
            "TOOL_SERVER_HOST": "127.0.0.1",
            "TOOL_SERVER_PORT": "9000",
            "TOOL_SERVER_RELOAD": "true",
            "TOOL_SERVER_LDAP_SERVER": "dc.example.com",
            "TOOL_SERVER_LDAP_PORT": "636",
            "TOOL_SERVER_LDAP_USE_SSL": "true",
            "TOOL_SERVER_LDAP_BASE_DN": "DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_USER": "CN=admin,DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_PASSWORD": "password",
            "TOOL_SERVER_TIMEOUT": "60",
        }

        with patch.dict(os.environ, env_vars, clear=True):
            settings = Settings()

            assert settings.host == "127.0.0.1"
            assert settings.port == 9000
            assert settings.reload is True
            assert settings.ldap_port == 636
            assert settings.ldap_use_ssl is True
            assert settings.timeout == 60

    def test_ssl_certificate_settings(self) -> None:
        """Test SSL certificate configuration."""
        env_vars = {
            "TOOL_SERVER_LDAP_SERVER": "dc.example.com",
            "TOOL_SERVER_LDAP_BASE_DN": "DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_USER": "CN=admin,DC=example,DC=com",
            "TOOL_SERVER_LDAP_BIND_PASSWORD": "password",
            "TOOL_SERVER_LDAP_CA_CERT_FILE": "/etc/ssl/certs/ca-bundle.crt",
            "TOOL_SERVER_LDAP_VALIDATE_CERT": "false",
        }

        with patch.dict(os.environ, env_vars, clear=True):
            settings = Settings()

            assert settings.ldap_ca_cert_file == "/etc/ssl/certs/ca-bundle.crt"
            assert settings.ldap_validate_cert is False
