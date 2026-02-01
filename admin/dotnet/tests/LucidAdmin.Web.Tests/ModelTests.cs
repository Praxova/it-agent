using LucidAdmin.Core.Enums;
using LucidAdmin.Web.Models;
using Xunit;

namespace LucidAdmin.Web.Tests;

public class ModelTests
{
    [Fact]
    public void CreateServiceAccountRequest_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var request = new CreateServiceAccountRequest(
            Name: "test-account",
            DisplayName: "Test Account",
            Description: "Test account",
            Provider: "windows-ad",
            AccountType: "gmsa",
            Configuration: "{\"domain\":\"example.com\",\"samAccountName\":\"test$\"}",
            CredentialStorage: CredentialStorageType.None,
            CredentialReference: null
        );

        // Assert
        Assert.Equal("test-account", request.Name);
        Assert.Equal("Test Account", request.DisplayName);
        Assert.Equal("Test account", request.Description);
        Assert.Equal("windows-ad", request.Provider);
        Assert.Equal("gmsa", request.AccountType);
        Assert.NotNull(request.Configuration);
        Assert.Equal(CredentialStorageType.None, request.CredentialStorage);
        Assert.Null(request.CredentialReference);
    }

    [Fact]
    public void LoginRequest_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var request = new LoginRequest(
            Username: "admin",
            Password: "password123"
        );

        // Assert
        Assert.Equal("admin", request.Username);
        Assert.Equal("password123", request.Password);
    }
}
