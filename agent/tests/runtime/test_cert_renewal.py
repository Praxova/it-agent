"""Tests for CertRenewalManager and AgentRunner cert renewal integration."""
import datetime
import time
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

from agent.runtime.cert_renewal import CertRenewalManager
from agent.runtime.runner import AgentRunner


def _generate_cert_and_key(tmp_path, days_until_expiry: int):
    """Generate a real self-signed cert expiring in the given number of days."""
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "test-agent")])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(subject)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now)
        .not_valid_after(now + datetime.timedelta(days=days_until_expiry))
        .sign(key, hashes.SHA256())
    )
    cert_path = tmp_path / "agent-client.crt"
    key_path = tmp_path / "agent-client.key"
    cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    key_path.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.TraditionalOpenSSL,
            serialization.NoEncryption(),
        )
    )
    return cert_path, key_path, cert


@pytest.fixture
def real_cert_and_key(tmp_path):
    """Generate a real self-signed cert that expires in 20 days (within renewal window)."""
    return _generate_cert_and_key(tmp_path, days_until_expiry=20)


class TestCertRenewalManager:
    """Tests for the CertRenewalManager logic class."""

    def test_needs_renewal_returns_true_when_no_cert_file(self, tmp_path):
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=tmp_path / "nonexistent.crt",
            key_path=tmp_path / "nonexistent.key",
        )
        assert mgr.needs_renewal() is True

    def test_needs_renewal_returns_true_when_within_window(self, real_cert_and_key):
        cert_path, key_path, _ = real_cert_and_key
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=cert_path,
            key_path=key_path,
        )
        # Cert expires in 20 days — within the 30-day renewal window
        assert mgr.needs_renewal() is True

    def test_needs_renewal_returns_false_when_outside_window(self, tmp_path):
        cert_path, key_path, _ = _generate_cert_and_key(tmp_path, days_until_expiry=60)
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=cert_path,
            key_path=key_path,
        )
        # Cert expires in 60 days — outside the 30-day renewal window
        assert mgr.needs_renewal() is False

    def test_get_cert_serial_returns_hex_string(self, real_cert_and_key):
        cert_path, key_path, cert = real_cert_and_key
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=cert_path,
            key_path=key_path,
        )
        serial = mgr.get_cert_serial()
        assert serial is not None
        assert len(serial) > 0
        # Should be lowercase hex
        assert serial == serial.lower()
        assert all(c in "0123456789abcdef" for c in serial)
        # Should match the actual cert serial
        assert serial == format(cert.serial_number, "x")

    def test_get_cert_serial_returns_none_when_no_file(self, tmp_path):
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=tmp_path / "nonexistent.crt",
            key_path=tmp_path / "nonexistent.key",
        )
        assert mgr.get_cert_serial() is None

    @pytest.mark.asyncio
    async def test_renew_success(self, real_cert_and_key, tmp_path):
        cert_path, key_path, _ = real_cert_and_key
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=cert_path,
            key_path=key_path,
        )

        new_cert_pem = "-----BEGIN CERTIFICATE-----\nNEW_CERT\n-----END CERTIFICATE-----"
        new_key_pem = "-----BEGIN PRIVATE KEY-----\nNEW_KEY\n-----END PRIVATE KEY-----"

        mock_response = MagicMock(spec=httpx.Response)
        mock_response.status_code = 200
        mock_response.json.return_value = {
            "certificatePem": new_cert_pem,
            "privateKeyPem": new_key_pem,
            "expiresAt": "2026-06-01T00:00:00Z",
            "serialNumber": "abcd1234",
        }

        mock_client = AsyncMock(spec=httpx.AsyncClient)
        mock_client.post = AsyncMock(return_value=mock_response)

        result = await mgr.renew(mock_client)

        assert result is True
        assert cert_path.read_text() == new_cert_pem
        assert key_path.read_text() == new_key_pem
        mock_client.post.assert_called_once()

    @pytest.mark.asyncio
    async def test_renew_returns_false_on_not_in_renewal_window_response(
        self, real_cert_and_key
    ):
        cert_path, key_path, _ = real_cert_and_key
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=cert_path,
            key_path=key_path,
        )

        mock_response = MagicMock(spec=httpx.Response)
        mock_response.status_code = 400
        mock_response.json.return_value = {
            "error": "not_in_renewal_window",
            "days_remaining": 45,
        }

        mock_client = AsyncMock(spec=httpx.AsyncClient)
        mock_client.post = AsyncMock(return_value=mock_response)

        result = await mgr.renew(mock_client)
        assert result is False

    @pytest.mark.asyncio
    async def test_renew_returns_false_on_network_error(self, real_cert_and_key):
        cert_path, key_path, _ = real_cert_and_key
        mgr = CertRenewalManager(
            portal_url="http://portal:5000",
            agent_name="test-agent",
            api_key="test-key",
            cert_path=cert_path,
            key_path=key_path,
        )

        mock_client = AsyncMock(spec=httpx.AsyncClient)
        mock_client.post = AsyncMock(side_effect=httpx.ConnectError("Connection refused"))

        # Should not raise
        result = await mgr.renew(mock_client)
        assert result is False


class TestAgentRunnerCertRenewal:
    """Tests for AgentRunner._maybe_renew_cert integration."""

    @pytest.fixture
    def runner(self):
        """Create a minimal AgentRunner wired with mocks."""
        r = AgentRunner(
            admin_portal_url="http://test-portal:5000",
            agent_name="test-agent",
        )
        # Simulate post-initialize() state
        r._portal_client = AsyncMock(spec=httpx.AsyncClient)
        r._portal_client.is_closed = False
        return r

    @pytest.mark.asyncio
    async def test_maybe_renew_cert_skips_when_interval_not_elapsed(self, runner):
        runner._last_cert_check_time = time.time()
        runner._cert_renewal = MagicMock()

        await runner._maybe_renew_cert()

        runner._cert_renewal.needs_renewal.assert_not_called()

    @pytest.mark.asyncio
    async def test_maybe_renew_cert_calls_renew_when_needed(self, runner):
        runner._last_cert_check_time = 0  # Expired
        runner._cert_renewal = MagicMock()
        runner._cert_renewal.needs_renewal = MagicMock(return_value=True)
        runner._cert_renewal.renew = AsyncMock(return_value=True)

        await runner._maybe_renew_cert()

        runner._cert_renewal.needs_renewal.assert_called_once()
        runner._cert_renewal.renew.assert_called_once()

    @pytest.mark.asyncio
    async def test_maybe_renew_cert_does_not_raise_on_exception(self, runner):
        runner._last_cert_check_time = 0
        runner._cert_renewal = MagicMock()
        runner._cert_renewal.needs_renewal = MagicMock(side_effect=RuntimeError("boom"))

        # Should NOT raise
        await runner._maybe_renew_cert()
