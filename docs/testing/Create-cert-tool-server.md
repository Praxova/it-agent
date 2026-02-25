Step 1: Generate the cert on the Tool Server (PowerShell as Admin)

# Create a self-signed cert — replace with your tool server's actual hostname/IP
$cert = New-SelfSignedCertificate `
    -DnsName "toolserver.montanifarms.com", "localhost" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -NotAfter (Get-Date).AddYears(2) `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -FriendlyName "Praxova Tool Server"

# Export the PFX (private key) — for Kestrel to use
$password = ConvertTo-SecureString -String "YourSecurePassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\PraxovaToolServer\certs\toolserver.pfx" -Password $password

# Export the CRT (public key only) — for containers to trust
Export-Certificate -Cert $cert -FilePath "C:\PraxovaToolServer\certs\toolserver.cer"

# Convert DER to PEM format (containers need PEM)
certutil -encode "C:\PraxovaToolServer\certs\toolserver.cer" "C:\PraxovaToolServer\certs\toolserver.crt"
Step 2: Configure the Tool Server for HTTPS
Edit C:\PraxovaToolServer\appsettings.json to add the HTTPS endpoint:


{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:8080"
      },
      "Https": {
        "Url": "https://0.0.0.0:8443",
        "Certificate": {
          "Path": "C:\\PraxovaToolServer\\certs\\toolserver.pfx",
          "Password": "YourSecurePassword"
        }
      }
    }
  }
}
Then restart the service:


Restart-Service PraxovaToolServer
Step 3: Copy the .crt to your Docker host
Copy toolserver.crt from the tool server to your Docker host (where the containers run). Place it in both CA trust directories:


# From your Docker host / Linux dev machine
# Copy the .crt file into both container trust directories:
cp toolserver.crt /home/alton/Documents/lucid-it-agent/docker/certs/ca-trust/toolserver.crt
cp toolserver.crt /home/alton/Documents/lucid-it-agent/agent/certs/ca-trust/toolserver.crt
Step 4: Rebuild the containers

docker compose build --no-cache
docker compose up -d
The admin portal Dockerfile runs update-ca-certificates which picks up anything in docker/certs/ca-trust/, and the agent Dockerfile (the change from last night) does the same from agent/certs/ca-trust/.

Quick note: If you'd rather keep HTTP for the demo and save HTTPS for production, everything works as-is right now over HTTP. The cert setup above is only needed if you want HTTPS between the containers and the tool server.
