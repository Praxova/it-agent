"""Data models for capability routing."""

from datetime import datetime

from pydantic import BaseModel, Field


class ToolServerInfo(BaseModel):
    """Information about a Tool Server that provides a capability."""

    id: str = Field(..., description="Tool Server GUID")
    name: str = Field(..., description="Tool Server name")
    display_name: str | None = Field(None, description="Friendly display name")
    url: str = Field(..., description="Tool Server API endpoint")
    domain: str = Field(..., description="Domain/region the server operates in")
    status: str = Field(..., description="Health status (Healthy, Degraded, Offline)")
    last_heartbeat: datetime | None = Field(None, description="Last heartbeat timestamp")


class CapabilityServersResponse(BaseModel):
    """Response from Admin Portal capability routing API."""

    capability: str = Field(..., description="Capability name")
    servers: list[ToolServerInfo] = Field(..., description="List of Tool Servers")
    total_count: int = Field(..., description="Total number of servers")
