using Csharparr.Configuration;
using Csharparr.Http;
using Csharparr.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Csharparr.Tests.Http;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public FakeHttpMessageHandler(HttpResponseMessage response)
    {
        _response = response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_response);
    }
}

public class HealthControllerTests
{
    private readonly Mock<IPutioClient> _putioClientMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<HealthController>> _loggerMock;
    private readonly AppConfig _config;

    public HealthControllerTests()
    {
        _putioClientMock = new Mock<IPutioClient>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<HealthController>>();
        _config = new AppConfig
        {
            DownloadDirectory = Path.GetTempPath()
        };
    }

    private HealthController CreateController()
    {
        return new HealthController(_putioClientMock.Object, _config, _httpClientFactoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task HealthCheck_AllOk_ShouldReturnOk()
    {
        // Arrange
        var controller = CreateController();
        _putioClientMock.Setup(x => x.GetAccountInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AccountInfo());
        var httpClient = new HttpClient(new FakeHttpMessageHandler(new HttpResponseMessage { StatusCode = HttpStatusCode.OK }));
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Act
        var result = await controller.HealthCheck();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task HealthCheck_PutioFails_ShouldReturnServiceUnavailable()
    {
        // Arrange
        var controller = CreateController();
        _putioClientMock.Setup(x => x.GetAccountInfoAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new System.Exception());

        // Act
        var result = await controller.HealthCheck();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        (result as ObjectResult)!.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task HealthCheck_DownloadDirNotWritable_ShouldReturnServiceUnavailable()
    {
        // Arrange
        _config.DownloadDirectory = "/non_existent_dir";
        var controller = CreateController();
        _putioClientMock.Setup(x => x.GetAccountInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AccountInfo());

        // Act
        var result = await controller.HealthCheck();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        (result as ObjectResult)!.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task HealthCheck_ArrFails_ShouldReturnServiceUnavailable()
    {
        // Arrange
        _config.Sonarr = new ArrConfig("http://localhost:8989", "apikey");
        var controller = CreateController();
        _putioClientMock.Setup(x => x.GetAccountInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AccountInfo());
        var httpClient = new HttpClient(new FakeHttpMessageHandler(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError }));
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Act
        var result = await controller.HealthCheck();

        // Assert
        result.Should().BeOfType<ObjectResult>();
        (result as ObjectResult)!.StatusCode.Should().Be(503);
    }
}
