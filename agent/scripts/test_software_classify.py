import asyncio
import json
import sys
import os

# Add agent src to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

from agent.runtime.config_loader import ConfigLoader
from agent.runtime.models import AgentExport, ExampleSetExportInfo


async def test_export_loading():
    """Test 1: Verify the agent can load and parse the export."""
    print("=" * 60)
    print("TEST 1: Export Loading")
    print("=" * 60)

    portal_url = os.environ.get("ADMIN_PORTAL_URL", "http://localhost:5000")
    agent_name = os.environ.get("AGENT_NAME", "test-software-agent")

    loader = ConfigLoader(portal_url, agent_name)

    try:
        export = await loader.load()
        print(f"✓ Agent loaded: {export.agent.name}")
        print(f"  Workflow: {export.workflow.name if export.workflow else 'NONE'}")
        print(f"  Example sets: {list(export.example_sets.keys())}")
        print(f"  Sub-workflows: {list(export.sub_workflows.keys())}")
        print(f"  Rulesets: {list(export.rulesets.keys())}")
        return export
    except Exception as e:
        print(f"✗ Failed to load export: {e}")
        return None


async def test_example_set_content(export: AgentExport):
    """Test 2: Verify example set content is usable."""
    print()
    print("=" * 60)
    print("TEST 2: Example Set Content")
    print("=" * 60)

    # Check for software catalog
    catalog = export.example_sets.get("approved-software-catalog")
    if not catalog:
        print("✗ approved-software-catalog not found in export!")
        print(f"  Available sets: {list(export.example_sets.keys())}")
        return False

    print(f"✓ Software catalog found: {len(catalog.examples)} entries")
    for ex in catalog.examples:
        # Parse the expected output to get install command
        output = json.loads(ex.expected_output_json) if ex.expected_output_json else {}
        install_cmd = output.get("target_resource", "N/A")
        print(f"  - {ex.input_text[:50]}... → {install_cmd}")

    # Check for classification examples
    classify = export.example_sets.get("software-install-examples")
    if not classify:
        print("✗ software-install-examples not found in export!")
        return False

    print(f"\n✓ Classification examples found: {len(classify.examples)} examples")
    for ex in classify.examples:
        output = json.loads(ex.expected_output_json) if ex.expected_output_json else {}
        print(f"  - {ex.input_text[:60]}... → {output.get('ticket_type', '?')}")

    return True


async def test_software_install_workflow(export: AgentExport):
    """Test 3: Verify the software-install-sub workflow structure."""
    print()
    print("=" * 60)
    print("TEST 3: Software Install Sub-Workflow Structure")
    print("=" * 60)

    sw_wf = export.sub_workflows.get("software-install-sub")
    if not sw_wf:
        print("✗ software-install-sub not found in sub_workflows!")
        print(f"  Available: {list(export.sub_workflows.keys())}")
        return False

    print(f"✓ Workflow: {sw_wf.name}")
    print(f"  Steps: {len(sw_wf.steps)}")
    print(f"  Transitions: {len(sw_wf.transitions)}")

    # Verify step types
    step_types = {}
    for step in sw_wf.steps:
        st = step.step_type
        step_types[st] = step_types.get(st, 0) + 1
        print(f"  [{step.sort_order:2d}] {step.name} ({st})")

    print(f"\n  Step type distribution: {dict(step_types)}")

    # Check for Clarify steps
    clarify_steps = [s for s in sw_wf.steps if s.step_type == "Clarify"]
    if clarify_steps:
        print(f"\n✓ Found {len(clarify_steps)} Clarify step(s):")
        for cs in clarify_steps:
            config = cs.configuration or {}
            template = config.get("question_template", "N/A")
            print(f"  - {cs.name}: {template[:80]}...")
    else:
        print("✗ No Clarify steps found!")

    # Check for catalog matching steps (Classify with query_prompt)
    catalog_steps = [s for s in sw_wf.steps
                     if s.step_type == "Classify"
                     and s.configuration
                     and s.configuration.get("query_prompt")]
    if catalog_steps:
        print(f"\n✓ Found {len(catalog_steps)} catalog matching step(s):")
        for cs in catalog_steps:
            print(f"  - {cs.name}: uses example set '{cs.configuration.get('use_example_set')}'")
    else:
        print("⚠ No catalog matching steps found (Classify with query_prompt)")

    return True


async def test_ruleset_content(export: AgentExport):
    """Test 4: Verify rulesets have actual rules."""
    print()
    print("=" * 60)
    print("TEST 4: Ruleset Content")
    print("=" * 60)

    all_good = True
    for name, rs in export.rulesets.items():
        rule_count = len(rs.rules)
        status = "✓" if rule_count > 0 else "✗"
        print(f"{status} {name}: {rule_count} rules")
        if rule_count == 0:
            all_good = False
        else:
            for rule in rs.rules[:3]:  # Show first 3
                print(f"    [{rule.priority}] {rule.rule_text[:70]}...")

    return all_good


async def main():
    print("Software Install Workflow — Integration Verification")
    print("=" * 60)
    print()

    export = await test_export_loading()
    if not export:
        print("\n❌ Cannot proceed without export data.")
        sys.exit(1)

    results = []
    results.append(await test_example_set_content(export))
    results.append(await test_software_install_workflow(export))
    results.append(await test_ruleset_content(export))

    print()
    print("=" * 60)
    passed = sum(1 for r in results if r)
    total = len(results)
    print(f"Results: {passed}/{total} tests passed")

    if all(results):
        print("✓ All verification tests passed!")
        print()
        print("Next: Run end-to-end test with ServiceNow ticket")
    else:
        print("✗ Some tests failed — review output above")
        sys.exit(1)


if __name__ == "__main__":
    asyncio.run(main())
