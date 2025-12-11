using Csharparr.Services;
using FluentAssertions;
using System.Text.Json;

namespace Csharparr.Tests.Services;

public class TransmissionTypesTests
{
    [Fact]
    public void TransmissionConfig_Default_ShouldHaveCorrectValues()
    {
        var config = TransmissionConfig.Default("/downloads");

        config.RpcVersion.Should().Be("18");
        config.Version.Should().Be("14.0.0");
        config.DownloadDir.Should().Be("/downloads");
        config.SeedRatioLimit.Should().Be(1.0f);
        config.SeedRatioLimited.Should().BeTrue();
        config.IdleSeedingLimit.Should().Be(100);
        config.IdleSeedingLimitEnabled.Should().BeFalse();
    }

    [Fact]
    public void TransmissionConfig_Default_WithDifferentDir_ShouldSetDir()
    {
        var config = TransmissionConfig.Default("/custom/path");

        config.DownloadDir.Should().Be("/custom/path");
    }

    [Theory]
    [InlineData("STOPPED", TorrentStatus.Stopped)]
    [InlineData("COMPLETED", TorrentStatus.Stopped)]
    [InlineData("ERROR", TorrentStatus.Stopped)]
    [InlineData("CHECKWAIT", TorrentStatus.CheckWait)]
    [InlineData("PREPARING_DOWNLOAD", TorrentStatus.CheckWait)]
    [InlineData("CHECK", TorrentStatus.Check)]
    [InlineData("COMPLETING", TorrentStatus.Check)]
    [InlineData("QUEUED", TorrentStatus.Queued)]
    [InlineData("IN_QUEUE", TorrentStatus.Queued)]
    [InlineData("DOWNLOADING", TorrentStatus.Downloading)]
    [InlineData("SEEDINGWAIT", TorrentStatus.SeedingWait)]
    [InlineData("SEEDING", TorrentStatus.Seeding)]
    [InlineData("UNKNOWN", TorrentStatus.CheckWait)]
    public void StatusFromString_ShouldMapCorrectly(string input, TorrentStatus expected)
    {
        var result = TransmissionTorrent.StatusFromString(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("downloading")]
    [InlineData("DOWNLOADING")]
    [InlineData("Downloading")]
    public void StatusFromString_ShouldBeCaseInsensitive(string input)
    {
        var result = TransmissionTorrent.StatusFromString(input);

        result.Should().Be(TorrentStatus.Downloading);
    }

    [Fact]
    public void FromPutioTransfer_WithCompletedTransfer_ShouldMapCorrectly()
    {
        var transfer = new PutioTransfer(
            Id: 123,
            Hash: "abc123def456",
            Name: "Test Download",
            Size: 1000000,
            Downloaded: 1000000,
            FinishedAt: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            EstimatedTime: 0,
            Status: "COMPLETED",
            StartedAt: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            ErrorMessage: null,
            FileId: 456,
            UserfileExists: true
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.Id.Should().Be(123);
        torrent.HashString.Should().Be("abc123def456");
        torrent.Name.Should().Be("Test Download");
        torrent.TotalSize.Should().Be(1000000);
        torrent.LeftUntilDone.Should().Be(0);
        torrent.IsFinished.Should().BeTrue();
        torrent.Eta.Should().Be(0);
        torrent.DownloadedEver.Should().Be(1000000);
        torrent.ErrorString.Should().BeNull();
        torrent.FileCount.Should().Be(1);
        torrent.DownloadDir.Should().Be("/downloads");
        torrent.Status.Should().Be(TorrentStatus.Stopped);
    }

    [Fact]
    public void FromPutioTransfer_WithDownloadingTransfer_ShouldMapCorrectly()
    {
        var transfer = new PutioTransfer(
            Id: 789,
            Hash: "xyz789",
            Name: "Downloading Item",
            Size: 5000000,
            Downloaded: 2500000,
            FinishedAt: null,
            EstimatedTime: 300,
            Status: "DOWNLOADING",
            StartedAt: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            ErrorMessage: null,
            FileId: 999,
            UserfileExists: false
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.Id.Should().Be(789);
        torrent.TotalSize.Should().Be(5000000);
        torrent.LeftUntilDone.Should().Be(2500000);
        torrent.IsFinished.Should().BeFalse();
        torrent.Eta.Should().Be(300);
        torrent.Status.Should().Be(TorrentStatus.Downloading);
    }

    [Fact]
    public void FromPutioTransfer_WithErrorTransfer_ShouldMapErrorMessage()
    {
        var transfer = new PutioTransfer(
            Id: 111,
            Hash: "error123",
            Name: "Failed Download",
            Size: 1000,
            Downloaded: 500,
            FinishedAt: null,
            EstimatedTime: null,
            Status: "ERROR",
            StartedAt: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            ErrorMessage: "Network error",
            FileId: null,
            UserfileExists: false
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.ErrorString.Should().Be("Network error");
        torrent.Status.Should().Be(TorrentStatus.Stopped);
    }

    [Fact]
    public void FromPutioTransfer_WithNullName_ShouldDefaultToUnknown()
    {
        var transfer = new PutioTransfer(
            Id: 222,
            Hash: null,
            Name: null,
            Size: 1000,
            Downloaded: 0,
            FinishedAt: null,
            EstimatedTime: null,
            Status: "QUEUED",
            StartedAt: null,
            ErrorMessage: null,
            FileId: null,
            UserfileExists: false
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.Name.Should().Be("Unknown");
        torrent.HashString.Should().BeNull();
    }

    [Fact]
    public void FromPutioTransfer_WithNullValues_ShouldDefaultToZero()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            Hash: null,
            Name: null,
            Size: null,
            Downloaded: null,
            FinishedAt: null,
            EstimatedTime: null,
            Status: "QUEUED",
            StartedAt: null,
            ErrorMessage: null,
            FileId: null,
            UserfileExists: false
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.TotalSize.Should().Be(0);
        torrent.DownloadedEver.Should().Be(0);
        torrent.LeftUntilDone.Should().Be(0);
        torrent.Eta.Should().Be(0);
    }

    [Fact]
    public void FromPutioTransfer_WithNegativeLeftUntilDone_ShouldClampToZero()
    {
        var transfer = new PutioTransfer(
            Id: 333,
            Hash: "test",
            Name: "Test",
            Size: 1000,
            Downloaded: 1500, // Downloaded more than size
            FinishedAt: null,
            EstimatedTime: null,
            Status: "DOWNLOADING",
            StartedAt: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            ErrorMessage: null,
            FileId: 1,
            UserfileExists: false
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.LeftUntilDone.Should().Be(0);
    }

    [Fact]
    public void TransmissionResponse_Serialization_ShouldBeCorrect()
    {
        var response = new TransmissionResponse("success");

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"result\":\"success\"");
    }

    [Fact]
    public void TransmissionRequest_Deserialization_ShouldBeCorrect()
    {
        var json = """{"method":"torrent-get","arguments":null}""";

        var request = JsonSerializer.Deserialize<TransmissionRequest>(json);

        request.Should().NotBeNull();
        request!.Method.Should().Be("torrent-get");
        request.Arguments.Should().BeNull();
    }

    [Fact]
    public void TransmissionConfig_Serialization_ShouldUseCorrectPropertyNames()
    {
        var config = new TransmissionConfig(
            RpcVersion: "18",
            Version: "14.0.0",
            DownloadDir: "/downloads",
            SeedRatioLimit: 2.0f,
            SeedRatioLimited: true,
            IdleSeedingLimit: 200,
            IdleSeedingLimitEnabled: true
        );

        var json = JsonSerializer.Serialize(config);

        json.Should().Contain("\"rpc-version\":\"18\"");
        json.Should().Contain("\"download-dir\":\"/downloads\"");
        json.Should().Contain("\"seedRatioLimit\":2");
    }

    [Fact]
    public void TorrentAddArguments_ShouldHaveNullableProperties()
    {
        var args = new TorrentAddArguments();

        args.Metainfo.Should().BeNull();
        args.Filename.Should().BeNull();
    }

    [Fact]
    public void TorrentRemoveArguments_ShouldHaveDefaultValues()
    {
        var args = new TorrentRemoveArguments([]);

        args.Ids.Should().BeEmpty();
        args.DeleteLocalData.Should().BeFalse();
    }

    [Fact]
    public void TorrentGetResponse_ShouldHaveEmptyTorrents()
    {
        var response = new TorrentGetResponse([]);

        response.Torrents.Should().BeEmpty();
    }

    [Fact]
    public void TorrentStatus_Values_ShouldBeCorrect()
    {
        ((int)TorrentStatus.Stopped).Should().Be(0);
        ((int)TorrentStatus.CheckWait).Should().Be(1);
        ((int)TorrentStatus.Check).Should().Be(2);
        ((int)TorrentStatus.Queued).Should().Be(3);
        ((int)TorrentStatus.Downloading).Should().Be(4);
        ((int)TorrentStatus.SeedingWait).Should().Be(5);
        ((int)TorrentStatus.Seeding).Should().Be(6);
    }

    [Fact]
    public void TransmissionTorrent_DefaultFileCount_ShouldBeOne()
    {
        var torrent = new TransmissionTorrent();

        torrent.FileCount.Should().Be(1);
    }

    [Fact]
    public void FromPutioTransfer_WithInvalidStartedAt_ShouldUseCurrentTime()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            Status: "DOWNLOADING",
            StartedAt: "invalid-date"
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        // SecondsDownloading should be close to 0 since we defaulted to now
        torrent.SecondsDownloading.Should().BeGreaterThanOrEqualTo(0);
        torrent.SecondsDownloading.Should().BeLessThan(5);
    }

    [Fact]
    public void TransmissionTorrent_SeedRatioValues_ShouldDefaultToZero()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            Status: "COMPLETED"
        );

        var torrent = TransmissionTorrent.FromPutioTransfer(transfer, "/downloads");

        torrent.SeedRatioLimit.Should().Be(0.0f);
        torrent.SeedRatioMode.Should().Be(0);
        torrent.SeedIdleLimit.Should().Be(0);
        torrent.SeedIdleMode.Should().Be(0);
    }
}
