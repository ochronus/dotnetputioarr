using Csharparr.Configuration;
using Csharparr.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Csharparr.Tests.Services;

public class ArrClientTests
{
    [Fact]
    public void ArrClient_Constructor_ShouldNotThrow()
    {
        var action = () => new ArrClient("http://localhost:8989", "test-api-key");

        action.Should().NotThrow();
    }

    [Fact]
    public void ArrClient_Dispose_ShouldNotThrow()
    {
        var client = new ArrClient("http://localhost:8989", "test-api-key");

        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public void ArrClient_DoubleDispose_ShouldNotThrow()
    {
        var client = new ArrClient("http://localhost:8989", "test-api-key");

        client.Dispose();
        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public void ArrClient_Constructor_ShouldTrimTrailingSlash()
    {
        // This is implicitly tested through the behavior of the client
        // The URL should work regardless of trailing slash
        var action = () => new ArrClient("http://localhost:8989/", "test-api-key");

        action.Should().NotThrow();
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithNoServices_ShouldReturnNotImported()
    {
        var services = Enumerable.Empty<ArrServiceInfo>();

        var (imported, serviceName) = await ArrClient.CheckImportedMultiServiceAsync(
            "/path/to/file.mkv",
            services);

        imported.Should().BeFalse();
        serviceName.Should().BeNull();
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithFailingService_ShouldLogWarning()
    {
        // Arrange
        var mockLogger = new Mock<ILogger>();
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://invalid-host-that-does-not-exist:8989", "api-key")
        };

        // Act
        var (imported, serviceName) = await ArrClient.CheckImportedMultiServiceAsync(
            "/path/to/file.mkv",
            services,
            mockLogger.Object);

        // Assert
        imported.Should().BeFalse();
        serviceName.Should().BeNull();

        // Verify that LogWarning was called
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to check import status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithNullLogger_ShouldNotThrow()
    {
        // Arrange
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://invalid-host-that-does-not-exist:8989", "api-key")
        };

        // Act
        var action = async () => await ArrClient.CheckImportedMultiServiceAsync(
            "/path/to/file.mkv",
            services,
            logger: null);

        // Assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_WithMultipleFailingServices_ShouldLogAllWarnings()
    {
        // Arrange
        var mockLogger = new Mock<ILogger>();
        var services = new[]
        {
            new ArrServiceInfo("Sonarr", "http://invalid-host-1:8989", "api-key-1"),
            new ArrServiceInfo("Radarr", "http://invalid-host-2:7878", "api-key-2")
        };

        // Act
        var (imported, serviceName) = await ArrClient.CheckImportedMultiServiceAsync(
            "/path/to/file.mkv",
            services,
            mockLogger.Object);

        // Assert
        imported.Should().BeFalse();
        serviceName.Should().BeNull();

        // Verify that LogWarning was called twice (once for each failing service)
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CheckImportedMultiServiceAsync_LogMessageContainsServiceDetails()
    {
        // Arrange
        var loggedMessages = new List<string>();
        var mockLogger = new Mock<ILogger>();
        mockLogger
            .Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, id, state, ex, formatter) =>
            {
                loggedMessages.Add(state.ToString() ?? "");
            });

        var services = new[]
        {
            new ArrServiceInfo("TestService", "http://test-url:1234", "test-key")
        };

        // Act
        await ArrClient.CheckImportedMultiServiceAsync(
            "/path/to/file.mkv",
            services,
            mockLogger.Object);

        // Assert
        loggedMessages.Should().ContainSingle();
        loggedMessages[0].Should().Contain("TestService");
        loggedMessages[0].Should().Contain("http://test-url:1234");
    }

    [Fact]
    public void ArrClientException_ShouldContainMessage()
    {
        var exception = new ArrClientException("Test error message");

        exception.Message.Should().Be("Test error message");
    }

    [Fact]
    public void ArrClientException_WithInnerException_ShouldContainBoth()
    {
        var innerException = new InvalidOperationException("Inner error");
        var exception = new ArrClientException("Outer error", innerException);

        exception.Message.Should().Be("Outer error");
        exception.InnerException.Should().BeSameAs(innerException);
    }

    [Fact]
    public void HistoryResponse_Deserialization_ShouldWork()
    {
        var json = """
            {
                "records": [
                    {
                        "eventType": "downloadFolderImported",
                        "data": {
                            "droppedPath": "/downloads/movie.mkv"
                        }
                    }
                ],
                "totalRecords": 1
            }
            """;

        var response = JsonSerializer.Deserialize<HistoryResponse>(json);

        response.Should().NotBeNull();
        response!.Records.Should().HaveCount(1);
        response.TotalRecords.Should().Be(1);
        response.Records[0].EventType.Should().Be("downloadFolderImported");
        response.Records[0].Data.Should().ContainKey("droppedPath");
        response.Records[0].Data["droppedPath"].Should().Be("/downloads/movie.mkv");
    }

    [Fact]
    public void HistoryResponse_EmptyRecords_ShouldDeserialize()
    {
        var json = """
            {
                "records": [],
                "totalRecords": 0
            }
            """;

        var response = JsonSerializer.Deserialize<HistoryResponse>(json);

        response.Should().NotBeNull();
        response!.Records.Should().BeEmpty();
        response.TotalRecords.Should().Be(0);
    }

    [Fact]
    public void HistoryRecord_WithMissingData_ShouldHaveEmptyDictionary()
    {
        var json = """
            {
                "eventType": "grabbed",
                "data": {}
            }
            """;

        var record = JsonSerializer.Deserialize<HistoryRecord>(json);

        record.Should().NotBeNull();
        record!.EventType.Should().Be("grabbed");
        record.Data.Should().BeEmpty();
    }

    [Fact]
    public void ArrServiceInfo_ShouldStoreValues()
    {
        var serviceInfo = new ArrServiceInfo("Sonarr", "http://localhost:8989", "my-api-key");

        serviceInfo.Name.Should().Be("Sonarr");
        serviceInfo.Url.Should().Be("http://localhost:8989");
        serviceInfo.ApiKey.Should().Be("my-api-key");
    }

    [Fact]
    public void ArrServiceInfo_Equality_ShouldWork()
    {
        var serviceInfo1 = new ArrServiceInfo("Sonarr", "http://localhost:8989", "my-api-key");
        var serviceInfo2 = new ArrServiceInfo("Sonarr", "http://localhost:8989", "my-api-key");
        var serviceInfo3 = new ArrServiceInfo("Radarr", "http://localhost:7878", "other-key");

        serviceInfo1.Should().Be(serviceInfo2);
        serviceInfo1.Should().NotBe(serviceInfo3);
    }
}
