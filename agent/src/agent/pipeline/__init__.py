"""Pipeline module for ticket processing."""

from .config import PipelineConfig
from .executor import TicketExecutor

__all__ = ["TicketExecutor", "PipelineConfig"]
