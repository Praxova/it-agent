"""LLM driver factory for workflow runtime."""
from __future__ import annotations
import logging
import os
from typing import Any

from griptape.drivers.prompt.base_prompt_driver import BasePromptDriver

from ..models import ProviderExportInfo, CredentialReference

logger = logging.getLogger(__name__)


class DriverFactoryError(Exception):
    """Error creating LLM driver."""
    pass


def resolve_credential(ref: CredentialReference) -> dict[str, str]:
    """
    Resolve credential reference to actual values.

    Supports storage types:
    - environment: Read from environment variables
    - vault: (future) Read from HashiCorp Vault
    """
    if ref.storage == "environment":
        result = {}

        if ref.username_key:
            result["username"] = os.environ.get(ref.username_key, "")
        if ref.password_key:
            result["password"] = os.environ.get(ref.password_key, "")
        if ref.api_key_key:
            result["api_key"] = os.environ.get(ref.api_key_key, "")

        return result

    elif ref.storage == "vault":
        # Future: HashiCorp Vault integration
        raise DriverFactoryError("Vault credential storage not yet implemented")

    elif ref.storage == "none":
        # No credentials needed (e.g., local Ollama)
        return {}

    else:
        raise DriverFactoryError(f"Unknown credential storage type: {ref.storage}")


def _normalize_key(key: str) -> str:
    """Normalize config key for comparison (strip underscores/hyphens, lowercase)."""
    return key.lower().replace("_", "").replace("-", "")


def _get_config_value(config: dict, key: str, default: Any = None) -> Any:
    """Get config value with case-insensitive, naming-convention-agnostic lookup.

    Handles PascalCase (C#) vs snake_case (Python) keys,
    e.g. 'base_url' matches 'BaseUrl'.
    """
    if key in config:
        return config[key]
    key_norm = _normalize_key(key)
    for k, v in config.items():
        if _normalize_key(k) == key_norm:
            return v
    return default


def create_prompt_driver(
    provider: ProviderExportInfo,
    resolved_credentials: dict[str, str] | None = None,
) -> BasePromptDriver:
    """
    Create a Griptape PromptDriver from provider configuration.

    Args:
        provider: Provider configuration from export
        resolved_credentials: Pre-resolved credentials (e.g., fetched from portal API).
            If provided, used directly instead of calling resolve_credential().

    Returns:
        Configured BasePromptDriver instance

    Raises:
        DriverFactoryError: If driver cannot be created
    """
    provider_type = provider.provider_type
    config = provider.config or {}

    # Resolve credentials
    if resolved_credentials is not None:
        credentials = resolved_credentials
    elif provider.credentials:
        credentials = resolve_credential(provider.credentials)
    else:
        credentials = {}

    try:
        if provider_type == "llm-ollama":
            from griptape.drivers.prompt.ollama import OllamaPromptDriver

            return OllamaPromptDriver(
                model=_get_config_value(config, "model", "llama3.1"),
                host=(_get_config_value(config, "base_url")
                      or _get_config_value(config, "endpoint")
                      or "http://localhost:11434"),
                temperature=float(_get_config_value(config, "temperature", 0.1)),
            )

        elif provider_type == "llm-llamacpp":
            from griptape.drivers.prompt.openai import OpenAiChatPromptDriver

            # llama.cpp uses OpenAI-compatible API but requires base_url
            # and doesn't need a real API key
            base_url = (
                _get_config_value(config, "base_url")
                or _get_config_value(config, "endpoint")
            )
            if not base_url:
                raise DriverFactoryError(
                    "llm-llamacpp requires a base_url (e.g. https://llm:8443/v1)"
                )

            # llama.cpp accepts any API key value — use a placeholder
            api_key = credentials.get("api_key") or "not-needed"

            return OpenAiChatPromptDriver(
                model=_get_config_value(config, "model", "llama3.1"),
                api_key=api_key,
                base_url=base_url,
                temperature=float(_get_config_value(config, "temperature", 0.1)),
            )

        elif provider_type == "llm-openai":
            from griptape.drivers.prompt.openai import OpenAiChatPromptDriver

            api_key = credentials.get("api_key") or os.environ.get("OPENAI_API_KEY")
            if not api_key:
                raise DriverFactoryError("OpenAI API key not found")

            return OpenAiChatPromptDriver(
                model=config.get("model", "gpt-4"),
                api_key=api_key,
                temperature=float(config.get("temperature", 0.1)),
            )

        elif provider_type == "llm-anthropic":
            from griptape.drivers.prompt.anthropic import AnthropicPromptDriver

            api_key = credentials.get("api_key") or os.environ.get("ANTHROPIC_API_KEY")
            if not api_key:
                raise DriverFactoryError("Anthropic API key not found")

            return AnthropicPromptDriver(
                model=config.get("model", "claude-3-sonnet-20240229"),
                api_key=api_key,
            )

        elif provider_type == "llm-azure-openai":
            from griptape.drivers.prompt.azure_openai_chat_prompt_driver import AzureOpenAiChatPromptDriver

            api_key = credentials.get("api_key") or os.environ.get("AZURE_OPENAI_API_KEY")
            if not api_key:
                raise DriverFactoryError("Azure OpenAI API key not found")

            return AzureOpenAiChatPromptDriver(
                azure_endpoint=config.get("endpoint"),
                azure_deployment=config.get("deployment_name"),
                api_key=api_key,
                model=config.get("model", "gpt-4"),
            )

        else:
            raise DriverFactoryError(f"Unsupported LLM provider type: {provider_type}")

    except ImportError as e:
        raise DriverFactoryError(f"Missing driver dependency: {e}")
    except Exception as e:
        raise DriverFactoryError(f"Failed to create driver: {e}")
