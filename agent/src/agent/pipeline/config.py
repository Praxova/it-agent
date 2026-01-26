"""Pipeline configuration settings."""

from pydantic_settings import BaseSettings, SettingsConfigDict


class PipelineConfig(BaseSettings):
    """Pipeline configuration from environment variables.

    All settings can be overridden via environment variables with LUCID_ prefix.
    """

    model_config = SettingsConfigDict(
        env_prefix="LUCID_",
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=False,
        extra="ignore",
    )

    # ServiceNow
    servicenow_instance: str
    servicenow_username: str
    servicenow_password: str
    assignment_group: str = "Helpdesk"
    agent_user: str = "Lucid Agent"  # Display name for assignment

    # Tool Server
    tool_server_url: str = "http://127.0.0.1:8100"

    # LLM
    ollama_model: str = "llama3.1"
    ollama_base_url: str = "http://localhost:11434"

    # Behavior
    confidence_threshold_auto: float = 0.8  # Auto-execute above this
    confidence_threshold_review: float = 0.6  # Flag for review above this, escalate below
    poll_interval_seconds: int = 30  # For daemon mode

    # Escalation
    escalation_group: str = "Helpdesk"  # Group to reassign escalated tickets
