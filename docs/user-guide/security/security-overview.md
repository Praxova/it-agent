# Security Overview

Praxova IT Agent operates with elevated privileges across your Active Directory
environment. It can reset passwords, modify group memberships, and change file
permissions — automatically, on behalf of end users, based on the content of a
ticket. That capability is the point. It is also the reason the security
architecture received serious design attention before the first line of code was
written.

This page explains how Praxova is secured, why it is designed that way, and what
that means practically for your deployment. It is written for IT administrators,
not security researchers — you will not find cryptographic specification detail
here. You will find enough to understand the system's security posture, answer
questions from a security review, and know what to watch out for.

The detailed configuration steps are in the pages linked at the end of each
section.

---

## Why This Matters More Than Usual

Most IT tools have a limited blast radius if they are compromised. A monitoring
tool leaks data. A ticketing system exposes records. Both are bad, but neither
gives an attacker the ability to take action inside your environment.

Praxova is different. Its service account has delegated permission to reset
passwords, modify group memberships, and adjust NTFS permissions across the OUs
you configure it to manage. If those credentials were stolen and misused,
an attacker could lock out users, add themselves to privileged groups, or grant
access to file shares.

This is not a reason to avoid deploying Praxova. It is a reason to understand
how the system protects those credentials and what controls are in place to
detect and limit misuse. Each of the four layers below addresses a different
part of that problem.

---

## The Four Layers

Praxova's security model has four distinct layers. They are not redundant —
each one addresses a different threat.

### Layer 1 — Encrypted Credentials at Rest

Every credential Praxova stores — Active Directory bind passwords, ServiceNow
passwords, LLM API keys — is encrypted before it is written to the database.
Praxova uses envelope encryption: each secret has its own encryption key, and
those keys are themselves encrypted by a master key that is never stored on disk.

The practical consequence: a stolen database file is not useful to an attacker
without the master key. The master key is derived from the unseal passphrase you
provide at startup — it exists only in memory while the portal is running. If the
server is powered off, or if the portal process is killed, no one can decrypt
stored credentials until the passphrase is provided again.

This design is the same approach used by HashiCorp Vault, KeePass, and most
purpose-built secrets managers.

→ [Secrets Management](secrets-management.md)

### Layer 2 — Internal PKI and Encrypted Connections

Every connection between Praxova components — the agent calling the portal, the
agent calling the tool server, the tool server connecting to Active Directory —
uses TLS. Praxova generates its own internal certificate authority at install time
and issues certificates to each component from that CA. You do not need to purchase
certificates or engage an external certificate authority for internal communication.

This means network traffic between components cannot be read or tampered with
by someone with access to your network. It also means that a component presenting
a certificate not issued by the Praxova CA will be rejected.

The connection to Active Directory is LDAPS — encrypted LDAP on port 636. Plain
LDAP on port 389 is not supported for connections that carry credentials.

### Layer 3 — Mutual TLS Between Agent and Tool Server

The agent needs to call the tool server to execute AD operations. That call
carries a request like "reset the password for this user." The tool server needs
to be certain it is the genuine Praxova agent making that request — not another
process on the network that has figured out the tool server's address.

Praxova uses mutual TLS (mTLS) for this connection. Both sides present a
certificate, and both sides verify the other's certificate against the Praxova
internal CA. A request from anything other than a component holding a valid
Praxova-issued certificate is rejected before it is processed.

This is different from normal HTTPS, where only the server proves its identity.
With mTLS, the client must also prove its identity. An attacker who can reach
the tool server's network port cannot send requests that the tool server will
accept without a valid client certificate.

### Layer 4 — Least-Privilege Active Directory Delegation

The service account Praxova uses to connect to Active Directory is not a Domain
Admin account. It holds only the specific delegated permissions required for the
operations it performs: reset passwords on specific OUs, modify group membership
on specific groups, read user attributes, unlock accounts.

These permissions are granted through AD delegation, scoped to the OUs and groups
you designate as Praxova-managed. An error in Praxova's logic, or a compromised
service account, cannot affect AD objects outside those boundaries.

→ [Active Directory Delegation](ad-delegation.md)

---

## The Sealed State

When the Praxova admin portal starts, it attempts to read the unseal passphrase
from its environment. If it succeeds, the secrets store unseals: the master key
is derived and held in memory, and credential operations are available.

If the passphrase is missing, wrong, or the environment variable is not set,
the portal starts in a sealed state. The UI is accessible and you can log in,
but the portal cannot decrypt any stored credentials. Agent operations that
require credentials — which is most of them — will fail. The portal health
endpoint reports `"sealed": true`.

This design means that if the server running the portal is stolen, or if the
database is extracted, the credentials inside are not readable without the
passphrase. The tradeoff is that the passphrase is required on every restart.
For production deployments, this is handled by storing the passphrase in a
separate, restricted file on the server so that Docker can inject it automatically.

→ [Secrets Management — Production Setup](secrets-management.md#production-setup)

---

## What the Security Model Does Not Protect Against

It is worth being clear about the limits.

**A compromised Docker host breaks all of it.** If an attacker has root access to
the server running the Praxova containers, they can read environment variables from
running processes, extract the unseal passphrase, and access the sealed secrets
store. Defense at the host level — OS hardening, access controls, monitoring —
is outside Praxova's scope and remains your responsibility.

**Approved actions are executed without further verification.** Once you approve
a pending action in the portal, Praxova carries it out. The system does not
re-verify that the original ticket was legitimate or that circumstances have not
changed. Approval workflows are a human control; they are only as good as the
person reviewing them.

**The audit log records what happened, not whether it should have.** Every action
is logged, but the log is not a prevention mechanism. If something goes wrong —
a misclassified ticket is approved and executed — the audit log tells you what
happened and when. It does not stop it from happening.

**The tool server runs on a domain-joined Windows host.** If that host is
compromised at the OS level, the service account credentials it uses to connect
to AD are accessible to the attacker. Securing the tool server host is a
standard Windows Server hardening exercise and is outside Praxova's scope.

---

## A Note on Cloud LLM Providers

If you configure Praxova to use a cloud LLM provider — OpenAI, Anthropic, or
Azure OpenAI — ticket content is sent to that provider's API for classification.
That content includes ticket descriptions, caller names, and usernames as they
appear in ServiceNow.

Depending on your organization's data handling requirements and the data
processing agreements you hold with LLM providers, this may or may not be
acceptable. Review your provider's data usage policies before deploying with
a cloud LLM in a regulated environment or with sensitive ticket content.

The local LLM option (llama.cpp server, running in a Docker container on your
own infrastructure) processes all ticket content within your network. No ticket
data leaves your environment.

---

## Security Topics in Detail

| Topic | Where to Find It |
|-------|-----------------|
| Unseal passphrase setup, production configuration, recovery key | [Secrets Management](secrets-management.md) |
| AD service account permissions, delegation steps, verification | [Active Directory Delegation](ad-delegation.md) |
| LDAPS certificate trust configuration | [Verify LDAPS](../installation/verify-ldaps.md) |
| Audit log and what it records | [Daily Operations](../operations/daily-operations.md) |
| Approval gates and what they protect | [Approval Gates](../configuration/approval-gates.md) |
