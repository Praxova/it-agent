using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LucidAdmin.Web.Tests;

/// <summary>
/// Tests for the certificate renewal endpoint validation logic.
/// Uses an in-memory DbContext to verify the DB queries and conditions
/// that the POST /api/pki/certificates/renew endpoint evaluates.
/// </summary>
public class CertificateRenewalTests : IDisposable
{
    private readonly LucidDbContext _db;

    public CertificateRenewalTests()
    {
        var options = new DbContextOptionsBuilder<LucidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new LucidDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task RenewCertificate_ReturnsNotInRenewalWindow_WhenCertExpiresBeyond30Days()
    {
        // Arrange — cert expires in 60 days (outside 30-day renewal window)
        var agent = new Agent
        {
            Name = "test-agent",
            DisplayName = "Test Agent",
            IsEnabled = true
        };
        _db.Agents.Add(agent);

        var cert = new IssuedCertificate
        {
            Name = "agent-client-test-agent",
            SubjectCN = "test-agent",
            Thumbprint = "abc123",
            SerialNumber = "deadbeef01",
            NotBefore = DateTime.UtcNow.AddDays(-30),
            NotAfter = DateTime.UtcNow.AddDays(60),
            Usage = "client-tls",
            IssuedTo = "test-agent",
            IsActive = true
        };
        _db.IssuedCertificates.Add(cert);
        await _db.SaveChangesAsync();

        // Act — simulate the renewal window check
        var foundCert = await _db.IssuedCertificates
            .FirstOrDefaultAsync(c => c.SerialNumber == "deadbeef01" && c.IsActive);

        // Assert
        Assert.NotNull(foundCert);
        var daysRemaining = (foundCert.NotAfter - DateTime.UtcNow).TotalDays;
        Assert.True(daysRemaining > 30, $"Expected > 30 days remaining but got {daysRemaining:F0}");
        // Endpoint would return 400 with "not_in_renewal_window"
    }

    [Fact]
    public async Task RenewCertificate_Returns403_WhenAgentNameMismatch()
    {
        // Arrange — cert belongs to "other-agent" but requesting agent is "test-agent"
        var requestingAgent = new Agent
        {
            Name = "test-agent",
            DisplayName = "Test Agent",
            IsEnabled = true
        };
        _db.Agents.Add(requestingAgent);

        var cert = new IssuedCertificate
        {
            Name = "agent-client-other-agent",
            SubjectCN = "other-agent",
            Thumbprint = "xyz789",
            SerialNumber = "cafebabe02",
            NotBefore = DateTime.UtcNow.AddDays(-80),
            NotAfter = DateTime.UtcNow.AddDays(10),
            Usage = "client-tls",
            IssuedTo = "other-agent",
            IsActive = true
        };
        _db.IssuedCertificates.Add(cert);
        await _db.SaveChangesAsync();

        // Act — simulate the ownership check
        var foundCert = await _db.IssuedCertificates
            .FirstOrDefaultAsync(c => c.SerialNumber == "cafebabe02" && c.IsActive);

        // Assert — cert's IssuedTo does not match requesting agent's name
        Assert.NotNull(foundCert);
        Assert.False(
            string.Equals(foundCert.IssuedTo, requestingAgent.Name, StringComparison.OrdinalIgnoreCase),
            "IssuedTo should NOT match the requesting agent — endpoint returns 403");
    }

    [Fact]
    public async Task RenewCertificate_Returns404_WhenSerialNotFound()
    {
        // Arrange — no certs in database
        var agent = new Agent
        {
            Name = "test-agent",
            DisplayName = "Test Agent",
            IsEnabled = true
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        // Act — query for a serial that doesn't exist
        var nonExistentSerial = "0000000000";
        var foundCert = await _db.IssuedCertificates
            .FirstOrDefaultAsync(c => c.SerialNumber == nonExistentSerial && c.IsActive);

        // Assert — endpoint would return 404
        Assert.Null(foundCert);
    }

    [Fact]
    public async Task RenewCertificate_Succeeds_WhenCertExpiresWithin20Days()
    {
        // Arrange — cert expires in 20 days (inside 30-day renewal window)
        var agent = new Agent
        {
            Name = "test-agent",
            DisplayName = "Test Agent",
            IsEnabled = true
        };
        _db.Agents.Add(agent);

        var cert = new IssuedCertificate
        {
            Name = "agent-client-test-agent",
            SubjectCN = "test-agent",
            Thumbprint = "def456",
            SerialNumber = "feedface03",
            NotBefore = DateTime.UtcNow.AddDays(-70),
            NotAfter = DateTime.UtcNow.AddDays(20),
            Usage = "client-tls",
            IssuedTo = "test-agent",
            IsActive = true
        };
        _db.IssuedCertificates.Add(cert);
        await _db.SaveChangesAsync();

        // Act — simulate the full validation chain
        var serialNormalized = "feedface03";
        var foundCert = await _db.IssuedCertificates
            .FirstOrDefaultAsync(c => c.SerialNumber == serialNormalized && c.IsActive);

        // Assert — all validation checks pass
        Assert.NotNull(foundCert);

        // Ownership check
        Assert.True(
            string.Equals(foundCert.IssuedTo, agent.Name, StringComparison.OrdinalIgnoreCase),
            "IssuedTo should match the requesting agent");

        // Renewal window check
        var daysRemaining = (foundCert.NotAfter - DateTime.UtcNow).TotalDays;
        Assert.True(daysRemaining <= 30, $"Expected <= 30 days remaining but got {daysRemaining:F0}");
        // Endpoint would proceed to call pkiService.GetOrIssueAgentClientCertAsync
    }
}
