# ADR-012: Dynamic Classification Training — Implementation Prompt

## Context

You are implementing ADR-012 for the Lucid IT Agent project. This wires the
ClassifyExecutor (Python) to use dynamic examples from the Admin Portal instead
of its current hardcoded prompt. Most infrastructure already exists — this is
about connecting the last mile.

**Project root**: `/home/alton/Documents/lucid-it-agent`

## Background: What Exists Today

### Python Agent (the consumer)

**`agent/src/agent/runtime/executors/classify.py`** (253 lines):
- `_build_classification_prompt()` has a HARDCODED prompt with hardcoded categories
  (`password_reset`, `group_access_add`, etc.) and 3 inline few-shot examples.
- `_parse_classification_response()` contains a `_TYPE_MAP` dict that translates
  LLM output from underscore-style (`password_reset`) to kebab-style
  (`password-reset`) to match dispatcher transition conditions. This is a bandaid.
- The comment on line ~142 literally says: *"Example sets would come from
  context.export.example_sets. For now, we'll use inline examples."*

**`agent/src/agent/runtime/models.py`**:
- `ExampleSetExportInfo` — Pydantic model with `name`, `display_name`,
  `description`, `examples: list[ExampleExportInfo]`
- `ExampleExportInfo` — Pydantic model with `input_text` (alias `inputText`),
  `expected_output_json` (alias `expectedOutputJson`), `notes`
- `AgentExport.example_sets` — `dict[str, ExampleSetExportInfo]` (alias `exampleSets`)

**`agent/src/agent/runtime/execution_context.py`**:
- `ExecutionContext` is a dataclass. The workflow engine dynamically attaches
  the agent export as `context._agent_export = self.export` (see workflow_engine.py
  line ~142). So executors can access `context._agent_export.example_sets`.

### C# Admin Portal (the producer)

**`admin/dotnet/src/LucidAdmin.Web/Services/AgentExportService.cs`** (516 lines):
- `CollectExampleSets()` already exports the workflow's linked example set.
- `BuildInputText(Example e)` concatenates `TicketShortDescription` + `\n` +
  `TicketDescription`.
- **CRITICAL BUG**: `BuildExpectedOutputJson(Example e)` does
  `example.ExpectedTicketType.ToString()` which produces **PascalCase** enum
  names like `"PasswordReset"`, `"GroupAccessAdd"`, `"FilePermissionGrant"`,
  `"OutOfScope"`. But dispatcher transition conditions use **kebab-case**:
  `password-reset`, `group-membership`, `file-permissions`, `unknown`.
  **This must be fixed** — the exported JSON must use the same strings that
  transition conditions expect.

**`admin/dotnet/src/LucidAdmin.Core/Enums/TicketType.cs`**:
```csharp
public enum TicketType
{
    PasswordReset,        // transitions use: password-reset
    GroupAccessAdd,       // transitions use: group-membership
    GroupAccessRemove,    // transitions use: group-membership
    FilePermissionGrant,  // transitions use: file-permissions
    FilePermissionRevoke, // transitions use: file-permissions
    Unknown,              // transitions use: unknown
    MultipleRequests,     // transitions use: unknown (escalate)
    OutOfScope            // transitions use: unknown (escalate)
}
```

**Seeded data**: `ExampleSetSeeder.cs` seeds three example sets:
- `password-reset-examples` (4 examples, TargetTicketType=PasswordReset)
- `group-access-examples` (4 examples, TargetTicketType=GroupAccessAdd)
- `it-dispatch-classification` (6 examples, mixed types — THIS is the one used
  by the IT-Dispatch workflow)

**Workflow seeder**: The IT-Dispatch workflow's classify step config is:
```json
{"extract_fields": ["ticket_type", "affected_user", "caller_name", "confidence",
 "should_escalate", "escalation_reason"]}
```
Note: It does NOT have `use_example_set`. The example set is linked at the
WORKFLOW level via `WorkflowDefinition.ExampleSetId` → `it-dispatch-classification`.

**Example editor UI**: `Components/Pages/Examples/Index.razor` (300 lines) and
`Components/Pages/Examples/Edit.razor` (263 lines) already exist with MudBlazor
components, CRUD operations, search, filter, and copy functionality.

---

## Implementation Tasks

### Task 1: Fix Export Serialization (C# — CRITICAL)

**File**: `admin/dotnet/src/LucidAdmin.Web/Services/AgentExportService.cs`

**Problem**: `BuildExpectedOutputJson()` produces PascalCase ticket types from
the enum's `ToString()`. The exported JSON must match the transition condition
strings the dispatcher uses.

**Fix**: Add a mapping method that converts `TicketType` enum values to the
kebab-case strings used in dispatcher transition conditions:

```csharp
private static string TicketTypeToClassificationString(TicketType type) => type switch
{
    TicketType.PasswordReset => "password-reset",
    TicketType.GroupAccessAdd => "group-membership",
    TicketType.GroupAccessRemove => "group-membership",
    TicketType.FilePermissionGrant => "file-permissions",
    TicketType.FilePermissionRevoke => "file-permissions",
    TicketType.Unknown => "unknown",
    TicketType.MultipleRequests => "unknown",
    TicketType.OutOfScope => "unknown",
    _ => "unknown"
};
```

Update `BuildExpectedOutputJson` to use this mapping:
```csharp
private string BuildExpectedOutputJson(Example example)
{
    var output = new Dictionary<string, object?>
    {
        ["ticket_type"] = TicketTypeToClassificationString(example.ExpectedTicketType),
        ["confidence"] = example.ExpectedConfidence,
        ["affected_user"] = example.ExpectedAffectedUser,
        ["target_group"] = example.ExpectedTargetGroup,
        ["target_resource"] = example.ExpectedTargetResource,
        ["permission_level"] = example.ExpectedPermissionLevel,
        ["should_escalate"] = example.ExpectedShouldEscalate,
        ["escalation_reason"] = example.ExpectedEscalationReason
    };

    return JsonSerializer.Serialize(output);
}
```

**Also**: Add the example set name to the workflow export so the agent knows which
example set to use without guessing. In `MapWorkflowInfo()`, the `WorkflowExportInfo`
should include a field like `exampleSetName`. If the `WorkflowExportInfo` response
model doesn't have this field, add it.

Check the response model at: `admin/dotnet/src/LucidAdmin.Web/Models/` or
`admin/dotnet/src/LucidAdmin.Web/Api/Models/Responses/` — look for
`WorkflowExportInfo` or `AgentExportModels.cs`.

### Task 2: Update Python Export Models (if needed)

**File**: `agent/src/agent/runtime/models.py`

If Task 1 adds an `exampleSetName` field to the workflow export, add a
corresponding field to `WorkflowExportInfo`:
```python
example_set_name: str | None = Field(None, alias="exampleSetName")
```

### Task 3: Rewrite ClassifyExecutor (Python — MAIN TASK)

**File**: `agent/src/agent/runtime/executors/classify.py`

Replace `_build_classification_prompt()` with a dynamic version that reads from
the agent export's example sets.

**Algorithm**:

```python
def _build_classification_prompt(self, ticket_data, rulesets, example_set_name, context):
    """Build classification prompt dynamically from portal-defined examples."""

    # 1. Resolve example set
    #    Priority: step config "use_example_set" > workflow's linked example set
    #    > first available example set > fallback to no examples
    example_set = self._resolve_example_set(example_set_name, context)

    # 2. Extract valid categories from examples
    #    Parse each example's expected_output_json, collect unique ticket_type values
    categories = self._extract_categories(example_set) if example_set else ["unknown"]

    # 3. Build system instruction with dynamic categories
    #    "Classify into one of: password-reset, group-membership, file-permissions, unknown"
    system_instruction = self._build_system_instruction(categories)

    # 4. Build few-shot section from examples
    #    For each example: show input text and expected JSON output
    few_shot = self._build_few_shot_section(example_set) if example_set else ""

    # 5. Build ticket section
    ticket_section = self._build_ticket_section(ticket_data)

    # 6. Combine
    parts = [system_instruction]
    rules_text = self.build_rules_prompt(rulesets)
    if rules_text:
        parts.append(rules_text)
    if few_shot:
        parts.append(few_shot)
    parts.append(ticket_section)

    return "\n\n".join(parts)
```

**Helper methods to implement**:

```python
def _resolve_example_set(self, example_set_name, context):
    """Find the example set to use."""
    export = getattr(context, '_agent_export', None)
    if not export or not export.example_sets:
        logger.warning("No example sets available in agent export")
        return None

    # If explicit name given (from step config), use it
    if example_set_name and example_set_name in export.example_sets:
        return export.example_sets[example_set_name]

    # Otherwise try the workflow's linked example set
    if export.workflow and hasattr(export.workflow, 'example_set_name'):
        wf_set_name = export.workflow.example_set_name
        if wf_set_name and wf_set_name in export.example_sets:
            return export.example_sets[wf_set_name]

    # Fallback: use first available
    if export.example_sets:
        first_name = next(iter(export.example_sets))
        logger.info(f"No explicit example set configured, using '{first_name}'")
        return export.example_sets[first_name]

    return None


def _extract_categories(self, example_set):
    """Extract unique ticket_type values from examples' expected outputs."""
    categories = set()
    for example in example_set.examples:
        if example.expected_output_json:
            try:
                output = json.loads(example.expected_output_json)
                ticket_type = output.get("ticket_type")
                if ticket_type:
                    categories.add(ticket_type)
            except json.JSONDecodeError:
                logger.warning(f"Could not parse expected output JSON for example")
    # Always include "unknown" as a fallback category
    categories.add("unknown")
    return sorted(categories)


def _build_system_instruction(self, categories):
    """Build the system instruction with dynamic category list."""
    cat_list = ", ".join(f'"{c}"' for c in categories)
    return f"""You are an IT helpdesk ticket classifier. Analyze the ticket and classify it.

Your response MUST be valid JSON with these fields:
- ticket_type: One of {cat_list}
- confidence: A number between 0.0 and 1.0 indicating your confidence
- affected_user: The username of the person the request is about (if identifiable, else null)
- target_group: The AD group name (if this is a group access request, else null)
- target_resource: The file/folder path (if this is a permission request, else null)
- reasoning: Brief explanation of your classification

If the ticket does not clearly match any category, use "unknown" with low confidence.
Respond with ONLY the JSON object, no other text."""


def _build_few_shot_section(self, example_set):
    """Build few-shot examples section from example set data."""
    if not example_set or not example_set.examples:
        return ""

    parts = ["## Classification Examples\n"]
    for i, example in enumerate(example_set.examples, 1):
        if not example.input_text:
            continue
        parts.append(f"Example {i}:")
        parts.append(f"Ticket: \"{example.input_text}\"")
        if example.expected_output_json:
            parts.append(f"Classification:\n```json\n{example.expected_output_json}\n```")
        parts.append("")  # blank line between examples

    return "\n".join(parts)


def _build_ticket_section(self, ticket_data):
    """Build the ticket-to-classify section."""
    short_desc = ticket_data.get("short_description", "")
    description = ticket_data.get("description", "")
    caller = ticket_data.get("caller_id", "Unknown")
    return f"""## Ticket to Classify

**Caller**: {caller}
**Short Description**: {short_desc}
**Description**: {description}

Classify this ticket and respond with ONLY the JSON object:"""
```

**CRITICAL**: Remove the `_TYPE_MAP` dict from `_parse_classification_response()`.
The examples now teach the LLM the correct output format (kebab-case), so no
translation is needed. Keep the JSON parsing, field defaults, and confidence
normalization — just delete the `_TYPE_MAP` and the mapping step that uses it.

The `_parse_classification_response()` method after cleanup should:
1. Extract JSON from response (handle markdown code blocks) — keep this
2. Parse JSON — keep this
3. Apply field defaults (setdefault) — keep this
4. Normalize confidence to float — keep this
5. **DELETE**: The `_TYPE_MAP` dict and the line that does
   `classification["ticket_type"] = _TYPE_MAP.get(raw_type, raw_type)`
6. Return the classification dict

### Task 4: Update Workflow Seeder — Add use_example_set to Classify Step

**File**: `admin/dotnet/src/LucidAdmin.Infrastructure/Data/Seeding/WorkflowSeeder.cs`

The IT-Dispatch workflow's classify step ConfigurationJson should include
`use_example_set` so the agent knows which example set to use even if the
workflow-level link isn't in the export:

Find the classify step in `SeedITDispatchWorkflow()` (around line 565):
```csharp
var classify = new WorkflowStep
{
    Name = "classify",
    DisplayName = "Classify Ticket",
    StepType = StepType.Classify,
    ConfigurationJson = """{"use_example_set": "it-dispatch-classification", "extract_fields": ["ticket_type", "affected_user", "caller_name", "confidence", "should_escalate", "escalation_reason"]}""",
    ...
};
```

Note the key name uses snake_case (`use_example_set`) because the Python agent
reads it as a Python dict. BUT the C# JSON serialization may camelCase it. Check
how `ParseConfigJsonAsObject` works — it deserializes to `Dictionary<string, object?>`
so the raw JSON keys are preserved. Since we're writing raw JSON string in the
seeder, `use_example_set` will stay as-is. Good.

IMPORTANT: Also update the other workflow that has a classify step. Search for
`StepType.Classify` in WorkflowSeeder.cs to find all classify steps. The
`helpdesk-password-reset` workflow (older single-type workflow) has a classify
step that references `password-reset-examples`:
```
ConfigurationJson = """{"useExampleSet": "password-reset-examples"}"""
```
Note the camelCase `useExampleSet` — this is inconsistent with the snake_case
`use_example_set` used above. **Standardize to snake_case** (`use_example_set`)
since the Python agent reads it. Update both occurrences.

### Task 5: Verify End-to-End with Existing Data

After implementing Tasks 1-4:

1. **Delete the existing database** to force re-seeding with updated data:
   ```bash
   rm admin/dotnet/src/LucidAdmin.Web/lucid-admin-dev.db
   ```

2. **Rebuild and restart the Admin Portal**:
   ```bash
   cd admin/dotnet/src/LucidAdmin.Web
   dotnet build
   dotnet run
   ```

3. **Verify the export** includes correctly formatted example data:
   ```bash
   curl -s http://localhost:5000/api/agents/by-name/test-agent/export | python3 -m json.tool
   ```
   Check that:
   - `exampleSets` dict contains `it-dispatch-classification`
   - Each example has `inputText` and `expectedOutputJson`
   - The `expectedOutputJson` uses kebab-case ticket types: `password-reset`,
     `group-membership`, `file-permissions`, `unknown`
   - NOT PascalCase like `PasswordReset`

4. **Run the agent** against a test ticket and verify:
   - Classification uses dynamic examples (check logs for prompt content)
   - No `_TYPE_MAP` translation in logs
   - LLM output matches transition conditions directly
   - Dispatcher routes correctly

### Task 6 (If Time): Add Basic Tests

Add a unit test for the new dynamic prompt builder:

**File**: `agent/tests/test_classify_dynamic.py` (new file)

```python
"""Tests for dynamic classification prompt building."""
import json
import pytest
from agent.runtime.executors.classify import ClassifyExecutor
from agent.runtime.models import ExampleSetExportInfo, ExampleExportInfo

def test_extract_categories_from_examples():
    """Categories should be derived from examples' expected outputs."""
    executor = ClassifyExecutor()
    example_set = ExampleSetExportInfo(
        name="test-set",
        examples=[
            ExampleExportInfo(
                inputText="Reset my password",
                expectedOutputJson=json.dumps({"ticket_type": "password-reset", "confidence": 0.95})
            ),
            ExampleExportInfo(
                inputText="Add me to Finance group",
                expectedOutputJson=json.dumps({"ticket_type": "group-membership", "confidence": 0.90})
            ),
        ]
    )
    categories = executor._extract_categories(example_set)
    assert "password-reset" in categories
    assert "group-membership" in categories
    assert "unknown" in categories  # always included


def test_extract_categories_empty_set():
    """Empty example set should return just 'unknown'."""
    executor = ClassifyExecutor()
    example_set = ExampleSetExportInfo(name="empty-set", examples=[])
    categories = executor._extract_categories(example_set)
    assert categories == ["unknown"]


def test_few_shot_section_format():
    """Few-shot section should include all examples with proper formatting."""
    executor = ClassifyExecutor()
    expected_json = json.dumps({"ticket_type": "password-reset", "confidence": 0.95})
    example_set = ExampleSetExportInfo(
        name="test-set",
        examples=[
            ExampleExportInfo(
                inputText="Reset my password please",
                expectedOutputJson=expected_json
            )
        ]
    )
    section = executor._build_few_shot_section(example_set)
    assert "Example 1:" in section
    assert "Reset my password please" in section
    assert "password-reset" in section


def test_system_instruction_includes_all_categories():
    """System instruction should list all categories from examples."""
    executor = ClassifyExecutor()
    instruction = executor._build_system_instruction(
        ["file-permissions", "group-membership", "password-reset", "unknown"]
    )
    assert '"password-reset"' in instruction
    assert '"group-membership"' in instruction
    assert '"file-permissions"' in instruction
    assert '"unknown"' in instruction


def test_parse_response_no_type_map():
    """Parsed response should NOT translate ticket_type anymore."""
    executor = ClassifyExecutor()
    response = json.dumps({
        "ticket_type": "password-reset",
        "confidence": 0.92,
        "affected_user": "jsmith"
    })
    result = executor._parse_classification_response(response)
    # Should pass through as-is, no _TYPE_MAP translation
    assert result["ticket_type"] == "password-reset"
    assert result["confidence"] == 0.92
```

---

## Files to Modify (Summary)

| File | Change |
|------|--------|
| `admin/dotnet/src/LucidAdmin.Web/Services/AgentExportService.cs` | Fix `BuildExpectedOutputJson` to use kebab-case, add `TicketTypeToClassificationString` mapping, optionally add `exampleSetName` to workflow export |
| `admin/dotnet/src/LucidAdmin.Web/Models/` (find the export response models) | Add `ExampleSetName` field to `WorkflowExportInfo` if needed |
| `agent/src/agent/runtime/models.py` | Add `example_set_name` to `WorkflowExportInfo` if C# side adds it |
| `agent/src/agent/runtime/executors/classify.py` | **MAIN**: Rewrite `_build_classification_prompt`, add helper methods, remove `_TYPE_MAP` from `_parse_classification_response` |
| `admin/dotnet/src/LucidAdmin.Infrastructure/Data/Seeding/WorkflowSeeder.cs` | Add `use_example_set` to classify step configs |
| `agent/tests/test_classify_dynamic.py` | New test file for dynamic classification |

## Important Notes

- The `ExampleExportInfo` Pydantic model uses **aliases**: `inputText` (JSON) maps
  to `input_text` (Python), `expectedOutputJson` maps to `expected_output_json`.
  When accessing these in Python code, use the Python names (snake_case).

- The `context._agent_export` is dynamically attached by the workflow engine.
  Use `getattr(context, '_agent_export', None)` for safe access.

- The `configuration` dict on `WorkflowStepExportInfo` comes from
  `ParseConfigJsonAsObject(step.ConfigurationJson)` which preserves the raw JSON
  keys. So `use_example_set` in the raw JSON stays as `use_example_set` in the
  Python dict.

- After deleting the database and re-seeding, check that the `it-dispatch-classification`
  example set is linked to the IT-Dispatch workflow (via `ExampleSetId` on the
  workflow definition).

- DO NOT modify the `Examples/Index.razor` or `Examples/Edit.razor` files in this
  task. UI polish (Phase D3 from the ADR) will be a separate effort.

## Definition of Done

1. ✅ Export API produces `expectedOutputJson` with kebab-case ticket types
2. ✅ `ClassifyExecutor` builds prompts from portal-defined examples
3. ✅ `_TYPE_MAP` normalization dict is completely removed
4. ✅ All classify step configs in seeder include `use_example_set`
5. ✅ Agent classifies test tickets correctly using dynamic examples
6. ✅ Transition routing works without any hardcoded translation
7. ✅ Unit tests pass for the new prompt builder
