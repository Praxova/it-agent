# Phase 4c — mTLS Client Certificate Authentication for Tool Server

## Context for the Intermediary Chat

This prompt was produced by a security architecture session. It describes WHAT needs
to exist and WHY. Your job is to compare against the current codebase and produce a
Claude Code implementation prompt.

**Background:** Phase 3 added operation authorization tokens — every tool server call
requires a portal-issued JWT. This provides per-operation authorization. However, the
tool server does not currently verify the IDENTITY of the caller. Any network client
that can present a valid operation token can call the tool server. mTLS adds the
identity layer: the caller must also present a client certificate signed by the
Praxova CA, proving it is a legitimate Praxova component.

**Why this matters (defense in depth):** If the operation token signing key is
leaked (from the tool server's filesystem, from a backup, from memory dump), an
attacker could forge valid tokens. Without mTLS, forged tokens are sufficient to
execute AD operations. With mTLS, the attacker also needs a valid client certificate
signed by the Praxova CA — a completely separate cryptographic artifact. Compromising
both the signing key AND the CA private key simultaneously is substantially harder
than compromising either one alone.

**Dependency:** Phase 3 (operation tokens) should be complete. mTLS is additive —
it layers on top of token validation, not instead of it. After this phase, the tool
server requires BOTH a valid client certificate AND a valid operation token.

---

## Specification

### 1. Client Certificate Issuance

The portal's internal PKI (established at install time, RSA 4096 root CA) issues a
client certificate for the agent. This certificate is used for mTLS when the agent
calls the tool server.

**Certificate details:**
- Subject: `CN=praxova-agent-helpdesk-01` (or whatever the agent's registered name is)
- Key usage: Digital Signature, Key Encipherment
- Extended key usage: Client Authentication (OID 1.3.6.1.5.5.7.3.2)
- SAN: Not strictly required for client certs, but include the agent name as a
  URI SAN (`URI:praxova:agent:helpdesk-01`) for identification
- Validity: 90 days (matching the certificate lifetime strategy from the architecture
  session)
- Issued by: Praxova internal CA

**Where the cert is generated:** The portal's PKI system generates it. The
intermediary chat should examine how the portal currently generates its own TLS
cert and the tool server cert (via `provision-toolserver-certs.ps1`) and follow
the same pattern.

**When the cert is generated:** Two options:

**Option A: At agent registration time.** When an agent is registered in the portal
(via the UI or API), the portal generates a client cert and stores it. The cert is
then made available for the agent to download (via an authenticated API endpoint)
or deployed to the agent's container via a volume.

**Option B: At first heartbeat.** The agent starts, sends its first heartbeat to
the portal, and the portal generates and returns the client cert in the heartbeat
response. This is more automatic but requires the first heartbeat to be over a
trusted channel (which it already is — HTTPS with the portal's CA).

For v1.0, I recommend **Option A** — generate at registration time and deploy
via volume mount. This is simpler, doesn't require modifying the heartbeat
protocol, and matches the existing cert deployment pattern (the tool server's
cert is provisioned separately, not auto-generated at runtime).

The intermediary chat should check whether there's already a mechanism for
deploying the agent's client cert. The docker-compose.yml may already have a
cert volume mounted into the agent container. If not, one needs to be added.

### 2. Certificate Deployment to Agent Container

The agent's client cert and private key need to be accessible to the agent's
HTTP client. In the Docker deployment:

```yaml
# In docker-compose.yml, agent service:
agent-helpdesk-01:
  # ... existing config ...
  volumes:
    - praxova-agent-certs:/certs/agent:ro
  environment:
    - AGENT_CLIENT_CERT=/certs/agent/agent-client.crt
    - AGENT_CLIENT_KEY=/certs/agent/agent-client.key
```

The cert and key are written to the `praxova-agent-certs` volume during
provisioning (either by a setup script or by the portal's PKI system).

The private key file should have restrictive permissions (readable only by the
agent process user).

### 3. Agent HTTP Client Configuration

The agent's HTTP client (wherever it makes tool server calls) must present the
client certificate on every HTTPS connection to the tool server.

From Phase 3 feedback, the agent has a clean single entry point:
`BaseToolServerTool._make_request()`. The intermediary chat should examine this
method and find where the HTTP request is constructed.

For Python's `requests` library:

```python
response = requests.post(
    url,
    json=payload,
    headers={"Authorization": f"Bearer {operation_token}"},
    cert=(client_cert_path, client_key_path),  # mTLS client cert
    verify=ca_cert_path,  # Verify tool server's cert against Praxova CA
)
```

For `httpx`:

```python
client = httpx.Client(
    cert=(client_cert_path, client_key_path),
    verify=ca_cert_path,
)
response = client.post(url, json=payload, headers={"Authorization": f"Bearer {token}"})
```

The cert paths should come from environment variables or configuration, not
hardcoded. Check whether the agent already has a configured HTTP session/client
for tool server calls and add the cert configuration to it.

### 4. Tool Server Kestrel Configuration

The tool server (ASP.NET Core, running as a Windows Service) needs to require
and validate client certificates. This is configured at the Kestrel level.

```csharp
// In Program.cs or host builder configuration:
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8443, listenOptions =>
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificate = LoadServerCert();
            httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            httpsOptions.ClientCertificateValidation = (cert, chain, errors) =>
            {
                // Validate that the client cert was issued by the Praxova CA
                // Build a chain using only the Praxova CA as the trusted root
                var praxovaCA = LoadPraxovaCA();
                
                using var customChain = new X509Chain();
                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(praxovaCA);
                
                // Require Client Authentication EKU
                customChain.ChainPolicy.ApplicationPolicy.Add(
                    new Oid("1.3.6.1.5.5.7.3.2")); // id-kp-clientAuth
                
                return customChain.Build(cert);
            };
        });
    });
});
```

**Notes for the intermediary chat:**
- Examine how the tool server currently configures Kestrel HTTPS. The server cert
  is already loaded from a file (provisioned by `provision-toolserver-certs.ps1`).
  Add client cert validation to the existing HTTPS configuration.
- `ClientCertificateMode.RequireCertificate` means the TLS handshake fails if the
  client doesn't present a cert. This is the strictest mode.
- The custom chain validation ensures that ONLY certs issued by the Praxova CA are
  accepted — not any cert trusted by the Windows certificate store.
- `RevocationMode.NoCheck` because we're using a private CA without CRL/OCSP
  infrastructure. Certificate revocation is handled by short lifetimes (90 days)
  and by not renewing revoked certs. Full CRL/OCSP is a v2+ feature.
- The Praxova CA cert is already on the tool server (deployed during cert
  provisioning). Check where it's stored and load it from there.

### 5. Health Endpoint Considerations

The tool server's health check endpoint needs to remain accessible for monitoring.
There are two approaches:

**Option A: Health endpoint requires mTLS too.** The portal's health check calls
would need a client cert. Since the portal is already a Praxova component, it could
have its own client cert. This is the most secure option but adds complexity.

**Option B: Separate listener for health checks.** Kestrel listens on two ports:
8443 (mTLS required, for operations) and 8080 (server cert only or plain HTTP, for
health checks). This keeps health monitoring simple.

**Option C: Health endpoint on same port, no client cert required.** Configure
Kestrel's client cert mode to `AllowCertificate` (optional) instead of
`RequireCertificate`, then enforce the requirement in middleware for operation
endpoints only. Health endpoints skip the check.

I recommend **Option C** for v1.0. It keeps a single port, doesn't require the
portal to have a client cert for health checks, and the operation token validation
(Phase 3) already protects the operation endpoints. The mTLS adds identity
verification on top — even without it on health endpoints, no operations can execute
without a valid token.

Implementation with Option C:

```csharp
// Kestrel: AllowCertificate (optional)
httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;

// Middleware: require client cert for operation endpoints
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api/v1/tools"),
    appBuilder => appBuilder.UseMiddleware<RequireClientCertificateMiddleware>()
);
```

The `RequireClientCertificateMiddleware` checks that a client cert was presented
and validates it against the Praxova CA. If no cert or invalid cert → 403.

### 6. Provisioning Script Updates

The `provision-toolserver-certs.ps1` script currently deploys the tool server's
TLS cert and the Praxova CA cert. Extend it to also:

1. Request a client certificate for the agent from the portal's PKI
2. Deploy the client cert and key to the agent's cert volume (or to a location
   accessible by the agent container)
3. Deploy the Praxova CA cert to the tool server's trusted client CA store
   (it may already be there from the existing provisioning)

Alternatively, create a separate script `provision-agent-certs.ps1` for agent
cert provisioning, since the agent is a Linux container and the tool server is
a Windows service — they have different deployment mechanisms.

The intermediary chat should examine the existing provisioning script and decide
the cleanest approach.

### 7. Certificate Renewal

Agent client certs have a 90-day lifetime. Renewal needs to happen before expiry.
For v1.0, renewal can be manual (re-run the provisioning script). Phase 5b (cert
auto-renewal) will automate this.

The tool server should log a WARNING when a client cert is within 30 days of
expiry. This gives operators time to renew manually.

```csharp
// In the client cert validation callback or middleware:
if (cert.NotAfter < DateTime.UtcNow.AddDays(30))
{
    logger.LogWarning("Agent client cert expires in {Days} days. Renew soon.",
        (cert.NotAfter - DateTime.UtcNow).Days);
}
```

---

## Testing

### Positive Tests

**Test 1: Normal operation with mTLS + token**
1. Agent has a valid client cert and requests an operation token
2. Agent calls tool server with both cert and token
3. Expected: operation succeeds

**Test 2: Health check without client cert**
1. Call the health endpoint without presenting a client cert
2. Expected: HTTP 200 (health checks don't require client cert)

### Negative Tests

**Test 3: No client cert, valid token → 403**
```bash
# Valid operation token but no client cert
curl -sk https://tool01:8443/api/v1/tools/password/reset \
  -H 'Authorization: Bearer <valid-token>' \
  -H 'Content-Type: application/json' \
  -d '{"username": "testuser", "new_password": "Test1234!"}'
# Expected: 403 — client certificate required
```

**Test 4: Wrong CA client cert, valid token → 403**
1. Generate a client cert signed by a different CA (not the Praxova CA)
2. Present it with a valid operation token
3. Expected: 403 — client certificate not trusted

**Test 5: Expired client cert, valid token → 403**
1. Generate a client cert with a past expiry date
2. Present it with a valid operation token
3. Expected: 403 — client certificate expired

**Test 6: Valid client cert, no token → 403**
(This should already pass from Phase 3, but verify it still works with mTLS)
1. Present a valid client cert but no operation token
2. Expected: 403 — operation token required

**Test 7: Valid client cert, valid token → success**
(Regression test — the full chain still works)
1. Full end-to-end: agent processes ticket → requests token → calls tool server
2. Expected: operation succeeds, audit trail complete

---

## Git Commit Guidance

```
feat(portal): agent client certificate issuance via PKI
feat(agent): configure mTLS client cert for tool server calls
feat(toolserver): require and validate client certificates (mTLS)
feat(toolserver): client cert expiry warning logging
feat(infra): agent cert volume in docker-compose
docs: update provision-toolserver-certs for mTLS
docs: update DEV-QUICKREF with mTLS setup steps
test: mTLS positive and negative test cases
```

### What NOT to Change

- Do not modify the operation token system from Phase 3 — mTLS is additive
- Do not add client cert requirements to the portal's own API endpoints
  (agent-to-portal communication uses API key auth, not mTLS)
- Do not implement CRL or OCSP — cert revocation is handled by short lifetimes
- Do not implement automatic cert renewal in this phase — that's Phase 5b
- Do not require mTLS for the LLM server (internal Docker network only)
