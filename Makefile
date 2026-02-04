.PHONY: build up down logs restart status clean test-tickets test-cleanup test-list

# Build the agent container image
build:
	docker compose build

# Start all services
up:
	docker compose up -d

# Stop all services
down:
	docker compose down

# Follow agent logs
logs:
	docker compose logs -f agent-helpdesk-01

# Restart the agent (picks up new config from portal)
restart:
	docker compose restart agent-helpdesk-01

# Show running status
status:
	docker compose ps

# Full rebuild (no cache)
clean:
	docker compose build --no-cache

# Create test tickets in ServiceNow
test-tickets:
	python agent/scripts/create_test_tickets.py --all

# Clean up test tickets
test-cleanup:
	python agent/scripts/create_test_tickets.py --cleanup

# List tickets
test-list:
	python agent/scripts/create_test_tickets.py --list
