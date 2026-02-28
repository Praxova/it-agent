"""Agent client certificate renewal manager.

Encapsulates all cert renewal logic: expiry checking, serial extraction,
and renewal via the portal API. Called from AgentRunner on a 24-hour cycle.
"""
from __future__ import annotations

import logging
import os
from datetime import datetime, timedelta, timezone
from pathlib import Path

import httpx
from cryptography import x509
from cryptography.hazmat.primitives.serialization import Encoding

logger = logging.getLogger(__name__)

RENEWAL_WINDOW_DAYS = 30
CERT_DIR = Path(os.environ.get("AGENT_CERT_DIR", "/tmp/praxova"))


class CertRenewalManager:
    """Manages scheduled renewal of the agent's mTLS client certificate.

    This is a pure logic class — not a background task. The runner calls
    its methods from a background check cycle.
    """

    def __init__(
        self,
        portal_url: str,
        agent_name: str,
        api_key: str,
        cert_path: Path | None = None,
        key_path: Path | None = None,
    ):
        self.portal_url = portal_url
        self.agent_name = agent_name
        self.api_key = api_key
        self.cert_path = cert_path or (CERT_DIR / "agent-client.crt")
        self.key_path = key_path or (CERT_DIR / "agent-client.key")

    def _load_cert(self) -> x509.Certificate | None:
        """Load and parse the PEM certificate from disk. Returns None on failure."""
        try:
            pem_data = self.cert_path.read_bytes()
            return x509.load_pem_x509_certificate(pem_data)
        except FileNotFoundError:
            return None
        except Exception as e:
            logger.warning(f"Failed to parse certificate at {self.cert_path}: {e}")
            return None

    def needs_renewal(self) -> bool:
        """Check if the agent client cert is within the renewal window.

        Returns True if:
        - The cert file does not exist
        - The cert cannot be parsed
        - The cert expires within RENEWAL_WINDOW_DAYS days
        """
        cert = self._load_cert()
        if cert is None:
            return True

        remaining = cert.not_valid_after_utc - datetime.now(timezone.utc)
        return remaining <= timedelta(days=RENEWAL_WINDOW_DAYS)

    def get_cert_serial(self) -> str | None:
        """Return the cert serial number as a lowercase hex string, or None."""
        cert = self._load_cert()
        if cert is None:
            return None
        return format(cert.serial_number, "x")

    async def renew(self, client: httpx.AsyncClient) -> bool:
        """Request a renewed certificate from the portal.

        Returns True if renewal succeeded and new cert was written to disk.
        Returns False on any failure (does not raise).
        """
        serial = self.get_cert_serial()
        if serial is None:
            logger.warning("Cannot renew — unable to read current cert serial")
            return False

        url = f"{self.portal_url}/api/pki/certificates/renew"
        payload = {
            "currentCertSerial": serial,
            "agentName": self.agent_name,
        }
        headers = {"X-API-Key": self.api_key}

        try:
            response = await client.post(url, json=payload, headers=headers)
        except Exception as e:
            days = self._days_remaining_label()
            logger.warning(
                f"Agent client cert renewal failed: {e}. "
                f"Cert expires in {days} days. Will retry tomorrow."
            )
            return False

        if response.status_code == 200:
            return self._handle_success(response)

        if response.status_code == 400:
            return self._handle_bad_request(response)

        # Unexpected status
        days = self._days_remaining_label()
        logger.warning(
            f"Agent client cert renewal failed: HTTP {response.status_code}. "
            f"Cert expires in {days} days. Will retry tomorrow."
        )
        return False

    def _handle_success(self, response: httpx.Response) -> bool:
        """Process a successful renewal response. Returns True on success."""
        try:
            data = response.json()
            cert_pem = data["certificatePem"]
            key_pem = data["privateKeyPem"]
            expires_at = data.get("expiresAt", "unknown")
            serial_number = data.get("serialNumber", "unknown")
        except (KeyError, ValueError) as e:
            logger.warning(f"Agent client cert renewal failed: bad response JSON: {e}")
            return False

        # Ensure directory exists
        self.cert_path.parent.mkdir(parents=True, exist_ok=True)

        # Write cert and key
        self.cert_path.write_text(cert_pem)
        self.key_path.write_text(key_pem)

        # Restrict key permissions on non-Windows
        if not os.name == "nt":
            os.chmod(self.key_path, 0o600)

        logger.info(
            f"Agent client certificate renewed. "
            f"New cert expires {expires_at}. Serial: {serial_number}"
        )
        logger.info(
            "Note: restart the agent container to reload the new client cert "
            "for mTLS tool server calls."
        )
        return True

    def _handle_bad_request(self, response: httpx.Response) -> bool:
        """Handle a 400 response. Returns False always."""
        try:
            data = response.json()
        except ValueError:
            data = {}

        error = data.get("error", "")
        if error == "not_in_renewal_window":
            days = data.get("days_remaining", "?")
            logger.debug(
                f"Renewal request rejected — cert not yet in renewal window "
                f"(days_remaining={days})"
            )
        else:
            days = self._days_remaining_label()
            logger.warning(
                f"Agent client cert renewal failed: {data.get('detail', error)}. "
                f"Cert expires in {days} days. Will retry tomorrow."
            )
        return False

    def _days_remaining_label(self) -> str:
        """Return human-readable days remaining, or '?' if cert unreadable."""
        cert = self._load_cert()
        if cert is None:
            return "?"
        remaining = (cert.not_valid_after_utc - datetime.now(timezone.utc)).days
        return str(remaining)
