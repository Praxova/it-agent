"""Configuration settings for Tool Server."""

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Tool server configuration from environment variables.

    All settings can be overridden via environment variables with TOOL_SERVER_ prefix.
    Supports loading from .env file in project root.
    """

    model_config = SettingsConfigDict(
        env_prefix="TOOL_SERVER_",
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=False,
        extra="ignore",
    )

    # Server settings
    host: str = "0.0.0.0"
    port: int = 8000
    reload: bool = False

    # LDAP/AD settings
    ldap_server: str
    ldap_port: int = 389
    ldap_use_ssl: bool = False
    ldap_base_dn: str
    ldap_bind_user: str
    ldap_bind_password: str

    # Optional LDAPS settings
    ldap_ca_cert_file: str | None = None
    ldap_validate_cert: bool = True

    # Operational settings
    timeout: int = 30  # LDAP operation timeout in seconds
    pool_size: int = 5  # Connection pool size
    pool_keepalive: int = 60  # Keepalive interval in seconds

    # Protected groups that cannot be modified
    protected_groups_deny_list: list[str] = [
        "Domain Admins",
        "Enterprise Admins",
        "Schema Admins",
        "Administrators",
        "Account Operators",
        "Backup Operators",
        "Server Operators",
        "Print Operators",
        "Cert Publishers",
    ]
