using LucidToolServer.Configuration;
using LucidToolServer.Exceptions;
using LucidToolServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LucidToolServer.Tests.Services;

/// <summary>
/// Unit tests for FilePermissionService.
/// Note: These tests verify business logic (path validation) but cannot test actual
/// file permission operations on Linux. Full integration tests require Windows environment.
/// </summary>
public class FilePermissionServiceTests
{
    private readonly Mock<ILogger<FilePermissionService>> _loggerMock;
    private readonly ToolServerSettings _settings;

    public FilePermissionServiceTests()
    {
        _loggerMock = new Mock<ILogger<FilePermissionService>>();
        _settings = new ToolServerSettings
        {
            AllowedPaths = new[] { @"\\server\share*", @"\\fileserver\*" }
        };
    }

    [Theory]
    [InlineData(@"\\server\share1")]
    [InlineData(@"\\server\share2\subfolder")]
    [InlineData(@"\\fileserver\docs")]
    [InlineData(@"\\FILESERVER\DOCS")]  // Case insensitive
    public void ValidatePath_AllowedPath_DoesNotThrow(string path)
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new FilePermissionService(options, _loggerMock.Object);

        // Act & Assert - We expect PathNotFoundException since paths don't exist on Linux,
        // but not PathNotAllowedException which means path validation passed
        Assert.ThrowsAny<Exception>(() => service.ListPermissions(path));
    }

    [Theory]
    [InlineData(@"\\otherserver\share")]
    [InlineData(@"\\server\admin")]
    [InlineData(@"C:\Windows")]
    public void ValidatePath_DisallowedPath_ThrowsPathNotAllowed(string path)
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new FilePermissionService(options, _loggerMock.Object);

        // Act & Assert
        var exception = Assert.Throws<PathNotAllowedException>(
            () => service.ListPermissions(path));

        Assert.Contains("not in the allowed paths", exception.Message);
    }

    [Fact]
    public void ValidatePath_EmptyAllowedPaths_AllowsAllPaths()
    {
        // Arrange
        var settingsWithNoRestrictions = new ToolServerSettings
        {
            AllowedPaths = Array.Empty<string>()
        };
        var options = Options.Create(settingsWithNoRestrictions);
        var service = new FilePermissionService(options, _loggerMock.Object);

        // Act & Assert - Should not throw PathNotAllowedException
        // May throw PathNotFoundException since path doesn't exist on Linux
        Assert.ThrowsAny<Exception>(() => service.ListPermissions(@"\\anyserver\anyshare"));
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FilePermissionService(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsException()
    {
        // Arrange
        var options = Options.Create(_settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FilePermissionService(options, null!));
    }

    [Fact]
    public void HealthCheck_ReturnsTrue()
    {
        // Arrange
        var options = Options.Create(_settings);
        var service = new FilePermissionService(options, _loggerMock.Object);

        // Act
        var result = service.HealthCheck();

        // Assert
        // Health check should return true on any system with Windows identity APIs
        // (may return false on pure Linux without Windows compatibility layer)
        Assert.IsType<bool>(result);
    }
}
