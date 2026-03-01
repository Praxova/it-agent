# ADR-016: Remove Tool Server API Key Requirement

**Status:** Accepted  
**Date:** 2026-03-01  
**Deciders:** Alton  

## Context

The tool server setup panel in the admin portal includes an optional API key field,
originally conceived as an authentication mechanism for agent-to-tool-server calls.
During v1.0 E2E security testing, we evaluated whether this layer adds meaningful
security given the other authentication mechanisms now in place.

The current agent-to-tool-server security stack:

| Layer | What it proves |
|-------|---------------|
| **mTLS client certificate** | The caller is a Praxova agent whose certificate was signed by the internal CA (RSA 4096). Cryptographic identity — cannot be forged without the CA private key. |
| **Operation authorization token** | The admin portal explicitly authorized this specific operation (capability + target user + tool server URL) within a 5-minute window. Includes JTI for replay prevention. |

Both layers were validated end-to-end on 2026-03-01, including TLS 1.3
post-handshake authentication and token claim validation on the tool server.

## Decision

**Remove the tool server API key as an authentication requirement.** It will not
ship as a v1.0 feature, and the existing UI field will be removed to avoid confusion.

## Rationale

1. **Redundant with stronger mechanisms.** mTLS provides cryptographic identity
   verification (stronger than a shared secret). The operation token provides
   fine-grained, time-limited, per-operation authorization (stronger than a
   static key). An API key sits between these two layers and proves less than
   either one individually.

2. **Increased operational fragility.** An API key is another secret to generate,
   distribute, configure, rotate, and debug when things go wrong. Every layer
   of authentication is a potential failure point — today's debugging session
   demonstrated how multiple auth layers compound troubleshooting complexity.

3. **Multi-agent isolation is already handled.** The hypothetical value of per-agent
   API keys (restricting which agents can reach which tool servers) is already
   achieved by capability routing. The portal controls which tool servers are
   returned for each capability request. If the portal doesn't return a tool
   server URL, the agent never gets an operation token for it either.

4. **Shared secrets are the weakest auth primitive we have.** Compared to X.509
   certificates and signed JWTs, a static API key is more vulnerable to
   interception, logging accidents, and configuration drift.

## Alternatives Considered

**Keep API key as defense-in-depth.** Rejected because defense-in-depth requires
each layer to cover a *different* threat. The API key covers the same threat as
mTLS (caller identity) but less effectively.

**Replace with per-agent scoped certificates.** If multi-agent isolation at the
tool server level becomes a real requirement in v2.0, the preferred approach is
to scope the mTLS client certificate's CN/SAN to specific agent identities, or
to add agent identity claims to the operation token. Both are already available
to the tool server's validation middleware without additional secrets management.

## Consequences

- The tool server setup panel in the admin portal will have the API key field removed.
- Tool server middleware will validate inbound requests using mTLS + operation tokens only.
- No API key generation, storage, or rotation logic is needed for tool servers.
- Documentation and provisioning scripts will be updated to reflect the two-layer auth model.
- If future requirements demand agent-level isolation at the tool server, ADR-014
  (certificate management) provides the extension point via scoped client certificates.

## Related

- ADR-014: Certificate Management — internal CA and mTLS infrastructure
- ADR-015: Secrets Management — envelope encryption for stored credentials
- ADR-013: Human-in-the-Loop Approval — operation tokens are issued only after approval
