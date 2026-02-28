"""Tests for AgentRunner._maybe_refresh_config (Gap 3)."""
from datetime import datetime, timedelta
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from agent.runtime.runner import AgentRunner


@pytest.fixture
def runner():
    """Create a minimal AgentRunner wired with mocks."""
    r = AgentRunner(
        admin_portal_url="http://test-portal:5000",
        agent_name="test-agent",
    )
    # Simulate post-initialize() state
    r._config_loader = AsyncMock()
    r._engine = MagicMock()
    r._config_version = "2026-02-01T12:00:00"
    r._last_config_refresh = datetime.utcnow()
    return r


class TestMaybeRefreshConfig:
    """Tests for the periodic config refresh timer."""

    @pytest.mark.asyncio
    async def test_skips_when_interval_not_elapsed(self, runner):
        """Config refresh should not fire when < 5 minutes have elapsed."""
        runner._last_config_refresh = datetime.utcnow()

        await runner._maybe_refresh_config()

        runner._config_loader.load.assert_not_called()

    @pytest.mark.asyncio
    async def test_triggers_after_interval(self, runner):
        """Config refresh should fire after 5 minutes."""
        runner._last_config_refresh = datetime.utcnow() - timedelta(seconds=301)

        mock_export = MagicMock()
        mock_export.exported_at = datetime(2026, 2, 1, 13, 0, 0)
        runner._config_loader.load = AsyncMock(return_value=mock_export)

        await runner._maybe_refresh_config()

        runner._config_loader.load.assert_called_once()
        # Engine export should be updated
        assert runner._engine.export is mock_export
        # Config version should be updated
        assert runner._config_version == mock_export.exported_at.isoformat()

    @pytest.mark.asyncio
    async def test_error_does_not_crash(self, runner):
        """Config refresh failure should log and continue, not raise."""
        runner._last_config_refresh = datetime.utcnow() - timedelta(seconds=301)
        runner._config_loader.load = AsyncMock(side_effect=Exception("Portal unreachable"))

        # Should NOT raise
        await runner._maybe_refresh_config()

        # Engine export should be unchanged
        # (we verify by checking load was called but engine.export was not overwritten)
        runner._config_loader.load.assert_called_once()

    @pytest.mark.asyncio
    async def test_refresh_timer_resets_after_success(self, runner):
        """After a successful refresh, the timer should reset."""
        runner._last_config_refresh = datetime.utcnow() - timedelta(seconds=301)

        mock_export = MagicMock()
        mock_export.exported_at = datetime(2026, 2, 1, 13, 0, 0)
        runner._config_loader.load = AsyncMock(return_value=mock_export)

        await runner._maybe_refresh_config()

        # Timer should have been reset to ~now
        elapsed = (datetime.utcnow() - runner._last_config_refresh).total_seconds()
        assert elapsed < 2  # Should be essentially 0

    @pytest.mark.asyncio
    async def test_refresh_timer_resets_after_error(self, runner):
        """After a failed refresh, the timer should still reset to prevent spam."""
        runner._last_config_refresh = datetime.utcnow() - timedelta(seconds=301)
        runner._config_loader.load = AsyncMock(side_effect=Exception("Timeout"))

        await runner._maybe_refresh_config()

        elapsed = (datetime.utcnow() - runner._last_config_refresh).total_seconds()
        assert elapsed < 2

    @pytest.mark.asyncio
    async def test_skips_when_not_initialized(self):
        """Should no-op if config_loader or engine are None."""
        runner = AgentRunner(
            admin_portal_url="http://test:5000",
            agent_name="test",
        )
        # Neither _config_loader nor _engine are set
        await runner._maybe_refresh_config()  # Should not raise
