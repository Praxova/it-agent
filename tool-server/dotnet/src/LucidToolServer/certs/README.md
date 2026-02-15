# Tool Server TLS Certificates

## Development Setup (mkcert)

1. Install mkcert: https://github.com/FiloSottile/mkcert

2. Generate certificates (from this directory):
   ```
   cd tool-server/dotnet/src/LucidToolServer/certs/
   mkcert -cert-file cert.pem -key-file key.pem localhost 127.0.0.1 toolserver.praxova.local
   ```

3. The tool server will automatically use these certs in Development mode.

## Production Deployment

### Option A: CA-Signed Certificate
Place your certificate files here:
- `cert.pem` — TLS certificate (or PFX file)
- `key.pem` — Private key

Configure paths in `appsettings.json` under `Kestrel.Endpoints.Https.Certificate`.

### Option B: Internal/Enterprise CA
1. Place your CA root certificate in `ca-trust/` as a `.crt` file
2. Place your server certificate as `cert.pem` and `key.pem`
3. Rebuild the container — the CA will be trusted automatically

### Option C: PFX Certificate
If using a PFX/PKCS12 file instead of PEM:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:8443",
        "Certificate": {
          "Path": "certs/server.pfx",
          "Password": ""
        }
      }
    }
  }
}
```
Note: For production, use environment variable `Kestrel__Certificates__Default__Password` instead of storing the password in config.

## Environment Variable Overrides

| Variable | Purpose | Example |
|----------|---------|---------|
| `ASPNETCORE_Kestrel__Certificates__Default__Path` | Cert file path | `/app/certs/cert.pem` |
| `ASPNETCORE_Kestrel__Certificates__Default__KeyPath` | Key file path | `/app/certs/key.pem` |
| `ASPNETCORE_Kestrel__Certificates__Default__Password` | PFX password | (for PFX files only) |

## Files (git-ignored)
- `*.pem` — certificates and keys
- `*.pfx` — PKCS12 certificate bundles
- `*.key` — private keys
