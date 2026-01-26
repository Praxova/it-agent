# Ticket Classifier

AI-powered ticket classification for the Lucid IT Agent using few-shot prompting with Ollama.

## Overview

The classifier analyzes IT helpdesk tickets and:
1. **Classifies** tickets into predefined types (password reset, group access, file permissions, etc.)
2. **Extracts entities** (usernames, group names, file paths)
3. **Assigns confidence** scores (0.0 to 1.0)
4. **Recommends actions** (proceed, review, or escalate)

## Features

✅ **5 Ticket Types**:
- `password_reset` - Password resets and account lockouts
- `group_access_add` - Add user to AD group
- `group_access_remove` - Remove user from AD group
- `file_permission` - Grant file/folder access
- `unknown` - Everything else (escalate to human)

✅ **Entity Extraction**:
- Affected user (username)
- Target group (AD group name)
- Target resource (file path)

✅ **Confidence-Based Actions**:
- `≥ 0.8`: **Proceed** automatically
- `≥ 0.6`: **Proceed with review**
- `< 0.6`: **Escalate** to human

✅ **Robust Error Handling**:
- Gracefully handles LLM failures
- Parses JSON from markdown code blocks
- Returns safe fallback on errors

## Quick Start

```python
from agent.classifier import TicketClassifier
from connectors import Ticket

# Initialize classifier
classifier = TicketClassifier(
    model="llama3.1",
    base_url="http://localhost:11434",
    temperature=0.1
)

# Classify a ticket
result = classifier.classify(ticket)

print(f"Type: {result.ticket_type}")
print(f"Confidence: {result.confidence:.2f}")
print(f"Action: {result.action_recommended}")
print(f"Affected User: {result.affected_user}")
print(f"Reasoning: {result.reasoning}")

if result.should_escalate:
    print(f"⚠️ Escalate: {result.escalation_reason}")
```

## Integration Test

Test the classifier against live Ollama:

```bash
cd agent
python scripts/test_classifier_integration.py
```

This runs 7 test cases covering all ticket types and edge cases.

**Requirements**:
- Ollama running: `ollama serve`
- Model installed: `ollama pull llama3.1`

## Architecture

```
classifier/
├── models.py          # TicketType enum, ClassificationResult model
├── prompts.py         # System prompt + 8 few-shot examples
├── classifier.py      # TicketClassifier with Griptape Agent
├── tests/
│   ├── test_prompts.py       # 16 tests for prompts
│   ├── test_classifier.py    # 24 tests for classifier
│   └── fixtures/
│       └── sample_tickets.py # 11 sample tickets with expected results
└── README.md
```

## Example Output

**Input Ticket**:
```
Number: INC0010001
Description: I forgot my password and can't log in. Username: jsmith
```

**Classification Result**:
```json
{
  "ticket_type": "password_reset",
  "confidence": 0.95,
  "reasoning": "Clear password reset request from user who explicitly states they forgot their password",
  "affected_user": "jsmith",
  "target_group": null,
  "target_resource": null,
  "should_escalate": false,
  "escalation_reason": null
}
```

**Action**: `proceed` (confidence ≥ 0.8)

## Few-Shot Examples

The classifier uses 8 carefully crafted examples:

1. **Password reset** - Clear request (0.95 confidence)
2. **Group add** - User requesting group access (0.92)
3. **Group remove** - Manager removing user (0.90)
4. **File permission** - Specific path request (0.88)
5. **Hardware issue** - Outside scope, escalate (0.85, escalate)
6. **Vague request** - Insufficient info (0.35, escalate)
7. **Account locked** - Password reset variant (0.90)
8. **Self-removal** - User requesting own removal (0.88)

Examples cover:
- ✅ All 5 ticket types
- ✅ High and low confidence cases
- ✅ Escalation scenarios
- ✅ Entity extraction patterns

## Configuration

### Model Selection

```python
# Use Llama 3.1 (default, best for tool calling)
classifier = TicketClassifier(model="llama3.1")

# Use Mistral
classifier = TicketClassifier(model="mistral")

# Use custom model
classifier = TicketClassifier(model="custom-model:latest")
```

### Temperature

```python
# Low temperature for consistency (recommended)
classifier = TicketClassifier(temperature=0.1)

# Higher temperature for more variety
classifier = TicketClassifier(temperature=0.5)
```

### Batch Processing

```python
# Classify multiple tickets
tickets = [ticket1, ticket2, ticket3]
results = classifier.classify_batch(tickets)

for result in results:
    print(f"{result.ticket_type}: {result.confidence:.2f}")
```

## Testing

### Unit Tests (40 tests)

```bash
cd agent
PYTHONPATH=/home/alton/Documents/lucid-it-agent:$PYTHONPATH pytest src/agent/classifier/tests/ -v
```

**Coverage**:
- 16 prompt tests (structure, examples, schema)
- 24 classifier tests (parsing, classification, error handling)
- All tests pass ✅

### Integration Test

```bash
python scripts/test_classifier_integration.py
```

Tests against live Ollama with 7 real-world scenarios.

## Troubleshooting

### ImportError: cannot import name 'TicketClassifier'

Make sure you're importing from the right path and PYTHONPATH is set:

```bash
export PYTHONPATH=/home/alton/Documents/lucid-it-agent:$PYTHONPATH
```

### Ollama Connection Failed

Ensure Ollama is running:

```bash
# Start Ollama server
ollama serve

# In another terminal, pull model
ollama pull llama3.1

# Test connectivity
curl http://localhost:11434/api/tags
```

### Low Confidence Scores

The classifier may give low confidence if:
- Ticket description is vague or ambiguous
- Request doesn't match known patterns
- Multiple issues in one ticket

**Solution**: These should escalate to humans automatically (confidence < 0.6).

### JSON Parsing Errors

The classifier handles most JSON formats, including:
- Markdown code blocks (`` ```json ... ``` ``)
- Plain JSON objects
- JSON with extra text

If parsing fails, it returns `unknown` with 0.0 confidence and escalates.

## Performance

**Typical Response Times** (with warm model):
- Single classification: ~1-2 seconds
- Batch of 10: ~10-20 seconds

**Accuracy** (based on integration tests):
- Password reset: ~95% confidence
- Group access: ~90% confidence
- File permissions: ~85% confidence
- Hardware/unknown: ~85% confidence (with escalation)

## Next Steps

1. ✅ Classifier implementation (Sprint 2) - **COMPLETE**
2. 🔄 Integrate with ServiceNow connector
3. 🔄 Add decision logic (proceed vs escalate)
4. 🔄 Implement tool execution (Sprint 3)

## API Reference

### TicketType

```python
class TicketType(str, Enum):
    PASSWORD_RESET = "password_reset"
    GROUP_ACCESS_ADD = "group_access_add"
    GROUP_ACCESS_REMOVE = "group_access_remove"
    FILE_PERMISSION = "file_permission"
    UNKNOWN = "unknown"
```

### ClassificationResult

```python
class ClassificationResult(BaseModel):
    ticket_type: TicketType
    confidence: float  # 0.0 to 1.0
    reasoning: str
    affected_user: str | None
    target_group: str | None
    target_resource: str | None
    should_escalate: bool
    escalation_reason: str | None

    @property
    def action_recommended(self) -> str:
        # Returns: "proceed", "proceed_with_review", or "escalate"
```

### TicketClassifier

```python
class TicketClassifier:
    def __init__(
        self,
        model: str = "llama3.1",
        base_url: str = "http://localhost:11434",
        temperature: float = 0.1
    )

    def classify(self, ticket: Ticket) -> ClassificationResult

    def classify_batch(self, tickets: list[Ticket]) -> list[ClassificationResult]
```

## License

Apache 2.0 - See project root LICENSE file
