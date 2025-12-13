using Csharparr.Configuration;
using Csharparr.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace Csharparr.Tests.Services;

public class ArrClientFactoryTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<ArrClientFactory>> _mockLogger;

    public ArrClientFactoryTests()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<ArrClientFactory>>();
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithNoServices_ShouldReturnNotImported()
    {
        // Arrange
        var services = Enumerable.Empty<ArrServiceInfo>();
        var factory = new ArrClientFactory(_mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var (imported, serviceName) = await factory.CheckImportedMultiServiceAsync("/path/to/file.mkv", services);

        // Assert
        imported.Should().BeFalse();
        serviceName.Should().BeNull();
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithFailingService_ShouldLogWarning()
    {
        // Arrange
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://invalid-host-that-does-not-exist:8989", "api-key")
        };
        _mockHttpClientFactory.Setup(f => f.CreateClient("ArrClient")).Throws(new HttpRequestException());
        var factory = new ArrClientFactory(_mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var (imported, serviceName) = await factory.CheckImportedMultiServiceAsync("/path/to/file.mkv", services);

        // Assert
        imported.Should().BeFalse();
        serviceName.Should().BeNull();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to check import status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
