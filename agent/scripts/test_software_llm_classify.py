import asyncio
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

from griptape.drivers.prompt.ollama import OllamaPromptDriver

from agent.runtime.config_loader import ConfigLoader
from agent.runtime.execution_context import ExecutionContext
from agent.runtime.executors.classify import ClassifyExecutor


async def test_dispatcher_classification():
    """Test dispatcher classifies a software install ticket correctly."""
    print("TEST: Dispatcher Classification of Software Install Ticket")
    print("=" * 60)

    # Load export
    portal_url = os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000")
    agent_name = os.environ.get("AGENT_NAME", "test-software-agent")
    loader = ConfigLoader(portal_url, agent_name)
    export = await loader.load()

    # Create LLM driver
    llm_config = export.llm_provider.config if export.llm_provider else {}
    driver = OllamaPromptDriver(
        model=llm_config.get("model", "llama3.1"),
        host=llm_config.get("endpoint", "http://localhost:11434"),
    )

    # Create execution context with test ticket
    context = ExecutionContext(
        ticket_id="TEST001",
        ticket_data={
            "short_description": "Install 7-Zip on my workstation",
            "description": "Please install 7-Zip on workstation WS-REBEL-001. I need it for extracting archive files.",
            "caller_id": "luke.skywalker",
            "number": "INC0010099",
        },
        llm_driver=driver,
    )
    # Attach export for example set resolution
    context._agent_export = export

    # Find the classify step in the dispatcher workflow
    classify_step = None
    if export.workflow:
        for step in export.workflow.steps:
            if step.step_type == "Classify":
                classify_step = step
                break

    if not classify_step:
        print("✗ No Classify step found in dispatcher workflow")
        return

    print(f"Using classify step: {classify_step.name}")
    print(f"  Example set: {classify_step.configuration.get('use_example_set', 'default')}")

    # Get rulesets
    rulesets = export.rulesets

    # Run classification
    executor = ClassifyExecutor()
    result = await executor.execute(classify_step, context, rulesets)

    print(f"\nResult status: {result.status}")
    print(f"Output: {json.dumps(result.output, indent=2) if result.output else 'None'}")

    # Check result
    if result.output:
        ticket_type = result.output.get("ticket_type", "unknown")
        confidence = result.output.get("confidence", 0)
        print(f"\n{'✓' if ticket_type == 'software-install' else '✗'} "
              f"ticket_type: {ticket_type} (expected: software-install)")
        print(f"{'✓' if confidence >= 0.7 else '⚠'} confidence: {confidence}")


async def test_catalog_matching():
    """Test catalog matching resolves software against approved catalog."""
    print()
    print("TEST: Catalog Matching — Resolve Software Name")
    print("=" * 60)

    # Load export
    portal_url = os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000")
    agent_name = os.environ.get("AGENT_NAME", "test-software-agent")
    loader = ConfigLoader(portal_url, agent_name)
    export = await loader.load()

    # Create LLM driver
    llm_config = export.llm_provider.config if export.llm_provider else {}
    driver = OllamaPromptDriver(
        model=llm_config.get("model", "llama3.1"),
        host=llm_config.get("endpoint", "http://localhost:11434"),
    )

    # Context with software_name already extracted from prior classification
    context = ExecutionContext(
        ticket_id="TEST002",
        ticket_data={
            "short_description": "Install 7-Zip on my workstation",
            "description": "Please install 7-Zip on workstation WS-REBEL-001.",
            "caller_id": "luke.skywalker",
        },
        llm_driver=driver,
    )
    context._agent_export = export
    context.set_variable("software_name", "7-Zip")

    # Find the resolve-software step in the sub-workflow
    sw_wf = export.sub_workflows.get("software-install-sub")
    if not sw_wf:
        print("✗ software-install-sub not found")
        return

    resolve_step = None
    for step in sw_wf.steps:
        if step.name == "resolve-software":
            resolve_step = step
            break

    if not resolve_step:
        print("✗ resolve-software step not found")
        return

    print(f"Using step: {resolve_step.name}")
    print(f"  Example set: {resolve_step.configuration.get('use_example_set')}")
    print(f"  Query prompt: {resolve_step.configuration.get('query_prompt', 'N/A')[:80]}...")

    # Run catalog matching
    executor = ClassifyExecutor()
    rulesets = export.rulesets
    result = await executor.execute(resolve_step, context, rulesets)

    print(f"\nResult status: {result.status}")
    print(f"Output: {json.dumps(result.output, indent=2) if result.output else 'None'}")

    if result.output:
        matched = result.output.get("matched_software", "unknown")
        cmd = result.output.get("install_command", "N/A")
        conf = result.output.get("match_confidence", 0)
        print(f"\n{'✓' if matched != 'no_match' else '✗'} matched_software: {matched}")
        print(f"  install_command: {cmd}")
        print(f"  match_confidence: {conf}")


async def main():
    await test_dispatcher_classification()
    await test_catalog_matching()


if __name__ == "__main__":
    asyncio.run(main())
