"""Factory for creating Griptape PromptDrivers from ServiceAccount configuration."""

import logging
from typing import Any

from griptape.drivers.prompt import BasePromptDriver
from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
from griptape.drivers.prompt.openai import OpenAiChatPromptDriver

from agent.config.admin_client import LlmProviderConfig

logger = logging.getLogger(__name__)


class DriverFactory:
    """Factory for creating Griptape PromptDrivers from ServiceAccount configuration.

    Supports multiple LLM providers:
    - llm-local: Local llama.cpp server (OpenAI-compatible API with TLS)
    - llm-openai: OpenAI API (GPT-3.5, GPT-4, etc.)
    - llm-anthropic: Anthropic API (Claude)
    """

    @staticmethod
    def create_driver(provider_config: LlmProviderConfig) -> BasePromptDriver:
        """Create a PromptDriver from ServiceAccount configuration.

        Args:
            provider_config: Provider configuration from Admin Portal.

        Returns:
            Configured Griptape PromptDriver instance.

        Raises:
            ValueError: If provider type is unsupported or configuration is invalid.
        """
        provider = provider_config.provider_type.lower()

        logger.info(f"Creating PromptDriver for provider: {provider}")

        if provider == "llm-local":
            return DriverFactory._create_local_llm_driver(provider_config)
        elif provider == "llm-openai":
            return DriverFactory._create_openai_driver(provider_config)
        elif provider == "llm-anthropic":
            return DriverFactory._create_anthropic_driver(provider_config)
        else:
            raise ValueError(
                f"Unsupported LLM provider: {provider}. "
                f"Supported providers: llm-local, llm-openai, llm-anthropic"
            )

    @staticmethod
    def _create_local_llm_driver(provider_config: LlmProviderConfig) -> OpenAiChatPromptDriver:
        """Create local llama.cpp PromptDriver (OpenAI-compatible API).

        Expected configuration:
        - model: Model name/alias (e.g., "llama3.1")
        - base_url: llama.cpp server URL (e.g., "https://llm:8443/v1")
        - temperature: (optional) Sampling temperature (default: 0.1)

        Args:
            provider_config: Provider configuration from Admin Portal.

        Returns:
            Configured OpenAiChatPromptDriver pointing at the local llama.cpp server.

        Raises:
            ValueError: If required configuration is missing.
        """
        model = provider_config.model
        if not model:
            raise ValueError("Local LLM configuration missing 'model'")

        base_url = provider_config.base_url
        if not base_url:
            raise ValueError("Local LLM configuration missing 'base_url'")

        temperature = provider_config.temperature

        logger.info(f"Creating local LLM driver: model={model}, base_url={base_url}")

        return OpenAiChatPromptDriver(
            model=model,
            base_url=base_url,
            api_key="no-key-required",
        )

    @staticmethod
    def _create_openai_driver(provider_config: LlmProviderConfig) -> OpenAiChatPromptDriver:
        """Create OpenAI PromptDriver.

        Expected configuration:
        - model: Model name (e.g., "gpt-4", "gpt-3.5-turbo")
        - api_key in credentials
        - base_url: (optional) Custom API endpoint

        Args:
            provider_config: Provider configuration from Admin Portal.

        Returns:
            Configured OpenAiChatPromptDriver.

        Raises:
            ValueError: If required configuration is missing.
        """
        model = provider_config.model
        if not model:
            raise ValueError("OpenAI configuration missing 'model'")

        # Get API key from credentials property
        api_key = provider_config.api_key
        if not api_key:
            raise ValueError("OpenAI configuration missing API key in credentials")

        # Get optional base_url from config
        base_url = provider_config.config.get("base_url")  # Custom endpoint (e.g., Azure OpenAI)

        logger.info(f"Creating OpenAI driver: model={model}, custom_endpoint={bool(base_url)}")

        kwargs: dict[str, Any] = {
            "model": model,
            "api_key": api_key,
        }

        if base_url:
            kwargs["base_url"] = base_url

        return OpenAiChatPromptDriver(**kwargs)

    @staticmethod
    def _create_anthropic_driver(provider_config: LlmProviderConfig) -> AnthropicPromptDriver:
        """Create Anthropic PromptDriver.

        Expected configuration:
        - model: Model name (e.g., "claude-3-opus-20240229", "claude-3-sonnet-20240229")
        - api_key in credentials

        Args:
            provider_config: Provider configuration from Admin Portal.

        Returns:
            Configured AnthropicPromptDriver.

        Raises:
            ValueError: If required configuration is missing.
        """
        model = provider_config.model
        if not model:
            raise ValueError("Anthropic configuration missing 'model'")

        # Get API key from credentials property
        api_key = provider_config.api_key
        if not api_key:
            raise ValueError("Anthropic configuration missing API key in credentials")

        logger.info(f"Creating Anthropic driver: model={model}")

        return AnthropicPromptDriver(
            model=model,
            api_key=api_key,
        )

def create_prompt_driver(provider_config: LlmProviderConfig) -> BasePromptDriver:
    """Convenience function to create a PromptDriver.

    Args:
        provider_config: Provider configuration from Admin Portal.

    Returns:
        Configured Griptape PromptDriver instance.

    Raises:
        ValueError: If provider type is unsupported or configuration is invalid.
    """
    return DriverFactory.create_driver(provider_config)
