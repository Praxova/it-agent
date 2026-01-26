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

    # WinRM Settings (for file permissions)
    winrm_host: str = "172.16.119.20"
    winrm_username: str | None = None  # Uses ldap_bind_user if not specified
    winrm_password: str | None = None  # Uses ldap_bind_password if not specified
    winrm_transport: str = "ntlm"  # ntlm, kerberos, or basic

    # File Permission Settings
    file_permission_denied_paths: list[str] = [
        r"\\*\C$",
        r"\\*\D$",
        r"\\*\ADMIN$",
        r"\\*\SYSVOL",
        r"\\*\NETLOGON",
    ]

    # Empty = allow all paths not in denied list
    file_permission_allowed_paths: list[str] = []

    @property
    def ldap_domain(self) -> str:
        """Extract domain from base DN (e.g., DC=example,DC=com -> example.com)"""
        parts = []
        for component in self.ldap_base_dn.split(','):
            if component.strip().upper().startswith('DC='):
                parts.append(component.split('=', 1)[1].strip())
        return '.'.join(parts)
