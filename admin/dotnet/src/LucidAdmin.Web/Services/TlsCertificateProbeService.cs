using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LucidAdmin.Web.Services;

public interface ITlsCertificateProbeService
{
    /// <summary>
    /// Connect to a host:port via TLS and retrieve the server's certificate chain.
    /// Does NOT validate the certificate — we want to see it even if untrusted.
    /// </summary>
    Task<TlsProbeResult> ProbeCertificateAsync(string host, int port, int timeoutMs = 5000);
}

public record TlsProbeResult(
    bool Connected,
    string? Error,
    TlsCertificateInfo? ServerCertificate,
    List<TlsCertificateInfo>? ChainCertificates);

public record TlsCertificateInfo(
    string Subject,
    string Issuer,
    DateTime NotBefore,
    DateTime NotAfter,
    string Thumbprint,
    string SerialNumber,
    bool IsExpired,
    bool IsSelfSigned,
    int DaysUntilExpiry,
    string Pem);

public class TlsCertificateProbeService : ITlsCertificateProbeService
{
    private readonly ILogger<TlsCertificateProbeService> _logger;

    public TlsCertificateProbeService(ILogger<TlsCertificateProbeService> logger)
    {
        _logger = logger;
    }

    public async Task<TlsProbeResult> ProbeCertificateAsync(string host, int port, int timeoutMs = 5000)
    {
        X509Certificate2? serverCert = null;
        var chainCerts = new List<TlsCertificateInfo>();
        bool certTrusted = false;

        try
        {
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                await tcpClient.ConnectAsync(host, port, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return new TlsProbeResult(false, $"Connection to {host}:{port} timed out after {timeoutMs}ms", null, null);
            }
            catch (SocketException ex)
            {
                return new TlsProbeResult(false, $"Cannot reach {host}:{port} — {ex.SocketErrorCode}", null, null);
            }

            using var sslStream = new SslStream(tcpClient.GetStream(), false,
                (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (certificate != null)
                        serverCert = new X509Certificate2(certificate);

                    // Capture chain certificates
                    if (chain != null)
                    {
                        foreach (var element in chain.ChainElements)
                        {
                            // Skip the leaf cert (already captured as serverCert)
                            if (serverCert != null && element.Certificate.Thumbprint == serverCert.Thumbprint)
                                continue;

                            chainCerts.Add(CertToInfo(element.Certificate));
                        }
                    }

                    certTrusted = sslPolicyErrors == SslPolicyErrors.None;
                    return true; // Accept any cert — we're probing, not validating
                });

            try
            {
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    // Don't fail on untrusted certs — the callback handles it
                };
                await sslStream.AuthenticateAsClientAsync(sslOptions, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // If we got the cert before timeout, still return it
                if (serverCert != null)
                {
                    return new TlsProbeResult(true, "TLS handshake timed out but certificate was captured",
                        CertToInfo(serverCert), chainCerts.Count > 0 ? chainCerts : null);
                }
                return new TlsProbeResult(false, $"TLS handshake with {host}:{port} timed out", null, null);
            }
            catch (Exception ex) when (ex is System.Security.Authentication.AuthenticationException or IOException)
            {
                // TLS handshake failed but we may still have the cert from the callback
                if (serverCert != null)
                {
                    return new TlsProbeResult(true, $"TLS handshake error: {ex.Message}",
                        CertToInfo(serverCert), chainCerts.Count > 0 ? chainCerts : null);
                }
                return new TlsProbeResult(false, $"TLS handshake failed: {ex.Message}", null, null);
            }

            if (serverCert == null)
            {
                return new TlsProbeResult(true, "Connected but no certificate was presented", null, null);
            }

            return new TlsProbeResult(true, null,
                CertToInfo(serverCert), chainCerts.Count > 0 ? chainCerts : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TLS probe failed for {Host}:{Port}", host, port);
            return new TlsProbeResult(false, $"Probe failed: {ex.Message}", null, null);
        }
    }

    private static TlsCertificateInfo CertToInfo(X509Certificate2 cert)
    {
        var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        var daysUntilExpiry = (int)(cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;

        return new TlsCertificateInfo(
            Subject: cert.Subject,
            Issuer: cert.Issuer,
            NotBefore: cert.NotBefore.ToUniversalTime(),
            NotAfter: cert.NotAfter.ToUniversalTime(),
            Thumbprint: thumbprint,
            SerialNumber: cert.SerialNumber.ToLowerInvariant(),
            IsExpired: cert.NotAfter.ToUniversalTime() < DateTime.UtcNow,
            IsSelfSigned: cert.Subject == cert.Issuer,
            DaysUntilExpiry: Math.Max(daysUntilExpiry, 0),
            Pem: cert.ExportCertificatePem());
    }
}
