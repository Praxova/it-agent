using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using Xunit;

namespace LucidAdmin.Core.Tests;

public class EntityTests
{
    [Fact]
    public void ServiceAccount_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var account = new ServiceAccount
        {
            Name = "test-account",
            Provider = "windows-ad",
            AccountType = "gmsa"
        };

        // Assert
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("test-account", account.Name);
        Assert.Equal("windows-ad", account.Provider);
        Assert.Equal("gmsa", account.AccountType);
        Assert.Equal(CredentialStorageType.None, account.CredentialStorage);
        Assert.True(account.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ToolServer_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var server = new ToolServer
        {
            Name = "test-server",
            Endpoint = "http://localhost:5000",
            Domain = "example.com"
        };

        // Assert
        Assert.NotEqual(Guid.Empty, server.Id);
        Assert.Equal("test-server", server.Name);
        Assert.Equal("http://localhost:5000", server.Endpoint);
        Assert.Equal("example.com", server.Domain);
        Assert.True(server.CreatedAt <= DateTime.UtcNow);
    }
}
