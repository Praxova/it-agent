"""FastAPI application for Tool Server."""

import logging
from contextlib import asynccontextmanager
from typing import AsyncGenerator

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from tool_server.api.routes import router
from tool_server.config import Settings

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)

logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """Application lifespan events.

    Handles startup and shutdown tasks.

    Args:
        app: FastAPI application instance.

    Yields:
        None during application lifetime.
    """
    # Startup
    logger.info("Starting Tool Server")

    # Load settings for logging
    try:
        settings = Settings()
        logger.info(f"LDAP Server: {settings.ldap_server}:{settings.ldap_port}")
        logger.info(f"Base DN: {settings.ldap_base_dn}")
    except Exception as e:
        logger.warning(f"Could not load settings: {e}")

    yield

    # Shutdown
    logger.info("Shutting down Tool Server")


# Create FastAPI app
app = FastAPI(
    title="Lucid IT Agent - Tool Server",
    description="HTTP API for IT automation tools (Active Directory, file systems, etc.)",
    version="0.1.0",
    lifespan=lifespan,
)

# Add CORS middleware (configure allowed origins as needed)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Adjust for production
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include API routes
app.include_router(router, prefix="/api/v1", tags=["tools"])


@app.get("/", summary="Root endpoint")
async def root() -> dict[str, str]:
    """Root endpoint with service information.

    Returns:
        Dictionary with service name and version.
    """
    return {
        "service": "Lucid IT Agent - Tool Server",
        "version": "0.1.0",
        "docs": "/docs",
    }


if __name__ == "__main__":
    import uvicorn

    settings = Settings()
    uvicorn.run(
        "tool_server.main:app",
        host=settings.host,
        port=settings.port,
        reload=settings.reload,
        log_level="info",
    )
