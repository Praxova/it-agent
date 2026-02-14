# Local Development Certificates

## Setup (one-time)

1. Install mkcert: https://github.com/FiloSottile/mkcert
   - macOS: `brew install mkcert`
   - Linux: `sudo apt install mkcert` or download from GitHub releases

2. Install the local CA:
   ```
   mkcert -install
   ```

3. Generate certificates (from this directory):
   ```
   cd docker/certs/
   mkcert -cert-file cert.pem -key-file key.pem localhost 127.0.0.1 admin.praxova.local
   ```

4. Add to /etc/hosts (optional, for clean demo URL):
   ```
   127.0.0.1 admin.praxova.local
   ```

## Files (git-ignored)
- `cert.pem` — TLS certificate
- `key.pem` — Private key
- Do NOT commit these files
