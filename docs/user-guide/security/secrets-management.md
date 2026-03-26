# Secrets Management

Praxova stores credentials — Active Directory bind passwords, ServiceNow
passwords, LLM API keys — in its database. To protect those credentials if
the database file is ever copied or stolen, Praxova encrypts every secret before
writing it.

The encryption relies on a master key that is never written to disk. That master
key is derived from the **unseal passphrase** you provide when the portal starts.
Without the passphrase, the portal cannot decrypt any stored credentials.

This page covers how to configure the passphrase correctly for production, how
the recovery key works, and what happens if both are lost.

---

## How the Encryption Works

Praxova uses envelope encryption — the same design used by HashiCorp Vault and
most dedicated secrets managers.

Each secret (a password, an API key) is encrypted with its own randomly generated
data encryption key (DEK). Each DEK is itself encrypted by a key encryption key
(KEK). The KEK is encrypted by the master key, which is derived from your unseal
passphrase using Argon2id — a memory-hard algorithm specifically designed to make
brute-force guessing expensive.

```
Unseal passphrase
       │  Argon2id
       ▼
  Master Key  ──encrypts──►  Key Encryption Key (KEK)
                                     │  stored encrypted in database
                                     │
                              ──encrypts──►  Data Encryption Keys (DEKs)
                                                    │  one per secret, stored encrypted
                                                    │
                                             ──encrypts──►  Secret values
                                                               (AD password, API keys, etc.)
```

The master key is never stored anywhere. It exists only in memory after the portal
unseals, and it is gone as soon as the portal process stops. A stolen database
file requires the passphrase to be useful.

---

## Development Setup

For local development and testing, set the passphrase in your `.env` file:

```
PRAXOVA_UNSEAL_PASSPHRASE=dev-passphrase-change-in-production
```

This is acceptable for development — `.env` is gitignored and the data is not
production data. Do not use this approach for a production deployment.

---

## Production Setup

In production, the passphrase should live outside the project directory in a
restricted file that only root can read. This prevents it from being accidentally
committed to version control, included in a backup archive of the project, or
read by processes running as a non-root user.

Run the setup script on your Docker host:

```bash
sudo ./scripts/setup-unseal.sh
```

The script creates `/etc/praxova/unseal.env` containing:

```
PRAXOVA_UNSEAL_PASSPHRASE=<your-passphrase>
```

You will be prompted to enter the passphrase. The script sets the following
permissions on the file and its parent directory:

| Path | Owner | Permissions | What this means |
|------|-------|-------------|-----------------|
| `/etc/praxova/` | `root:root` | `700` | Only root can list or enter the directory |
| `/etc/praxova/unseal.env` | `root:root` | `600` | Only root can read or write the file |

Docker Compose reads the file as root when it starts the container, then injects
the passphrase as an environment variable. The container process itself never
has filesystem access to the file — the variable is already in its environment
by the time it starts.

Docker Compose loads the file automatically because `docker-compose.yml` includes:

```yaml
env_file:
  - path: .env
    required: true
  - path: /etc/praxova/unseal.env
    required: false
```

The `required: false` on the second entry means Docker Compose does not fail if
the file does not exist — this keeps development environments working without the
file. In production, the file is present and its value takes precedence over
anything set in `.env`.

### Choosing a Passphrase

Use a randomly generated passphrase of at least 32 characters. A password manager
or the following command will generate a suitable one:

```bash
openssl rand -base64 32
```

Do not reuse a passphrase from another system. Do not use a phrase you can
remember — store it in your password manager.

---

## The Recovery Key

When the portal initializes for the first time, it generates a **recovery key**
alongside the KEK. The recovery key is a second, independent path to unseal the
secrets store — it provides the same access as the passphrase and goes through
the same Argon2id derivation before use.

The recovery key is formatted as eight groups of four uppercase hex characters:

```
XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
```

### Where to Find It

The recovery key is shown once: in the portal startup log immediately after first
initialization, and on the portal dashboard during the initial setup flow.

```bash
docker compose logs admin-portal | grep "recovery key"
```

If you missed it, the recovery key is not retrievable from the portal UI after
the initial display. The only option at that point is to regenerate it (see below),
which produces a new key and invalidates the old one.

### Where to Store It

Store the recovery key separately from the unseal passphrase. The purpose of
having two credentials is that losing one does not mean losing access — but
only if they are in different places.

Suitable storage:
- A different entry in your password manager than the passphrase
- Printed and stored in a physical safe or safety deposit box
- An offline encrypted backup

Do not store both in the same location. If you keep both in the same password
manager account, a compromised account loses both at once.

### Using the Recovery Key

If the unseal passphrase is lost, set the recovery key in the portal's
environment instead:

```bash
# Add to /etc/praxova/unseal.env (or .env for dev)
PRAXOVA_RECOVERY_KEY=XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
```

Restart the portal:

```bash
docker compose restart admin-portal
```

The portal will unseal via the recovery key path. The startup log will contain:

```
Secrets store unsealed via PRAXOVA_RECOVERY_KEY — change the passphrase as soon as possible
```

Once unsealed, immediately set a new passphrase and remove the recovery key from
the environment:

1. Log into the portal
2. Navigate to **Settings → Security**
3. Use **Change Unseal Passphrase** to set a new passphrase
4. Update `/etc/praxova/unseal.env` with the new passphrase
5. Remove `PRAXOVA_RECOVERY_KEY` from the environment

### Regenerating the Recovery Key

If you suspect the recovery key has been exposed, regenerate it from the portal:

Navigate to **Settings → Security → Recovery Key → Regenerate**.

This immediately invalidates the old recovery key and generates a new one.
The portal must be unsealed to perform this action.

---

## If Both the Passphrase and Recovery Key Are Lost

If both credentials are gone, **all stored credentials become unrecoverable**.

The portal will start but remain permanently sealed. It cannot decrypt the KEK,
which means it cannot decrypt any DEKs, which means it cannot decrypt any secrets.
The UI will be accessible and you can log in, but no operation requiring a
credential will work.

Recovery requires a full reset:

1. Stop the portal and delete the database volume:
   ```bash
   docker compose down
   docker volume rm praxova-admin-data
   ```
2. Start fresh:
   ```bash
   docker compose up -d
   ```
3. Re-enter all service account credentials (AD, ServiceNow, LLM provider)
   through the portal UI
4. Re-create tool server registrations and capability mappings
5. Re-generate the agent API key

There is no backdoor. This is intentional — a backdoor that lets Praxova support
recover your secrets is also a backdoor that lets anyone else do the same.

**Back up both credentials. Store them in separate locations. Do this before you
put production data into the system.**

---

## Verifying the Seal State

Check whether the portal is currently unsealed:

```bash
curl -s http://localhost:5000/api/health/ | jq .sealed
```

`false` — unsealed, operating normally.
`true` — sealed, credential operations unavailable.

The portal UI also displays a **SEALED** banner in the header when the secrets
store is locked.

---

## Credential Expiration Tracking

Every service account credential stored in Praxova has an optional expiration date.
The portal tracks these and alerts before they expire. You can view the status of
all stored credentials by navigating to **Service Accounts** in the portal — the
list shows the credential age and expiration date for each account.

No automatic rotation of external credentials occurs in version 1.0. The portal
will alert you that a credential is approaching expiry; rotating it in the external
system and updating it in the portal is a manual step.

---

## Summary: What to Do Before Going to Production

1. Run `sudo ./scripts/setup-unseal.sh` and set a strong randomly-generated passphrase
2. Copy the recovery key from the portal startup log and store it separately from the passphrase
3. Verify the portal health endpoint returns `"sealed": false` after a fresh restart
4. Confirm `/etc/praxova/unseal.env` has permissions `600` and is owned by `root:root`
5. Remove `PRAXOVA_UNSEAL_PASSPHRASE` from your `.env` file if it was set there during initial setup
