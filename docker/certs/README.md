# TLS Certificate Configuration

## Quick Start (Development)

Run the setup script:
```bash
./scripts/setup-dev-certs.sh
```

This generates locally-trusted certificates and configures the container
to trust your local CA. Then rebuild:
```bash
docker compose build admin-portal
docker compose up -d
```

Browse to: https://admin.praxova.local:5001

## Certificate Files

### TLS Certificate (docker/certs/)
- `cert.pem` — TLS certificate served by the portal
- `key.pem` — Private key for the TLS certificate
- These files are git-ignored. Each developer/deployment generates their own.

### Encryption Key (docker/certs/)
- `encryption.key` — AES-256 key for encrypting credentials stored in the database
- Generated automatically by `setup-dev-certs.sh`
- **CRITICAL:** Back up this key! If lost, all database-stored credentials become unrecoverable
- This file is git-ignored. Each deployment generates or provides its own.

### CA Trust (docker/certs/ca-trust/)
- Place CA root certificates here (`.crt` format) to trust them inside the container
- Used for internal/private CAs and development self-signed CAs
- Files here are git-ignored except `.gitkeep`
- The container runs `update-ca-certificates` at build time to trust these

## Deployment Scenarios

### Development (mkcert)
Run `./scripts/setup-dev-certs.sh` — it handles everything automatically.

The setup script also generates an encryption key for the database credential
store. This key is used to encrypt service account passwords, API keys, and
other secrets stored in the Admin Portal database.

### Enterprise / Internal CA
If your organization uses an internal Certificate Authority:

1. Place your TLS certificate and key in `docker/certs/`:
   ```
   docker/certs/cert.pem    # Your portal's TLS certificate
   docker/certs/key.pem     # Your portal's private key
   ```

2. Place your CA root certificate in `docker/certs/ca-trust/`:
   ```
   docker/certs/ca-trust/internal-ca.crt
   ```

3. Generate or provide an encryption key for credential storage:
   ```
   # Generate a new key:
   openssl rand -base64 32 > docker/certs/encryption.key
   chmod 600 docker/certs/encryption.key

   # Or copy an existing key from your secrets management system
   ```
   **Important:** Back up this key securely. If the key is lost, all
   database-stored credentials must be re-entered.

4. Rebuild the container:
   ```bash
   docker compose build admin-portal
   docker compose up -d
   ```

**Note:** If your CA is already trusted on all client machines (e.g., distributed
via Group Policy), you only need step 1. Step 2 is needed so the portal container
itself trusts the CA for internal loopback HTTPS calls.

### Public CA (Let's Encrypt, DigiCert, etc.)
1. Place your certificate and key in `docker/certs/`:
   ```
   docker/certs/cert.pem
   docker/certs/key.pem
   ```

2. No CA trust configuration needed — public CAs are already trusted by the
   base container image.

3. Generate or provide an encryption key for credential storage:
   ```
   openssl rand -base64 32 > docker/certs/encryption.key
   chmod 600 docker/certs/encryption.key
   ```
   **Important:** Back up this key securely.

4. Rebuild and start:
   ```bash
   docker compose build admin-portal
   docker compose up -d
   ```

### Certificate Renewal
Replace the files in `docker/certs/` and restart the container:
```bash
docker compose restart admin-portal
```

For automated renewal (e.g., Let's Encrypt with certbot), mount the cert
directory as a volume and use a renewal sidecar or cron job.

## Key Management

### Encryption Key Backup
The `encryption.key` file encrypts all service account credentials stored in
the database. **If this key is lost, stored credentials cannot be recovered.**

Best practices:
- Back up the key in your organization's secrets management system
- Use the same key across container rebuilds to preserve stored credentials
- Rotate the key by decrypting all credentials, generating a new key, and
  re-encrypting (future feature)

### Environment Variables
| Variable | Purpose | Example |
|----------|---------|---------|
| `PRAXOVA_KEY_FILE` | Path to encryption key file (recommended) | `/app/certs/encryption.key` |
| `PRAXOVA_ENCRYPTION_KEY` | Inline base64-encoded key (alternative) | `dGhpcyBpcyBhIHRlc3Qga2V5...` |

Only one is required. `PRAXOVA_KEY_FILE` is preferred for production as it
keeps the key out of environment variable listings.
