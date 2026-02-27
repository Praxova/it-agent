# Unseal Passphrase Configuration

## What the Unseal Passphrase Does

The Praxova admin portal stores sensitive credentials (ServiceNow passwords, AD
bind passwords, API keys) using envelope encryption. Each secret is encrypted with
its own data encryption key (DEK), and each DEK is encrypted by a key encryption
key (KEK). The KEK is derived from the **unseal passphrase** using Argon2id — it
is never stored on disk.

When the portal starts, it reads `PRAXOVA_UNSEAL_PASSPHRASE` from the environment,
derives the KEK in memory, and uses it to decrypt DEKs on demand. If the passphrase
is missing or wrong, the portal starts but cannot decrypt any stored credentials —
the secrets store remains **sealed**.

This design means that even if an attacker obtains the database file, they cannot
read credentials without the passphrase. The passphrase is the root of trust for
all stored secrets.

## Production Setup

In production, the unseal passphrase should live in a separate file outside the
project directory, owned by root with restricted permissions:

```bash
sudo ./scripts/setup-unseal.sh
```

This creates `/etc/praxova/unseal.env` containing:

```
PRAXOVA_UNSEAL_PASSPHRASE=<your-passphrase>
```

The file is:
- Owned by `root:root`
- Permissions `600` (read/write by root only)
- Outside the project directory (not in version control, not in `.env`)

Docker Compose loads it via `env_file` in `docker-compose.yml`:

```yaml
env_file:
  - path: .env
    required: true
  - path: /etc/praxova/unseal.env
    required: false
```

The `required: false` means Docker Compose won't fail if the file doesn't exist
(for development environments that use `.env` instead).

## Development Setup

For local development, set the passphrase in `.env`:

```
PRAXOVA_UNSEAL_PASSPHRASE=dev-passphrase-change-in-production
```

This is fine for development — the `.env` file is gitignored and only contains
local credentials. Do not use this approach in production.

## Loading Order

Docker Compose loads environment variables in this order (last wins):

1. `.env` file (via `env_file`)
2. `/etc/praxova/unseal.env` (via `env_file`, if it exists)
3. `environment:` block in `docker-compose.yml`

If `PRAXOVA_UNSEAL_PASSPHRASE` is set in both `.env` and `/etc/praxova/unseal.env`,
the value from `unseal.env` takes precedence. This lets you override a dev default
with the production passphrase without editing `.env`.

## What Happens If the Passphrase Is Lost

If the unseal passphrase is lost, **all stored credentials become unrecoverable**.
The portal will start but remain sealed — it cannot decrypt any DEKs, and therefore
cannot decrypt any secrets.

Recovery requires:
1. Re-entering all credentials manually through the portal UI
2. The portal will re-encrypt them with a new passphrase

There is no backdoor or recovery key (a recovery key mechanism may be added in a
future release).

**Always back up the passphrase** to a secure location: password manager, hardware
security module, or printed and stored in a safe.

## File Permissions

| Path | Owner | Permissions | Purpose |
|------|-------|-------------|---------|
| `/etc/praxova/` | `root:root` | `700` | Praxova configuration directory |
| `/etc/praxova/unseal.env` | `root:root` | `600` | Unseal passphrase |

Docker reads the file as root before dropping privileges into the container. The
container process itself does not need filesystem access to the file — the variable
is injected into the container's environment by Docker Compose.
