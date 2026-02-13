# ─────────────────────────────────────────────────────────────────
# Praxova IT Agent — Runtime Container
# ─────────────────────────────────────────────────────────────────
# Multi-stage build: install dependencies in builder, copy to slim runtime

# Stage 1: Builder
FROM python:3.12-slim AS builder

WORKDIR /build

# Install build dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends gcc && \
    rm -rf /var/lib/apt/lists/*

# Copy dependency files first (layer caching)
COPY requirements.txt .
RUN pip install --no-cache-dir --prefix=/install -r requirements.txt

# Stage 2: Runtime
FROM python:3.12-slim AS runtime

WORKDIR /app

# Install curl for entrypoint health check
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Copy installed packages from builder
COPY --from=builder /install /usr/local

# Copy application code
COPY src/ ./src/

# Copy entrypoint script and set permissions
COPY docker-entrypoint.sh /app/
RUN chmod +x /app/docker-entrypoint.sh

# Create non-root user
RUN useradd --create-home --shell /bin/bash agent
USER agent

# ── Configuration via environment variables ──────────────────────
# Required:
ENV ADMIN_PORTAL_URL=http://admin-portal:5000
ENV AGENT_NAME=praxova-agent
#
# Optional (with defaults):
ENV POLL_INTERVAL=30
ENV HEARTBEAT_INTERVAL=60
ENV LOG_LEVEL=INFO
#
# Credentials (set at runtime, never bake into image):
# ENV SERVICENOW_PASSWORD=   (required)
# ENV SERVICENOW_USERNAME=   (optional, default from portal config)
# ─────────────────────────────────────────────────────────────────

# Health check — verify the process is running
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD pgrep -f "agent.runtime.cli" || exit 1

# Python module search path
ENV PYTHONPATH=/app/src

# Entry point
ENTRYPOINT ["/app/docker-entrypoint.sh"]
CMD []
