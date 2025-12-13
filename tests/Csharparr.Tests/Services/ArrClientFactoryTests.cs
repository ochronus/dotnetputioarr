using Csharparr.Configuration;
using Csharparr.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Polly.CircuitBreaker;
using System.Net;
using System.Net.Sockets;
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

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithBrokenCircuit_ShouldLogDebug()
    {
        // Arrange
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://localhost:8989", "api-key")
        };
        _mockHttpClientFactory.Setup(f => f.CreateClient("ArrClient"))
            .Throws(new BrokenCircuitException("The circuit is now open and is not allowing calls."));
        var factory = new ArrClientFactory(_mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var (imported, serviceName) = await factory.CheckImportedMultiServiceAsync("/path/to/file.mkv", services);

        // Assert
        imported.Should().BeFalse();
        serviceName.Should().BeNull();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Circuit breaker open")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithSocketException_ShouldLogWarningWithoutStackTrace()
    {
        // Arrange
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://localhost:8989", "api-key")
        };
        var socketException = new SocketException((int)SocketError.ConnectionRefused);
        var httpException = new HttpRequestException("Connection refused", socketException);
        _mockHttpClientFactory.Setup(f => f.CreateClient("ArrClient"))
            .Throws(httpException);
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
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cannot connect")),
                null, // Should not include exception when logging concisely
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithUnexpectedException_ShouldLogWarningWithStackTrace()
    {
        // Arrange
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://localhost:8989", "api-key")
        };
        var unexpectedException = new InvalidOperationException("Unexpected error");
        _mockHttpClientFactory.Setup(f => f.CreateClient("ArrClient"))
            .Throws(unexpectedException);
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
                It.IsAny<Exception>(), // Should include exception for unexpected errors
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithMultipleServices_OneDownOneFails_ShouldContinueChecking()
    {
        // Arrange
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://localhost:8989", "api-key-1"),
            new ArrServiceInfo("Radarr", "http://localhost:7878", "api-key-2")
        };

        var callCount = 0;
        _mockHttpClientFactory.Setup(f => f.CreateClient("ArrClient"))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First service throws BrokenCircuitException
                    throw new BrokenCircuitException("Circuit open");
                }
                // Second service succeeds (but we still need to throw since we can't return a real HttpClient easily)
                throw new HttpRequestException("Test", new SocketException((int)SocketError.ConnectionRefused));
            });

        var factory = new ArrClientFactory(_mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var (imported, serviceName) = await factory.CheckImportedMultiServiceAsync("/path/to/file.mkv", services);

        // Assert
        imported.Should().BeFalse();
        serviceName.Should().BeNull();
        callCount.Should().Be(2, "Should attempt both services despite first failure");

        // Should log both: one Debug for circuit breaker, one Warning for connection error
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Circuit breaker open")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cannot connect")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
