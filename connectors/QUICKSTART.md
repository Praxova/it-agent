# ServiceNow Connector Quick Start

## 5-Minute Test

### 1. Verify Your Environment

```bash
# Activate virtual environment
source .venv/bin/activate

# Check .env file has credentials
cat .env | grep SERVICENOW
```

Should show:
```
SERVICENOW_INSTANCE=dev341394.service-now.com
SERVICENOW_USERNAME=admin
SERVICENOW_PASSWORD=***
```

### 2. Run Quick Test

```bash
python connectors/test_live_servicenow.py
```

Expected: **4/4 tests passed** ✅

### 3. Create Test Data (Optional)

```bash
# Create a test incident in your PDI
python connectors/create_test_incident.py
```

### 4. See Examples

```bash
# Interactive examples of all operations
python connectors/example_usage.py
```

## Python REPL Quick Test

Open Python and run:

```python
import asyncio
import os
from dotenv import load_dotenv
from connectors import ServiceNowConnector

# Load credentials
load_dotenv()

async def quick_test():
    """Quick test of ServiceNow connector."""
    async with ServiceNowConnector(
        instance=os.getenv("SERVICENOW_INSTANCE"),
        username=os.getenv("SERVICENOW_USERNAME"),
        password=os.getenv("SERVICENOW_PASSWORD"),
    ) as conn:
        # Test 1: Health check
        healthy = await conn.client.health_check()
        print(f"Health check: {'✓ PASS' if healthy else '✗ FAIL'}")

        # Test 2: Poll queue
        tickets = await conn.poll_queue()
        print(f"Open tickets: {len(tickets)}")

        # Test 3: Show first ticket (if any)
        if tickets:
            t = tickets[0]
            print(f"\nFirst ticket:")
            print(f"  {t.number}: {t.short_description}")
            print(f"  State: {t.state.name}")
            print(f"  Priority: {t.priority}")

# Run it
asyncio.run(quick_test())
```

## One-Liners

### Poll for all open tickets

```python
asyncio.run((lambda: ServiceNowConnector(os.getenv("SERVICENOW_INSTANCE"), os.getenv("SERVICENOW_USERNAME"), os.getenv("SERVICENOW_PASSWORD")).poll_queue())())
```

### Test connectivity

```bash
python -c "
import asyncio, os
from dotenv import load_dotenv
from connectors.servicenow.client import ServiceNowClient
load_dotenv()
async def test():
    async with ServiceNowClient(os.getenv('SERVICENOW_INSTANCE'), os.getenv('SERVICENOW_USERNAME'), os.getenv('SERVICENOW_PASSWORD')) as c:
        print('✓ Connected' if await c.health_check() else '✗ Failed')
asyncio.run(test())
"
```

## Common Operations Cheat Sheet

```python
from connectors import ServiceNowConnector, TicketUpdate, TicketState
from datetime import datetime, timedelta

# Initialize
async with ServiceNowConnector(
    instance="dev341394.service-now.com",
    username="admin",
    password="your_password"
) as conn:

    # Poll for tickets (all open)
    tickets = await conn.poll_queue()

    # Poll for tickets (last hour)
    since = datetime.now() - timedelta(hours=1)
    recent = await conn.poll_queue(since=since)

    # Get specific ticket
    ticket = await conn.get_ticket("sys_id_here")

    # Add internal note
    await conn.add_work_note("sys_id_here", "Investigating...")

    # Add customer comment
    await conn.add_comment("sys_id_here", "We're working on it")

    # Update ticket
    update = TicketUpdate(
        state=TicketState.IN_PROGRESS,
        work_notes="Started working"
    )
    await conn.update_ticket("sys_id_here", update)

    # Close ticket
    await conn.close_ticket("sys_id_here", "Issue resolved")
```

## Jupyter Notebook

```python
# Cell 1: Setup
import asyncio
import os
from dotenv import load_dotenv
from connectors import ServiceNowConnector, TicketState
from datetime import datetime, timedelta

load_dotenv()

# Create connector
conn = ServiceNowConnector(
    instance=os.getenv("SERVICENOW_INSTANCE"),
    username=os.getenv("SERVICENOW_USERNAME"),
    password=os.getenv("SERVICENOW_PASSWORD")
)

# Cell 2: Test connection
await conn.client.health_check()

# Cell 3: Get tickets
tickets = await conn.poll_queue()
print(f"Found {len(tickets)} tickets")

# Cell 4: Show ticket details
for t in tickets[:5]:  # First 5
    print(f"{t.number}: {t.short_description}")
    print(f"  State: {t.state.name}, Priority: {t.priority}")

# Cell 5: Clean up when done
await conn.close()
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `ImportError: No module named 'connectors'` | Run from project root, or add to PYTHONPATH |
| `Missing environment variables` | Check `.env` file exists and has SERVICENOW_* vars |
| `Authentication failed` | Verify credentials in `.env` are correct |
| `No tickets found` | Run `create_test_incident.py` to create test data |
| `Connection timeout` | Check PDI instance is accessible via browser |

## Next Steps

1. ✅ Test basic connectivity
2. 🔄 Create test incidents
3. 🔄 Try all operations (poll, update, close)
4. 🔄 Integrate with Griptape agent

See [README.md](README.md) for full documentation.
