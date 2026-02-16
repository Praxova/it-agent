using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class TrustEndpoints
{
    public static void MapTrustEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/trusted-certs")
            .WithTags("Trust")
            .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        // Import a PEM certificate
        group.MapPost("/", async ([FromBody] ImportCertRequest request, ITrustedCertificateStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Pem))
                return Results.BadRequest(new { error = "PEM certificate content is required." });

            var result = await store.ImportCertificateAsync(request.Pem, request.FriendlyName ?? "Imported certificate");

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            return Results.Ok(result);
        }).WithName("ImportTrustedCert");

        // List all imported certificates
        group.MapGet("/", async (ITrustedCertificateStore store) =>
        {
            var certs = await store.ListTrustedCertsAsync();
            return Results.Ok(certs);
        }).WithName("ListTrustedCerts");

        // Remove a trusted certificate
        group.MapDelete("/{thumbprint}", async (string thumbprint, ITrustedCertificateStore store) =>
        {
            var removed = await store.RemoveCertificateAsync(thumbprint);
            if (!removed)
                return Results.NotFound(new { error = "Certificate not found." });

            return Results.Ok(new { message = "Certificate removed from trust store." });
        }).WithName("RemoveTrustedCert");

        // Probe LDAPS certificate
        var adGroup = app.MapGroup("/api/settings/active-directory")
            .WithTags("Settings")
            .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        adGroup.MapPost("/test-ldaps", async (
            [FromBody] LdapsProbeRequest request,
            ITlsCertificateProbeService probeService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Host))
                return Results.BadRequest(new { error = "Host is required." });

            var result = await probeService.ProbeCertificateAsync(request.Host, request.Port);
            return Results.Ok(result);
        }).WithName("TestLdaps");
    }
}

public record ImportCertRequest(string Pem, string? FriendlyName = null);
public record LdapsProbeRequest(string Host, int Port = 636);
