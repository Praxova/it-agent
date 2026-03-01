# Phase 5b — Certificate Auto-Renewal

## Context for the Intermediary Chat

This prompt was produced by a security architecture session. It describes WHAT needs
to exist and WHY. Your job is to compare against the current codebase and produce a
Claude Code implementation prompt.

**Background:** The Praxova internal PKI issues certificates with finite lifetimes:
1 year for server certs (portal, tool server), 90 days for agent client certs. Without
auto-renewal, these certs expire and break inter-component communication. The operator
has to manually re-provision certs — a process that's error-prone and, if forgotten,
causes an outage.

Auto-renewal eliminates this class of outage. Certificates are renewed before they
expire, transparently, with no operator intervention required.

**Dependency:** The internal PKI must exist and be functional (it already is — this
was part of the initial platform). Phase 4c (mTLS) should be complete, since the
agent client cert is one of the certs being auto-renewed.

**Scope boundary:** This phase handles cert renewal for Praxova's OWN certificates
(issued by its internal CA). It does NOT handle renewal of external certificates
(enterprise CA-issued certs, publicly-trusted certs). Enterprise CA integration is
a v2.0 feature.

---

## Specification

### 1. Certificate Inventory

Certs managed by Praxova's internal PKI that need auto-renewal:

| Certificate | Lifetime | Renewal Window | Renewal Mechanism |
|---|---|---|---|
| Admin Portal TLS (server) | 1 year | 30 days before expiry | Self-renewal (portal renews its own cert) |
| Tool Server TLS (server) | 1 year | 30 days before expiry | Portal issues new cert, tool server fetches or is pushed |
| LLM Server TLS (server) | 1 year | 30 days before expiry | Portal issues new cert, deployed to volume |
| Agent Client Cert (mTLS) | 90 days | 30 days before expiry | Agent requests renewal via portal API |

The CA root certificate itself is NOT auto-renewed. It has a 5-year lifetime and
its renewal is a manual, deliberate operation (because it changes the trust root
for all components).

### 2. Portal Self-Renewal (Portal TLS Cert)

The portal can renew its own TLS certificate because it has the CA private key.

**Background task:** A hosted service (ASP.NET Core `IHostedService`) runs daily
and checks the portal's current TLS cert expiry. If the cert expires within 30 days:

1. Generate a new key pair
2. Issue a new cert from the CA with the same SANs
3. Store the new cert in the same location as the current cert
4. Signal Kestrel to reload the certificate (if hot-reload is supported) or
   log a notification that the portal needs a restart to pick up the new cert

**Kestrel cert hot-reload:** ASP.NET Core 8 supports `ServerCertificateSelector`
which can return different certs per connection. An alternative is to use file-based
cert configuration with `reloadOnChange: true`. The intermediary chat should examine
how the portal currently loads its TLS cert and determine the least-disruptive
renewal mechanism.

If hot-reload isn't feasible, the background task writes the new cert and logs a
WARNING: "Portal TLS certificate renewed. Restart the portal to activate the new
certificate." This is acceptable for v1.0 — the portal restart can be scheduled
during a maintenance window.

**Audit event:** `CertificateRenewed` with certificate subject, old expiry, new
expiry, serial numbers.

### 3. Agent Client Cert Renewal

The agent requests a new client cert from the portal before the current one expires.

**Portal endpoint:**

```
POST /api/pki/certificates/renew
Authorization: X-Api-Key <agent-api-key>
Content-Type: application/json

{
  "current_cert_serial": "AB:CD:EF:...",
  "agent_name": "helpdesk-01"
}
```

Response:

```json
{
  "certificate": "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----",
  "private_key": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----",
  "ca_certificate": "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----",
  "expires_at": "2026-06-01T00:00:00Z",
  "serial": "12:34:56:..."
}
```

The portal validates:
- The requesting agent is registered and active
- The `current_cert_serial` matches a cert the portal issued (prevents arbitrary
  cert requests)
- The current cert is within the renewal window (within 30 days of expiry) —
  reject requests for certs that aren't close to expiring

**Agent-side renewal:**

A background task in the agent checks the client cert expiry daily. If within 30
days of expiry:

1. Call `POST /api/pki/certificates/renew`
2. Write the new cert and key to the cert directory
3. Reload the HTTP client's cert configuration (if possible without restart)
4. Log: "Agent client certificate renewed. New cert expires {date}."

If the renewal fails (portal unreachable, request rejected):
1. Log WARNING: "Agent client cert renewal failed: {reason}. Cert expires in
   {days} days. Will retry tomorrow."
2. Retry the next day
3. If cert expires without renewal → the agent's tool server calls will start
   failing (mTLS handshake rejected). Log CRITICAL and escalate.

With a 30-day renewal window and daily retry, there are 30 attempts before failure.
This is generous — even extended portal outages won't cause a cert-related outage
unless they last 30+ days.

### 4. Tool Server Cert Renewal

The tool server runs on Windows as a service. It can't easily call the portal's
PKI API (it's not a Praxova-managed agent in the same way the Python agent is).
Two approaches:

**Option A: Portal pushes renewal.** The portal's background task checks tool
server cert expiry (it knows the expiry because it issued the cert). When within
30 days:
1. Portal generates new cert
2. Portal calls a renewal endpoint on the tool server:
   `POST https://tool01:8443/api/v1/admin/certificate/update`
3. Tool server writes new cert and reloads

This requires the tool server to have a cert update endpoint, which is a
sensitive endpoint that needs careful authentication (must be called only by
the portal). The operation token system could be used here, or a separate
admin auth mechanism.

**Option B: Tool server pulls renewal.** The tool server periodically checks
its own cert expiry and calls the portal to request a new one (similar to the
agent pattern). This requires the tool server to have portal API access and
credentials.

**Option C: Manual with advance warning.** The portal tracks tool server cert
expiry and surfaces warnings in the portal UI starting 30 days before expiry.
The operator re-runs `provision-toolserver-certs.ps1` to renew. This is the
simplest approach and may be sufficient for v1.0, given that tool server certs
have a 1-year lifetime (the operator has 30 days to act on the warning).

For v1.0, I recommend **Option C** — manual renewal with advance warning. The
tool server cert renews once per year, and the provisioning script already exists.
Automated tool server cert renewal is a v1.1 improvement.

**Portal UI warning:** The tool server management page should show cert expiry
status. Color coding: green (>30 days), yellow (7-30 days), red (<7 days),
with a prominent banner at <7 days.

### 5. LLM Server Cert Renewal

The LLM server (llama.cpp) reads its cert from a mounted volume. It doesn't have
a renewal API. Renewal approach:

1. Portal's background task checks the LLM server cert expiry
2. When within 30 days, generate a new cert and write to the certs volume
3. The LLM server container needs to be restarted to pick up the new cert
   (llama.cpp server doesn't support cert hot-reload)
4. Portal logs: "LLM server certificate renewed. Container restart required."

For v1.0, this can be manual (operator restarts the container after seeing the
notification). Automated restart via Docker API is a future enhancement.

### 6. Expiry Monitoring Dashboard

Add a certificate status section to the portal's home page or a dedicated
"Certificate Health" page:

| Component | Certificate | Expires | Status | Action |
|---|---|---|---|---|
| Admin Portal | TLS server cert | 2027-01-15 | ✅ OK (322 days) | — |
| Agent helpdesk-01 | mTLS client cert | 2026-05-01 | ⚠️ Renewing (63 days) | Auto |
| Tool Server tool01 | TLS server cert | 2026-04-01 | 🔴 Expiring (33 days) | Re-provision |
| LLM Server | TLS server cert | 2027-01-15 | ✅ OK (322 days) | — |
| Praxova CA | Root certificate | 2031-02-27 | ✅ OK (5 years) | — |

This gives operators visibility into the entire certificate landscape at a glance.

### 7. Background Task Architecture

The portal's certificate monitoring should be a single `IHostedService` that runs
on a daily schedule:

```csharp
public class CertificateRenewalService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAndRenewPortalCert();
            await CheckToolServerCertExpiry();   // Log warnings only for v1.0
            await CheckLlmServerCertExpiry();    // Log warnings only for v1.0
            // Agent cert renewal is agent-initiated, not portal-initiated
            
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

The task should run at a consistent time (e.g., 2:00 AM local time) to avoid
interfering with normal operations. But for v1.0, a simple 24-hour delay loop
is fine — exact scheduling can be added later.

---

## Testing

**Test 1: Portal self-renewal**
1. Issue a portal cert with a short expiry (e.g., 2 days) for testing
2. Verify the background task detects it and generates a new cert
3. Verify the new cert is valid and has the correct SANs

**Test 2: Agent cert renewal**
1. Issue an agent client cert with a short expiry (e.g., 2 days)
2. Verify the agent's background task detects it and calls the portal
3. Verify the portal issues a new cert
4. Verify the agent uses the new cert for subsequent tool server calls

**Test 3: Renewal request rejection**
1. Agent requests renewal for a cert that expires in 60 days (outside window)
2. Expected: portal rejects with "Certificate not within renewal window"

**Test 4: Expiry warning for tool server**
1. Issue a tool server cert with a short expiry
2. Verify the portal UI shows a warning
3. Verify the portal logs a warning

**Test 5: Full lifecycle**
1. Deploy the stack with standard cert lifetimes
2. Fast-forward time (or use short-lived test certs)
3. Verify all certs renew before expiry
4. Verify no service interruptions during renewal

---

## Git Commit Guidance

```
feat(portal): certificate expiry monitoring background service
feat(portal): portal TLS cert self-renewal
feat(portal): agent cert renewal API endpoint
feat(portal): certificate health dashboard in UI
feat(portal): tool server cert expiry warnings
feat(agent): client cert expiry monitoring and auto-renewal
docs: certificate lifecycle documentation
test: certificate renewal integration tests
```

### What NOT to Change

- Do not modify the CA generation logic
- Do not modify the initial cert issuance logic (first-time provisioning)
- Do not implement CRL or OCSP (that's v2+)
- Do not implement enterprise CA integration (that's v2+)
- Do not automate tool server cert deployment for v1.0 (manual with warning)
- Do not modify the operation token system
