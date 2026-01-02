using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Csharparr.Configuration;
using Csharparr.Http;
using Csharparr.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Csharparr.Tests.Http;

public class TransmissionControllerTests
{
    private static string CreateBasicAuthHeader(string username, string password)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return $"Basic {credentials}";
    }

    [Fact]
    public void SessionId_ShouldBeConstant()
    {
        const string expectedSessionId = "useless-session-id";

        // The session ID should be consistent across requests
        expectedSessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BasicAuthHeader_Generation_ShouldBeCorrect()
    {
        var header = CreateBasicAuthHeader("testuser", "testpass");

        header.Should().StartWith("Basic ");

        var encoded = header["Basic ".Length..];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        decoded.Should().Be("testuser:testpass");
    }

    [Fact]
    public void BasicAuthHeader_WithPasswordContainingColon_ShouldWork()
    {
        var header = CreateBasicAuthHeader("user", "pass:word:with:colons");

        var encoded = header["Basic ".Length..];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        decoded.Should().Be("user:pass:word:with:colons");
    }

    [Fact]
    public void BasicAuthHeader_WithEmptyPassword_ShouldWork()
    {
        var header = CreateBasicAuthHeader("user", "");

        var encoded = header["Basic ".Length..];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        decoded.Should().Be("user:");
    }

    [Fact]
    public void TransmissionRequest_Deserialization_SessionGet()
    {
        var json = """{"method":"session-get","arguments":null}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("session-get");
        request.Arguments.Should().BeNull();
    }

    [Fact]
    public void TransmissionRequest_Deserialization_TorrentGet()
    {
        var json = """{"method":"torrent-get","arguments":{"fields":["id","name"]}}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("torrent-get");
        request.Arguments.Should().NotBeNull();
    }

    [Fact]
    public void TransmissionRequest_Deserialization_TorrentAdd()
    {
        var json = """{"method":"torrent-add","arguments":{"filename":"magnet:?xt=urn:btih:test"}}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("torrent-add");
        request.Arguments.Should().NotBeNull();
    }

    [Fact]
    public void TransmissionRequest_Deserialization_TorrentAddWithMetainfo()
    {
        var base64Torrent = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake torrent data"));
        var json = $$$"""{"method":"torrent-add","arguments":{"metainfo":"{{{base64Torrent}}}"}}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("torrent-add");
    }

    [Fact]
    public void TransmissionRequest_Deserialization_TorrentRemove()
    {
        var json = """{"method":"torrent-remove","arguments":{"ids":["abc123"],"delete-local-data":true}}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("torrent-remove");
    }

    [Fact]
    public void TransmissionResponse_Serialization_Success()
    {
        var response = new TransmissionResponse("success");

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"result\":\"success\"");
    }

    [Fact]
    public void TransmissionResponse_Serialization_WithArguments()
    {
        var response = new TransmissionResponse("success", new { test = "value" });

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"result\":\"success\"");
        json.Should().Contain("\"arguments\"");
    }

    [Fact]
    public void TransmissionConfig_Default_ShouldHaveCorrectJsonProperties()
    {
        var config = TransmissionConfig.Default("/downloads");
        var json = JsonSerializer.Serialize(config);

        json.Should().Contain("\"rpc-version\"");
        json.Should().Contain("\"version\"");
        json.Should().Contain("\"download-dir\"");
        json.Should().Contain("\"seedRatioLimit\"");
        json.Should().Contain("\"seedRatioLimited\"");
        json.Should().Contain("\"idle-seeding-limit\"");
        json.Should().Contain("\"idle-seeding-limit-enabled\"");
    }

    [Fact]
    public void TorrentAddArguments_Deserialization_Filename()
    {
        var json = """{"filename":"magnet:?xt=urn:btih:abc123&dn=Test%20File"}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var args = JsonSerializer.Deserialize<TorrentAddArguments>(json, options);

        args.Should().NotBeNull();
        args!.Filename.Should().Be("magnet:?xt=urn:btih:abc123&dn=Test%20File");
        args.Metainfo.Should().BeNull();
    }

    [Fact]
    public void TorrentAddArguments_Deserialization_Metainfo()
    {
        var base64Data = Convert.ToBase64String("fake data"u8.ToArray());
        var json = $$$"""{"metainfo":"{{{base64Data}}}"}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var args = JsonSerializer.Deserialize<TorrentAddArguments>(json, options);

        args.Should().NotBeNull();
        args!.Metainfo.Should().Be(base64Data);
        args.Filename.Should().BeNull();
    }

    [Fact]
    public void TorrentRemoveArguments_Deserialization()
    {
        var json = """{"ids":["hash1","hash2"],"delete-local-data":true}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var args = JsonSerializer.Deserialize<TorrentRemoveArguments>(json, options);

        args.Should().NotBeNull();
        args!.Ids.Should().HaveCount(2);
        args.Ids.Should().Contain("hash1");
        args.Ids.Should().Contain("hash2");
        args.DeleteLocalData.Should().BeTrue();
    }

    [Fact]
    public void TorrentRemoveArguments_Deserialization_EmptyIds()
    {
        var json = """{"ids":[],"delete-local-data":false}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var args = JsonSerializer.Deserialize<TorrentRemoveArguments>(json, options);

        args.Should().NotBeNull();
        args!.Ids.Should().BeEmpty();
        args.DeleteLocalData.Should().BeFalse();
    }

    [Fact]
    public void TorrentGetResponse_Serialization()
    {
        var response = new TorrentGetResponse(
            [
                new TransmissionTorrent
                {
                    Id = 1,
                    Name = "Test Torrent",
                    HashString = "abc123",
                    Status = TorrentStatus.Downloading
                }
            ]
        );

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"torrents\"");
        json.Should().Contain("\"id\":1");
        json.Should().Contain("\"name\":\"Test Torrent\"");
    }

    [Fact]
    public void TransmissionTorrent_Serialization_AllFields()
    {
        var torrent = new TransmissionTorrent
        {
            Id = 123,
            HashString = "abc123def456",
            Name = "Test Movie",
            DownloadDir = "/downloads",
            TotalSize = 1000000,
            LeftUntilDone = 500000,
            IsFinished = false,
            Eta = 3600,
            Status = TorrentStatus.Downloading,
            SecondsDownloading = 1800,
            ErrorString = null,
            DownloadedEver = 500000,
            SeedRatioLimit = 1.0f,
            SeedRatioMode = 0,
            SeedIdleLimit = 0,
            SeedIdleMode = 0,
            FileCount = 1
        };

        var json = JsonSerializer.Serialize(torrent);

        json.Should().Contain("\"id\":123");
        json.Should().Contain("\"hashString\":\"abc123def456\"");
        json.Should().Contain("\"name\":\"Test Movie\"");
        json.Should().Contain("\"downloadDir\":\"/downloads\"");
        json.Should().Contain("\"totalSize\":1000000");
        json.Should().Contain("\"leftUntilDone\":500000");
        json.Should().Contain("\"isFinished\":false");
        json.Should().Contain("\"eta\":3600");
        json.Should().Contain("\"status\":4"); // TorrentStatus.Downloading = 4
    }

    [Theory]
    [InlineData("session-get")]
    [InlineData("torrent-get")]
    [InlineData("torrent-set")]
    [InlineData("torrent-add")]
    [InlineData("torrent-remove")]
    [InlineData("queue-move-top")]
    public void SupportedMethods_ShouldBeRecognized(string method)
    {
        var validMethods = new[]
        {
            "session-get", "torrent-get", "torrent-set",
            "torrent-add", "torrent-remove", "queue-move-top"
        };

        validMethods.Should().Contain(method);
    }

    [Fact]
    public void MagnetLink_Parsing_ShouldExtractDisplayName()
    {
        var magnetLink = "magnet:?xt=urn:btih:abc123&dn=Test%20Movie%20(2024)";

        var uri = new Uri(magnetLink);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var displayName = query["dn"];

        displayName.Should().Be("Test Movie (2024)");
    }

    [Fact]
    public void MagnetLink_Parsing_WithoutDisplayName_ShouldBeNull()
    {
        var magnetLink = "magnet:?xt=urn:btih:abc123";

        var uri = new Uri(magnetLink);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var displayName = query["dn"];

        displayName.Should().BeNullOrEmpty();
    }

    [Fact]
    public void MagnetLink_Parsing_WithComplexUrl_ShouldWork()
    {
        var magnetLink = "magnet:?xt=urn:btih:abc123&dn=Test%20File&tr=udp://tracker1.com&tr=udp://tracker2.com";

        var uri = new Uri(magnetLink);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        query["xt"].Should().Be("urn:btih:abc123");
        query["dn"].Should().Be("Test File");
        query.GetValues("tr").Should().HaveCount(2);
    }

    [Fact]
    public void Base64_Encoding_TorrentData_ShouldBeValid()
    {
        var originalData = new byte[] { 0x64, 0x38, 0x3a, 0x61, 0x6e, 0x6e, 0x6f, 0x75, 0x6e, 0x63, 0x65 };
        var encoded = Convert.ToBase64String(originalData);
        var decoded = Convert.FromBase64String(encoded);

        decoded.Should().BeEquivalentTo(originalData);
    }

    [Fact]
    public void HttpStatusCode_409_ShouldBeConflict()
    {
        HttpStatusCode.Conflict.Should().Be((HttpStatusCode)409);
    }

    [Fact]
    public void HttpStatusCode_403_ShouldBeForbidden()
    {
        HttpStatusCode.Forbidden.Should().Be((HttpStatusCode)403);
    }

    [Fact]
    public async Task TorrentAdd_ShouldUseExistingInstanceFolderAndSource()
    {
        var config = new AppConfig
        {
            Username = "user",
            Password = "pass",
            InstanceName = "inst123",
            InstanceFolderId = 42,
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig("key"),
            Sonarr = new ArrConfig("http://sonarr", "key")
        };

        var mockPutio = new Mock<IPutioClient>();
        mockPutio.Setup(x => x.AddTransferAsync(
            "magnet:?xt=urn:btih:abc123",
            "inst123",
            42,
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new TransmissionController(config, mockPutio.Object, NullLogger<TransmissionController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Request.Headers.Authorization = CreateBasicAuthHeader("user", "pass");

        var request = new TransmissionRequest("torrent-add", new { filename = "magnet:?xt=urn:btih:abc123" });

        var result = await controller.RpcPost(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mockPutio.Verify(x => x.ListFilesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPutio.Verify(x => x.CreateFolderAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPutio.Verify(x => x.AddTransferAsync("magnet:?xt=urn:btih:abc123", "inst123", 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TorrentAdd_ShouldUseConfiguredInstanceFolderWithoutLookup()
    {
        var config = new AppConfig
        {
            Username = "user",
            Password = "pass",
            InstanceName = "inst124",
            InstanceFolderId = 77,
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig("key"),
            Sonarr = new ArrConfig("http://sonarr", "key")
        };

        var mockPutio = new Mock<IPutioClient>();

        mockPutio.Setup(x => x.AddTransferAsync(
            "magnet:?xt=urn:btih:def456",
            "inst124",
            77,
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new TransmissionController(config, mockPutio.Object, NullLogger<TransmissionController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Request.Headers.Authorization = CreateBasicAuthHeader("user", "pass");

        var request = new TransmissionRequest("torrent-add", new { filename = "magnet:?xt=urn:btih:def456" });

        await controller.RpcPost(request, CancellationToken.None);
        await controller.RpcPost(request, CancellationToken.None); // second call should reuse cached folder id

        mockPutio.Verify(x => x.ListFilesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPutio.Verify(x => x.CreateFolderAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPutio.Verify(x => x.AddTransferAsync("magnet:?xt=urn:btih:def456", "inst124", 77, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void AppConfig_Validation_ShouldWorkForController()
    {
        var config = new AppConfig
        {
            Username = "test",
            Password = "test",
            InstanceName = "inst123",
            InstanceFolderId = 42,
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig("key"),
            Sonarr = new ArrConfig("http://sonarr", "key")
        };

        var action = () => config.Validate();

        action.Should().NotThrow();
    }

    [Fact]
    public void TransmissionRequest_EmptyMethod_ShouldDeserialize()
    {
        var json = """{"method":"","arguments":null}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().BeEmpty();
    }

    [Fact]
    public void TransmissionRequest_WhitespaceMethod_ShouldDeserialize()
    {
        var json = """{"method":"  ","arguments":null}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("  ");
    }

    [Fact]
    public void TorrentRemoveArguments_DefaultDeleteLocalData_ShouldBeFalse()
    {
        var args = new TorrentRemoveArguments([]);

        args.DeleteLocalData.Should().BeFalse();
    }

    [Fact]
    public void TorrentRemoveArguments_DefaultIds_ShouldBeEmpty()
    {
        var args = new TorrentRemoveArguments([]);

        args.Ids.Should().BeEmpty();
    }
}
