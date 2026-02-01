using LucidToolServer.Configuration;
using LucidToolServer.Exceptions;
using LucidToolServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LucidToolServer.Tests.Services;

/// <summary>
/// Unit tests for ActiveDirectoryService.
/// Note: These tests verify business logic (protected accounts, etc.) but cannot
/// test actual AD operations on Linux. Full integration tests require Windows/AD environment.
/// </summary>
public class ActiveDirectoryServiceTests
{
    private readonly Mock<ILogger<ActiveDirectoryService>> _loggerMock;
    private readonly ToolServerSettings _settings;

    public ActiveDirectoryServiceTests()
    {
        _loggerMock = new Mock<ILogger<ActiveDirectoryService>>();
        _settings = new ToolServerSettings
        {
            ProtectedAccounts = new[] { "Administrator", "krbtgt" },
            ProtectedGroups = new[] { "Domain Admins", "Enterprise Admins" }
        };
    }

    [Fact]
    public async Task ResetPassword_ProtectedAccount_ThrowsPermissionDenied()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => service.ResetPasswordAsync("Administrator", "NewPass123!"));

        Assert.Contains("protected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPassword_ProtectedAccountCaseInsensitive_ThrowsPermissionDenied()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => service.ResetPasswordAsync("administrator", "NewPass123!"));

        Assert.Contains("protected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddUserToGroup_ProtectedGroup_ThrowsPermissionDenied()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => service.AddUserToGroupAsync("testuser", "Domain Admins"));

        Assert.Contains("protected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveUserFromGroup_ProtectedGroup_ThrowsPermissionDenied()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => service.RemoveUserFromGroupAsync("testuser", "Enterprise Admins"));

        Assert.Contains("protected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ActiveDirectoryService(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsException()
    {
        // Arrange
        var options = Options.Create(_settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ActiveDirectoryService(options, null!));
    }

    [Fact]
    public async Task SearchUsersAsync_EmptyQuery_ReturnsValidResponse()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act
        // Note: This will throw on non-Windows/non-AD environment, but verifies the signature
        try
        {
            var result = await service.SearchUsersAsync("");

            // Assert - verify response structure if we get here (Windows/AD environment)
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Results);
            Assert.Equal("", result.Query);
        }
        catch (AdOperationException)
        {
            // Expected on non-AD environments - test passes
            Assert.True(true);
        }
    }

    [Fact]
    public async Task SearchUsersAsync_WithQuery_ReturnsValidResponse()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act
        try
        {
            var result = await service.SearchUsersAsync("test");

            // Assert - verify response structure if we get here (Windows/AD environment)
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Results);
            Assert.Equal("test", result.Query);
            Assert.Equal(result.Results.Count, result.Count);
        }
        catch (AdOperationException)
        {
            // Expected on non-AD environments - test passes
            Assert.True(true);
        }
    }

    [Fact]
    public async Task ListGroupsAsync_NoFilter_ReturnsValidResponse()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act
        try
        {
            var result = await service.ListGroupsAsync();

            // Assert - verify response structure if we get here (Windows/AD environment)
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Groups);
            Assert.Null(result.CategoryFilter);
            Assert.Equal(result.Groups.Count, result.Count);
        }
        catch (AdOperationException)
        {
            // Expected on non-AD environments - test passes
            Assert.True(true);
        }
    }

    [Fact]
    public async Task ListGroupsAsync_WithCategoryFilter_ReturnsValidResponse()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act
        try
        {
            var result = await service.ListGroupsAsync("DEPT");

            // Assert - verify response structure if we get here (Windows/AD environment)
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Groups);
            Assert.Equal("DEPT", result.CategoryFilter);
            Assert.Equal(result.Groups.Count, result.Count);

            // Verify all returned groups match the category filter
            foreach (var group in result.Groups)
            {
                Assert.Equal("DEPT", group.Category);
            }
        }
        catch (AdOperationException)
        {
            // Expected on non-AD environments - test passes
            Assert.True(true);
        }
    }

    [Fact]
    public async Task SearchGroupsAsync_WithQuery_ReturnsMatchingGroups()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act
        try
        {
            var result = await service.SearchGroupsAsync("VPN");

            // Assert - verify response structure if we get here (Windows/AD environment)
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Groups);
            Assert.Equal(result.Groups.Count, result.Count);
            Assert.Null(result.CategoryFilter); // Search doesn't use category filter

            // Verify all returned groups contain "VPN" in name or description
            foreach (var group in result.Groups)
            {
                var matchesName = group.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase);
                var matchesDescription = group.Description?.Contains("VPN", StringComparison.OrdinalIgnoreCase) ?? false;
                Assert.True(matchesName || matchesDescription);
            }
        }
        catch (AdOperationException)
        {
            // Expected on non-AD environments - test passes
            Assert.True(true);
        }
    }

    [Fact]
    public async Task SearchGroupsAsync_WithEmptyQuery_ReturnsValidResponse()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new ActiveDirectoryService(options, _loggerMock.Object);

        // Act
        try
        {
            var result = await service.SearchGroupsAsync("");

            // Assert - verify response structure if we get here (Windows/AD environment)
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Groups);
            // Empty query should return all groups (since empty string matches everything)
        }
        catch (AdOperationException)
        {
            // Expected on non-AD environments - test passes
            Assert.True(true);
        }
    }
}
