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

### CA Trust (docker/certs/ca-trust/)
- Place CA root certificates here (`.crt` format) to trust them inside the container
- Used for internal/private CAs and development self-signed CAs
- Files here are git-ignored except `.gitkeep`
- The container runs `update-ca-certificates` at build time to trust these

## Deployment Scenarios

### Development (mkcert)
Run `./scripts/setup-dev-certs.sh` — it handles everything automatically.

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

3. Rebuild the container:
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

3. Rebuild and start:
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
