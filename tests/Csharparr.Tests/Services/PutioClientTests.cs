using Csharparr.Services;
using FluentAssertions;
using System.Text.Json;

namespace Csharparr.Tests.Services;

public class PutioClientTests
{
    [Fact]
    public void PutioTransfer_IsDownloadable_WithFileId_ShouldReturnTrue()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            FileId: 123,
            Status: "COMPLETED"
        );

        transfer.IsDownloadable.Should().BeTrue();
    }

    [Fact]
    public void PutioTransfer_IsDownloadable_WithoutFileId_ShouldReturnFalse()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            FileId: null,
            Status: "DOWNLOADING"
        );

        transfer.IsDownloadable.Should().BeFalse();
    }

    [Fact]
    public void PutioTransfer_Deserialization_AllFields()
    {
        var json = """
            {
                "id": 123,
                "hash": "abc123def456",
                "name": "Test Transfer",
                "size": 1000000,
                "downloaded": 500000,
                "finished_at": "2024-01-01T12:00:00",
                "estimated_time": 300,
                "status": "DOWNLOADING",
                "started_at": "2024-01-01T11:00:00",
                "error_message": null,
                "file_id": 456,
                "userfile_exists": true
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var transfer = JsonSerializer.Deserialize<PutioTransfer>(json, options);

        transfer.Should().NotBeNull();
        transfer!.Id.Should().Be(123);
        transfer.Hash.Should().Be("abc123def456");
        transfer.Name.Should().Be("Test Transfer");
        transfer.Size.Should().Be(1000000);
        transfer.Downloaded.Should().Be(500000);
        transfer.FinishedAt.Should().Be("2024-01-01T12:00:00");
        transfer.EstimatedTime.Should().Be(300);
        transfer.Status.Should().Be("DOWNLOADING");
        transfer.StartedAt.Should().Be("2024-01-01T11:00:00");
        transfer.ErrorMessage.Should().BeNull();
        transfer.FileId.Should().Be(456);
        transfer.UserfileExists.Should().BeTrue();
    }

    [Fact]
    public void PutioTransfer_Deserialization_WithNullFields()
    {
        var json = """
            {
                "id": 1,
                "hash": null,
                "name": null,
                "size": null,
                "downloaded": null,
                "finished_at": null,
                "estimated_time": null,
                "status": "QUEUED",
                "started_at": null,
                "error_message": null,
                "file_id": null,
                "userfile_exists": false
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var transfer = JsonSerializer.Deserialize<PutioTransfer>(json, options);

        transfer.Should().NotBeNull();
        transfer!.Id.Should().Be(1);
        transfer.Hash.Should().BeNull();
        transfer.Name.Should().BeNull();
        transfer.Size.Should().BeNull();
        transfer.Downloaded.Should().BeNull();
        transfer.FinishedAt.Should().BeNull();
        transfer.EstimatedTime.Should().BeNull();
        transfer.Status.Should().Be("QUEUED");
        transfer.StartedAt.Should().BeNull();
        transfer.ErrorMessage.Should().BeNull();
        transfer.FileId.Should().BeNull();
        transfer.UserfileExists.Should().BeFalse();
    }

    [Fact]
    public void ListTransferResponse_Deserialization()
    {
        var json = """
            {
                "transfers": [
                    {
                        "id": 1,
                        "status": "COMPLETED",
                        "file_id": 100,
                        "userfile_exists": true
                    },
                    {
                        "id": 2,
                        "status": "DOWNLOADING",
                        "file_id": null,
                        "userfile_exists": false
                    }
                ]
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var response = JsonSerializer.Deserialize<ListTransferResponse>(json, options);

        response.Should().NotBeNull();
        response!.Transfers.Should().HaveCount(2);
        response.Transfers[0].Id.Should().Be(1);
        response.Transfers[0].IsDownloadable.Should().BeTrue();
        response.Transfers[1].Id.Should().Be(2);
        response.Transfers[1].IsDownloadable.Should().BeFalse();
    }

    [Fact]
    public void AccountInfo_Deserialization()
    {
        var json = """
            {
                "info": {
                    "username": "testuser",
                    "mail": "test@example.com",
                    "account_active": true
                }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var response = JsonSerializer.Deserialize<AccountInfoResponse>(json, options);

        response.Should().NotBeNull();
        response!.Info.Username.Should().Be("testuser");
        response.Info.Mail.Should().Be("test@example.com");
        response.Info.AccountActive.Should().BeTrue();
    }

    [Fact]
    public void ListFileResponse_Deserialization()
    {
        var json = """
            {
                "files": [
                    {
                        "content_type": "video/x-matroska",
                        "id": 100,
                        "name": "movie.mkv",
                        "file_type": "VIDEO"
                    },
                    {
                        "content_type": "text/plain",
                        "id": 101,
                        "name": "readme.txt",
                        "file_type": "FILE"
                    }
                ],
                "parent": {
                    "content_type": "application/x-directory",
                    "id": 50,
                    "name": "Movies",
                    "file_type": "FOLDER"
                }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var response = JsonSerializer.Deserialize<ListFileResponse>(json, options);

        response.Should().NotBeNull();
        response!.Files.Should().HaveCount(2);
        response.Files[0].Name.Should().Be("movie.mkv");
        response.Files[0].FileType.Should().Be("VIDEO");
        response.Parent.Name.Should().Be("Movies");
        response.Parent.FileType.Should().Be("FOLDER");
    }

    [Fact]
    public void UrlResponse_Deserialization()
    {
        var json = """{"url": "https://download.put.io/v2/files/123/download"}""";

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var response = JsonSerializer.Deserialize<UrlResponse>(json, options);

        response.Should().NotBeNull();
        response!.Url.Should().Be("https://download.put.io/v2/files/123/download");
    }

    [Fact]
    public void PutioException_ShouldContainMessage()
    {
        var exception = new PutioException("Test error message");

        exception.Message.Should().Be("Test error message");
    }

    [Fact]
    public void PutioException_WithInnerException_ShouldContainBoth()
    {
        var innerException = new InvalidOperationException("Inner error");
        var exception = new PutioException("Outer error", innerException);

        exception.Message.Should().Be("Outer error");
        exception.InnerException.Should().BeSameAs(innerException);
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("DOWNLOADING")]
    [InlineData("SEEDING")]
    [InlineData("ERROR")]
    [InlineData("QUEUED")]
    [InlineData("IN_QUEUE")]
    public void PutioTransfer_Status_ShouldStoreAnyValue(string status)
    {
        var transfer = new PutioTransfer(
            Id: 1,
            Status: status
        );

        transfer.Status.Should().Be(status);
    }

    [Fact]
    public void PutioTransfer_ErrorMessage_ShouldBeStored()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            Status: "ERROR",
            ErrorMessage: "Download failed: connection timeout"
        );

        transfer.ErrorMessage.Should().Be("Download failed: connection timeout");
    }

    [Fact]
    public void PutioFileInfo_DefaultValues_ShouldBeEmpty()
    {
        var fileInfo = new PutioFileInfo(0);

        fileInfo.ContentType.Should().BeEmpty();
        fileInfo.Id.Should().Be(0);
        fileInfo.Name.Should().BeEmpty();
        fileInfo.FileType.Should().BeEmpty();
    }

    [Fact]
    public void AccountInfo_DefaultValues_ShouldBeEmpty()
    {
        var accountInfo = new AccountInfo();

        accountInfo.Username.Should().BeEmpty();
        accountInfo.Mail.Should().BeEmpty();
        accountInfo.AccountActive.Should().BeFalse();
    }

    [Fact]
    public void ListTransferResponse_EmptyTransfers_ShouldDeserialize()
    {
        var json = """{"transfers": []}""";

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var response = JsonSerializer.Deserialize<ListTransferResponse>(json, options);

        response.Should().NotBeNull();
        response!.Transfers.Should().BeEmpty();
    }

    [Fact]
    public void PutioTransfer_Timestamps_ShouldBeParsable()
    {
        var transfer = new PutioTransfer(
            Id: 1,
            Status: "COMPLETED",
            StartedAt: "2024-01-01T10:00:00",
            FinishedAt: "2024-01-01T11:30:00"
        );

        DateTime.TryParse(transfer.StartedAt, out var started).Should().BeTrue();
        DateTime.TryParse(transfer.FinishedAt, out var finished).Should().BeTrue();
        finished.Should().BeAfter(started);
    }

    [Fact]
    public void PutioClient_Constructor_ShouldNotThrow()
    {
        var action = () => new PutioClient("test-token");

        action.Should().NotThrow();
    }

    [Fact]
    public void PutioClient_Dispose_ShouldNotThrow()
    {
        var client = new PutioClient("test-token");

        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public void PutioClient_DoubleDispose_ShouldNotThrow()
    {
        var client = new PutioClient("test-token");

        client.Dispose();
        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public void GetTransferResponse_Deserialization()
    {
        var json = """
            {
                "transfer": {
                    "id": 123,
                    "status": "SEEDING",
                    "file_id": 456,
                    "userfile_exists": true
                }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var response = JsonSerializer.Deserialize<GetTransferResponse>(json, options);

        response.Should().NotBeNull();
        response!.Transfer.Should().NotBeNull();
        response.Transfer.Id.Should().Be(123);
        response.Transfer.Status.Should().Be("SEEDING");
        response.Transfer.IsDownloadable.Should().BeTrue();
    }
}
