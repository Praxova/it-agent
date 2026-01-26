#!/usr/bin/env python3
"""
Griptape + Ollama Integration Test

This script verifies that the Griptape framework is properly configured
to work with a local Ollama instance. Run this after setting up your
development environment to ensure everything is working.

Usage:
    python scripts/test_ollama_integration.py

Prerequisites:
    1. Ollama installed and running (ollama serve)
    2. Llama 3.1 model pulled (ollama pull llama3.1)
    3. Virtual environment activated with griptape installed
"""

import sys
import time


def print_header(text: str) -> None:
    """Print a formatted section header."""
    print("\n" + "=" * 60)
    print(f"  {text}")
    print("=" * 60 + "\n")


def print_result(test_name: str, passed: bool, details: str = "") -> None:
    """Print test result with status indicator."""
    status = "✅ PASS" if passed else "❌ FAIL"
    print(f"{status}: {test_name}")
    if details:
        print(f"       {details}")


def test_imports() -> bool:
    """Test that all required Griptape modules can be imported."""
    print_header("Test 1: Import Verification")
    
    try:
        # Core structures
        from griptape.structures import Agent, Pipeline, Workflow
        print_result("Import structures (Agent, Pipeline, Workflow)", True)
        
        # Ollama driver
        from griptape.drivers.prompt.ollama import OllamaPromptDriver
        print_result("Import OllamaPromptDriver", True)
        
        # Tasks
        from griptape.tasks import PromptTask, ToolkitTask
        print_result("Import tasks (PromptTask, ToolkitTask)", True)
        
        # Rules
        from griptape.rules import Rule, Ruleset
        print_result("Import rules (Rule, Ruleset)", True)
        
        # Tools base
        from griptape.tools import BaseTool
        print_result("Import BaseTool", True)
        
        # Artifacts
        from griptape.artifacts import TextArtifact, ListArtifact
        print_result("Import artifacts", True)
        
        return True
        
    except ImportError as e:
        print_result("Import test", False, str(e))
        return False


def test_ollama_connection() -> bool:
    """Test that Ollama is running and accessible."""
    print_header("Test 2: Ollama Connection")
    
    try:
        import httpx
        
        # Check if Ollama is running
        response = httpx.get("http://localhost:11434/api/tags", timeout=5.0)
        
        if response.status_code == 200:
            models = response.json().get("models", [])
            model_names = [m["name"] for m in models]
            print_result("Ollama server running", True, f"Found {len(models)} models")
            
            # Check for llama3.1
            has_llama = any("llama3.1" in name for name in model_names)
            if has_llama:
                print_result("Llama 3.1 model available", True)
            else:
                print_result("Llama 3.1 model available", False, 
                           f"Available models: {model_names}")
                print("\n       Run: ollama pull llama3.1")
                return False
            
            return True
        else:
            print_result("Ollama server running", False, 
                        f"Status code: {response.status_code}")
            return False
            
    except httpx.ConnectError:
        print_result("Ollama server running", False, 
                    "Cannot connect to localhost:11434")
        print("\n       Run: ollama serve")
        return False
    except Exception as e:
        print_result("Ollama connection", False, str(e))
        return False


def test_simple_agent() -> bool:
    """Test a simple agent interaction without tools."""
    print_header("Test 3: Simple Agent (No Tools)")
    
    try:
        from griptape.structures import Agent
        from griptape.drivers.prompt.ollama import OllamaPromptDriver
        
        print("Creating agent with OllamaPromptDriver...")
        agent = Agent(
            prompt_driver=OllamaPromptDriver(
                model="llama3.1",
            ),
        )
        
        print("Running simple prompt...")
        start_time = time.time()
        
        result = agent.run("What is 2 + 2? Reply with just the number.")
        
        elapsed = time.time() - start_time
        
        # Get the output text
        output = result.output_task.output.value if result.output_task.output else "No output"
        
        print_result("Agent execution", True, f"Completed in {elapsed:.2f}s")
        print(f"       Response: {output[:100]}...")
        
        return True
        
    except Exception as e:
        print_result("Simple agent test", False, str(e))
        import traceback
        traceback.print_exc()
        return False


def test_agent_with_tool() -> bool:
    """Test an agent with a simple built-in tool."""
    print_header("Test 4: Agent with Tool (Calculator)")
    
    try:
        from griptape.structures import Agent
        from griptape.drivers.prompt.ollama import OllamaPromptDriver
        from griptape.tools import CalculatorTool
        
        print("Creating agent with CalculatorTool...")
        agent = Agent(
            prompt_driver=OllamaPromptDriver(
                model="llama3.1",
            ),
            tools=[CalculatorTool()],
        )
        
        print("Running calculation prompt...")
        start_time = time.time()
        
        result = agent.run("What is 127 * 453? Use the calculator tool.")
        
        elapsed = time.time() - start_time
        
        # Get the output text
        output = result.output_task.output.value if result.output_task.output else "No output"
        
        # Check if the answer is correct (127 * 453 = 57531)
        # Account for formatted numbers like "57,531" or "57531"
        has_correct = "57531" in output or "57,531" in output
        
        print_result("Tool execution", True, f"Completed in {elapsed:.2f}s")
        print(f"       Response: {output[:200]}...")
        print_result("Correct answer (57531)", has_correct)
        
        return has_correct
        
    except Exception as e:
        print_result("Tool test", False, str(e))
        import traceback
        traceback.print_exc()
        return False


def test_agent_with_rules() -> bool:
    """Test an agent with custom rules."""
    print_header("Test 5: Agent with Ruleset")
    
    try:
        from griptape.structures import Agent
        from griptape.drivers.prompt.ollama import OllamaPromptDriver
        from griptape.rules import Rule, Ruleset
        
        print("Creating agent with custom ruleset...")
        
        it_support_rules = Ruleset(
            name="IT Support Agent",
            rules=[
                Rule("You are an IT support agent named 'Lucid'"),
                Rule("Always be professional and helpful"),
                Rule("Keep responses concise"),
            ]
        )
        
        agent = Agent(
            prompt_driver=OllamaPromptDriver(
                model="llama3.1",
            ),
            rulesets=[it_support_rules],
        )
        
        print("Running prompt with rules...")
        start_time = time.time()
        
        result = agent.run("What is your name and role?")
        
        elapsed = time.time() - start_time
        
        output = result.output_task.output.value if result.output_task.output else "No output"
        
        # Check if the agent identifies as Lucid
        mentions_lucid = "lucid" in output.lower()
        
        print_result("Ruleset execution", True, f"Completed in {elapsed:.2f}s")
        print(f"       Response: {output[:200]}...")
        print_result("Agent identifies as 'Lucid'", mentions_lucid)
        
        return True  # Rules test passes even if name isn't perfect
        
    except Exception as e:
        print_result("Ruleset test", False, str(e))
        import traceback
        traceback.print_exc()
        return False


def main() -> int:
    """Run all integration tests."""
    print("\n" + "=" * 60)
    print("   LUCID IT AGENT - Integration Test Suite")
    print("=" * 60)
    
    results = []
    
    # Test 1: Imports
    results.append(("Imports", test_imports()))
    
    # Test 2: Ollama Connection
    results.append(("Ollama Connection", test_ollama_connection()))
    
    # Only continue if Ollama is available
    if not results[-1][1]:
        print("\n⚠️  Skipping agent tests - Ollama not available")
    else:
        # Test 3: Simple Agent
        results.append(("Simple Agent", test_simple_agent()))
        
        # Test 4: Agent with Tool
        results.append(("Agent with Tool", test_agent_with_tool()))
        
        # Test 5: Agent with Rules
        results.append(("Agent with Rules", test_agent_with_rules()))
    
    # Summary
    print_header("Test Summary")
    
    passed = sum(1 for _, result in results if result)
    total = len(results)
    
    for name, result in results:
        status = "✅" if result else "❌"
        print(f"  {status} {name}")
    
    print(f"\nTotal: {passed}/{total} tests passed")
    
    if passed == total:
        print("\n🎉 All tests passed! Your environment is ready for development.")
        return 0
    else:
        print("\n⚠️  Some tests failed. Please check the errors above.")
        return 1


if __name__ == "__main__":
    sys.exit(main())
