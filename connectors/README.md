# ServiceNow Connector

A production-ready, async ServiceNow connector for the Praxova IT Agent with comprehensive retry logic and error handling.

## Features

- ✅ Full async/await support with `httpx`
- ✅ Automatic retry with exponential backoff (3 attempts, 2-30s)
- ✅ Type-safe with Pydantic models
- ✅ Context manager support
- ✅ Comprehensive test coverage (33 tests passing)
- ✅ Production-ready error handling

## Testing Against Your PDI

### 1. Quick Health Check

Run the automated test suite against your live ServiceNow PDI:

```bash
source .venv/bin/activate
python connectors/test_live_servicenow.py
```

This will test:
- ✓ ServiceNow connectivity (health check)
- ✓ Queue polling
- ✓ Ticket retrieval
- ✓ Work note operations (dry run)

**Expected output:**
```
======================================================================
ServiceNow Connector Live Test
======================================================================
Instance: dev341394.service-now.com
Username: admin
Assignment Group: Helpdesk
======================================================================
✓ Health check passed - ServiceNow is reachable
✓ Successfully polled queue - Found 0 open tickets
...
Results: 4/4 tests passed
🎉 All tests passed! ServiceNow connector is working correctly.
```

### 2. Create Test Incidents

Since your PDI has no open tickets, create some test data:

```bash
python connectors/create_test_incident.py
```

This will create a sample incident that you can use to test the full connector functionality.

### 3. Interactive Examples

See all connector features in action:

```bash
python connectors/example_usage.py
```

This demonstrates:
- Polling for recent tickets
- Getting ticket details
- Adding work notes
- Adding customer comments
- Updating ticket state
- Closing tickets

**Note:** Examples run in dry-run mode by default. Edit the script to set `dry_run=False` to actually modify tickets.

### 4. Unit Tests

Run the full test suite (mocked, no PDI needed):

```bash
pytest connectors/tests/ -v
```

All 33 tests should pass:
- 15 client tests (retry logic, auth, error handling)
- 18 connector tests (polling, model conversion, ticket operations)

## Usage in Code

### Basic Example

```python
import asyncio
from connectors import ServiceNowConnector
from datetime import datetime, timedelta

async def main():
    async with ServiceNowConnector(
        instance="dev341394.service-now.com",
        username="admin",
        password="your_password",
        assignment_group="Helpdesk"
    ) as connector:
        # Poll for tickets updated in last hour
        since = datetime.now() - timedelta(hours=1)
        tickets = await connector.poll_queue(since=since)

        for ticket in tickets:
            print(f"{ticket.number}: {ticket.short_description}")
            print(f"  State: {ticket.state.name}")
            print(f"  Priority: {ticket.priority}")

asyncio.run(main())
```

### Get Ticket Details

```python
# Get specific ticket by sys_id
ticket = await connector.get_ticket("abc123def456")
print(f"Ticket {ticket.number}")
print(f"Description: {ticket.description}")
print(f"Caller: {ticket.caller_username}")
```

### Add Work Notes (Internal)

```python
# Internal note - not visible to caller
await connector.add_work_note(
    ticket_id="abc123def456",
    note="Investigating issue. Checking user's AD account."
)
```

### Add Comments (Customer-Visible)

```python
# Customer-visible comment
await connector.add_comment(
    ticket_id="abc123def456",
    comment="We are working on your password reset request."
)
```

### Update Ticket

```python
from connectors import TicketUpdate, TicketState

# Update multiple fields
update = TicketUpdate(
    state=TicketState.IN_PROGRESS,
    assigned_to="agent.user",
    work_notes="Started working on this ticket",
    comments="We've received your request and are working on it"
)

updated_ticket = await connector.update_ticket(
    ticket_id="abc123def456",
    update=update
)
```

### Close Ticket

```python
# Close with resolution notes
closed_ticket = await connector.close_ticket(
    ticket_id="abc123def456",
    resolution="Password reset successfully. User can now log in."
)
```

## Configuration

The connector uses environment variables from `.env`:

```bash
# ServiceNow instance (without https://)
SERVICENOW_INSTANCE=dev341394.service-now.com

# ServiceNow credentials
SERVICENOW_USERNAME=admin
SERVICENOW_PASSWORD=your_password_here

# Assignment group to monitor (optional)
SERVICENOW_ASSIGNMENT_GROUP=Helpdesk
```

## Architecture

```
connectors/
├── base.py                      # Abstract base classes
├── servicenow/
│   ├── client.py               # Low-level REST client with retry
│   ├── models.py               # Pydantic models
│   └── connector.py            # High-level connector interface
└── tests/
    ├── test_client.py          # Client tests (15 tests)
    ├── test_connector.py       # Connector tests (18 tests)
    └── fixtures/
        └── incidents.json      # Sample ServiceNow responses
```

## Retry Logic

The connector automatically retries on:
- **Connection errors** (network issues)
- **Timeout errors** (slow responses)
- **HTTP 429** (rate limiting)
- **HTTP 5xx** (server errors)

Configuration:
- **Max attempts:** 3
- **Backoff:** Exponential (2s → 4s → 8s, max 30s)
- **Logging:** Warnings before each retry

## Error Handling

The connector handles:
- ✅ Invalid incidents (skips and logs)
- ✅ Missing fields (graceful defaults)
- ✅ Network failures (automatic retry)
- ✅ Authentication errors (clear error messages)
- ✅ Rate limiting (exponential backoff)

## API Reference

### BaseConnector (Abstract)

All connectors implement this interface:

- `poll_queue(since=None) -> list[Ticket]` - Fetch open tickets
- `get_ticket(ticket_id) -> Ticket` - Get single ticket
- `update_ticket(ticket_id, update) -> Ticket` - Update fields
- `add_work_note(ticket_id, note) -> None` - Add internal note
- `add_comment(ticket_id, comment) -> None` - Add customer comment
- `close_ticket(ticket_id, resolution) -> Ticket` - Close with notes

### Models

**Ticket** - Connector-agnostic ticket representation:
- `id: str` - Unique ID (sys_id)
- `number: str` - Human-readable (INC0010001)
- `short_description: str` - Brief summary
- `description: str | None` - Detailed description
- `state: TicketState` - Current state
- `priority: int` - Priority (1-5)
- `caller_username: str` - Affected user
- `assignment_group: str` - Assigned group
- `created_at: datetime` - Creation time
- `updated_at: datetime` - Last update time

**TicketState** - Enum for ticket states:
- `NEW = 1`
- `IN_PROGRESS = 2`
- `ON_HOLD = 3`
- `RESOLVED = 6`
- `CLOSED = 7`

**TicketUpdate** - Fields that can be updated:
- `state: TicketState | None`
- `assigned_to: str | None`
- `work_notes: str | None`
- `comments: str | None`

## Next Steps

1. ✅ ServiceNow connector implementation (Sprint 1) - **COMPLETE**
2. 🔄 Create test incidents in PDI
3. 🔄 Integrate with Griptape agent (Sprint 2)
4. 🔄 Add tool implementations (password reset, AD groups, etc.)

## Troubleshooting

### Authentication Failed

Check your `.env` file has correct credentials:
```bash
cat .env
```

### No Tickets Found

Create test incidents:
```bash
python connectors/create_test_incident.py
```

### Connection Timeout

Check your ServiceNow instance is accessible:
```bash
curl https://dev341394.service-now.com/api/now/table/incident?sysparm_limit=1
```

### Import Errors

Ensure dependencies are installed:
```bash
pip install -e "agent[dev]"
```
