#!/usr/bin/env python3
"""
Integration tests for Admin Portal connectivity and agent configuration.

Prerequisites:
1. Admin Portal running (http://localhost:5000 by default)
2. An agent created in Admin Portal with LLM service account configured
3. API key created for the agent
4. Environment variables set:
   - LUCID_ADMIN_URL (e.g., http://localhost:5000)
   - LUCID_API_KEY (the API key created for the agent)
   - LUCID_AGENT_NAME (name of the agent in Admin Portal)

Usage:
    python scripts/test_admin_integration.py
"""

import os
import sys
import socket
from datetime import datetime

# Add src to path for imports
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))


def print_header(title: str):
    """Print a formatted section header."""
    print(f"\n{'='*60}")
    print(f" {title}")
    print(f"{'='*60}")


def print_result(test_name: str, passed: bool, message: str = ""):
    """Print a test result."""
    status = "✓ PASS" if passed else "✗ FAIL"
    print(f"  {status}: {test_name}")
    if message:
        print(f"         {message}")


def check_environment():
    """Check required environment variables."""
    print_header("Environment Check")
    
    required_vars = {
        'LUCID_ADMIN_URL': os.environ.get('LUCID_ADMIN_URL'),
        'LUCID_API_KEY': os.environ.get('LUCID_API_KEY'),
        'LUCID_AGENT_NAME': os.environ.get('LUCID_AGENT_NAME'),
    }
    
    all_set = True
    for var, value in required_vars.items():
        if value:
            # Mask sensitive values
            display_value = value if var != 'LUCID_API_KEY' else f"{value[:10]}...{value[-4:]}" if len(value) > 14 else "***"
            print(f"  {var}: {display_value}")
        else:
            print(f"  {var}: NOT SET")
            all_set = False
    
    if not all_set:
        print("\n  ERROR: Missing required environment variables.")
        print("  Please set the following before running tests:")
        print("    export LUCID_ADMIN_URL=http://localhost:5000")
        print("    export LUCID_API_KEY=lk_your_api_key_here")
        print("    export LUCID_AGENT_NAME=your-agent-name")
        return False
    
    return True


def test_admin_client():
    """Test Admin Portal client connectivity."""
    print_header("Test 1: Admin Portal Client")
    
    try:
        from agent.config.admin_client import AdminPortalClient
        
        print("  Creating Admin Portal client...")
        client = AdminPortalClient()
        print_result("Create client", True)
        
        # Test getting agent configuration
        print("  Fetching agent configuration...")
        config = client.get_configuration()
        
        print_result("Connect to Admin Portal", True)
        print_result("Retrieve agent configuration", True)
        
        # Display configuration summary
        print(f"\n  Agent: {config.agent.name}")
        print(f"  Display Name: {config.agent.display_name or 'N/A'}")
        print(f"  Enabled: {config.agent.is_enabled}")
        
        if config.llm_provider:
            print(f"\n  LLM Provider: {config.llm_provider.provider_type}")
            print(f"  LLM Account Type: {config.llm_provider.account_type}")
            if config.llm_provider.model:
                print(f"  LLM Model: {config.llm_provider.model}")
            if config.llm_provider.base_url:
                print(f"  LLM Base URL: {config.llm_provider.base_url}")
        else:
            print("\n  LLM Provider: Not configured")
        
        if config.servicenow:
            print(f"\n  ServiceNow Provider: {config.servicenow.provider_type}")
            if config.servicenow.instance_url:
                print(f"  ServiceNow URL: {config.servicenow.instance_url}")
        else:
            print("\n  ServiceNow: Not configured")
        
        if config.assignment_group:
            print(f"  Assignment Group: {config.assignment_group}")
        
        # Test heartbeat
        print("\n  Sending heartbeat...")
        hostname = socket.gethostname()
        client.send_heartbeat(host_name=hostname, status="Running", tickets_processed=0)
        print_result("Report heartbeat", True)
        
        client.close()
        return True, config
        
    except ImportError as e:
        print_result("Import admin_client module", False, str(e))
        return False, None
    except Exception as e:
        print_result("Admin client test", False, str(e))
        import traceback
        traceback.print_exc()
        return False, None


def test_llm_driver(config):
    """Test LLM driver creation and basic inference."""
    print_header("Test 2: LLM Driver")
    
    if not config or not config.llm_provider:
        print("  SKIPPED: No LLM provider configured")
        return False
    
    try:
        # Check provider type
        provider_type = config.llm_provider.provider_type.lower()
        print(f"  Provider type: {provider_type}")
        
        if provider_type == "llm-ollama":
            return test_ollama_driver(config.llm_provider)
        elif provider_type == "llm-openai":
            return test_openai_driver(config.llm_provider)
        elif provider_type == "llm-anthropic":
            return test_anthropic_driver(config.llm_provider)
        else:
            print_result("Unknown provider", False, f"Provider '{provider_type}' not supported in test")
            return False
            
    except Exception as e:
        print_result("LLM driver test", False, str(e))
        import traceback
        traceback.print_exc()
        return False


def test_ollama_driver(llm_config):
    """Test Ollama driver specifically."""
    try:
        from griptape.drivers.prompt.ollama import OllamaPromptDriver
        from griptape.structures import Agent
        
        model = llm_config.model or "llama3.1"
        base_url = llm_config.base_url or "http://localhost:11434"
        temperature = llm_config.temperature
        
        print(f"  Creating Ollama driver: model={model}, host={base_url}")
        
        driver = OllamaPromptDriver(
            model=model,
            host=base_url,
            options={"temperature": temperature},
        )
        print_result("Create Ollama driver", True)
        
        # Test a simple prompt
        print("\n  Testing inference with simple prompt...")
        agent = Agent(prompt_driver=driver)
        result = agent.run("Say 'Hello from Lucid!' and nothing else.")
        
        response = result.output_task.output.value if hasattr(result.output_task.output, 'value') else str(result.output_task.output)
        print_result("Run test prompt", True)
        print(f"         Response: {response[:100]}")
        
        return True
        
    except ImportError as e:
        print_result("Import Ollama driver", False, str(e))
        print("         You may need to install: pip install -e '.[ollama]'")
        return False
    except Exception as e:
        print_result("Ollama test", False, str(e))
        return False


def test_openai_driver(llm_config):
    """Test OpenAI driver specifically."""
    try:
        from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
        from griptape.structures import Agent
        
        model = llm_config.model or "gpt-3.5-turbo"
        api_key = llm_config.api_key
        
        if not api_key:
            print_result("OpenAI API key", False, "No API key in credentials")
            return False
        
        print(f"  Creating OpenAI driver: model={model}")
        
        driver = OpenAiChatPromptDriver(
            model=model,
            api_key=api_key,
        )
        print_result("Create OpenAI driver", True)
        
        # Test a simple prompt
        print("\n  Testing inference with simple prompt...")
        agent = Agent(prompt_driver=driver)
        result = agent.run("Say 'Hello from Lucid!' and nothing else.")
        
        response = result.output_task.output.value if hasattr(result.output_task.output, 'value') else str(result.output_task.output)
        print_result("Run test prompt", True)
        print(f"         Response: {response[:100]}")
        
        return True
        
    except ImportError as e:
        print_result("Import OpenAI driver", False, str(e))
        print("         You may need to install: pip install -e '.[openai]'")
        return False
    except Exception as e:
        print_result("OpenAI test", False, str(e))
        return False


def test_anthropic_driver(llm_config):
    """Test Anthropic driver specifically."""
    try:
        from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
        from griptape.structures import Agent
        
        model = llm_config.model or "claude-3-sonnet-20240229"
        api_key = llm_config.api_key
        
        if not api_key:
            print_result("Anthropic API key", False, "No API key in credentials")
            return False
        
        print(f"  Creating Anthropic driver: model={model}")
        
        driver = AnthropicPromptDriver(
            model=model,
            api_key=api_key,
        )
        print_result("Create Anthropic driver", True)
        
        # Test a simple prompt
        print("\n  Testing inference with simple prompt...")
        agent = Agent(prompt_driver=driver)
        result = agent.run("Say 'Hello from Lucid!' and nothing else.")
        
        response = result.output_task.output.value if hasattr(result.output_task.output, 'value') else str(result.output_task.output)
        print_result("Run test prompt", True)
        print(f"         Response: {response[:100]}")
        
        return True
        
    except ImportError as e:
        print_result("Import Anthropic driver", False, str(e))
        print("         You may need to install: pip install -e '.[anthropic]'")
        return False
    except Exception as e:
        print_result("Anthropic test", False, str(e))
        return False


def test_full_agent(config):
    """Test creating a complete agent with the configuration."""
    print_header("Test 3: Full Agent Creation")
    
    if not config or not config.llm_provider:
        print("  SKIPPED: No LLM provider configured")
        return False
    
    try:
        from griptape.structures import Agent
        from griptape.rules import Rule, Ruleset
        
        # Create driver based on provider type
        provider_type = config.llm_provider.provider_type.lower()
        
        if provider_type == "llm-ollama":
            from griptape.drivers.prompt.ollama import OllamaPromptDriver
            driver = OllamaPromptDriver(
                model=config.llm_provider.model or "llama3.1",
                host=config.llm_provider.base_url or "http://localhost:11434",
                options={"temperature": config.llm_provider.temperature},
            )
        elif provider_type == "llm-openai":
            from griptape.drivers.prompt.openai import OpenAiChatPromptDriver
            driver = OpenAiChatPromptDriver(
                model=config.llm_provider.model or "gpt-3.5-turbo",
                api_key=config.llm_provider.api_key,
            )
        elif provider_type == "llm-anthropic":
            from griptape.drivers.prompt.anthropic import AnthropicPromptDriver
            driver = AnthropicPromptDriver(
                model=config.llm_provider.model or "claude-3-sonnet-20240229",
                api_key=config.llm_provider.api_key,
            )
        else:
            print_result("Create driver", False, f"Unknown provider: {provider_type}")
            return False
        
        print_result("Create prompt driver", True, type(driver).__name__)
        
        # Create agent with Praxova identity ruleset
        ruleset = Ruleset(
            name="Praxova IT Agent",
            rules=[
                Rule("You are Praxova, an AI-powered IT helpdesk assistant."),
                Rule("You help resolve IT support tickets autonomously."),
                Rule("You are helpful, professional, and concise."),
            ]
        )
        
        agent = Agent(
            prompt_driver=driver,
            rulesets=[ruleset]
        )
        print_result("Create Griptape Agent", True)
        
        # Test the agent
        print("\n  Testing agent with identity question...")
        result = agent.run("Who are you? Answer in one sentence.")
        response = result.output_task.output.value if hasattr(result.output_task.output, 'value') else str(result.output_task.output)
        print_result("Agent responds", True)
        print(f"         Response: {response[:150]}")
        
        return True
        
    except Exception as e:
        print_result("Full agent test", False, str(e))
        import traceback
        traceback.print_exc()
        return False


def main():
    """Run all integration tests."""
    print("\n" + "="*60)
    print(" LUCID IT AGENT - Admin Integration Tests")
    print(f" {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("="*60)
    
    # Check environment
    if not check_environment():
        sys.exit(1)
    
    results = {}
    
    # Test 1: Admin client
    passed, config = test_admin_client()
    results['Admin Client'] = passed
    
    if not passed:
        print("\n  Cannot continue without Admin Portal connection.")
        print_summary(results)
        sys.exit(1)
    
    # Test 2: LLM Driver
    results['LLM Driver'] = test_llm_driver(config)
    
    # Test 3: Full Agent
    results['Full Agent'] = test_full_agent(config)
    
    # Summary
    print_summary(results)
    
    # Exit with appropriate code
    all_passed = all(results.values())
    sys.exit(0 if all_passed else 1)


def print_summary(results: dict):
    """Print test summary."""
    print_header("Test Summary")
    
    passed = sum(1 for v in results.values() if v)
    total = len(results)
    
    for test, result in results.items():
        status = "✓" if result else "✗"
        print(f"  {status} {test}")
    
    print(f"\n  {passed}/{total} tests passed")
    
    if passed == total:
        print("\n  🎉 All tests passed!")
    else:
        print("\n  ⚠️  Some tests failed. Check output above for details.")


if __name__ == "__main__":
    main()
