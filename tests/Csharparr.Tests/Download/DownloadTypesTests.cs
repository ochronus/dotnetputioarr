using Csharparr.Configuration;
using Csharparr.Download;
using Csharparr.Services;
using FluentAssertions;

namespace Csharparr.Tests.Download;

public class DownloadTypesTests
{
    [Fact]
    public void DownloadTarget_ToString_ShouldFormatCorrectly()
    {
        var target = new DownloadTarget
        {
            To = "/downloads/movie.mkv",
            TransferHash = "abcdef123456",
            TargetType = TargetType.File
        };

        var result = target.ToString();

        result.Should().Be("[abcd: /downloads/movie.mkv]");
    }

    [Fact]
    public void DownloadTarget_ToString_WithShortHash_ShouldNotCrash()
    {
        var target = new DownloadTarget
        {
            To = "/downloads/movie.mkv",
            TransferHash = "ab",
            TargetType = TargetType.File
        };

        var result = target.ToString();

        result.Should().Be("[ab: /downloads/movie.mkv]");
    }

    [Fact]
    public void DownloadTarget_File_ShouldHaveCorrectType()
    {
        var target = new DownloadTarget
        {
            To = "/downloads/file.mkv",
            From = "http://example.com/file.mkv",
            TargetType = TargetType.File
        };

        target.TargetType.Should().Be(TargetType.File);
        target.From.Should().NotBeEmpty();
    }

    [Fact]
    public void DownloadTarget_Directory_ShouldHaveEmptyFrom()
    {
        var target = new DownloadTarget
        {
            To = "/downloads/folder",
            From = string.Empty,
            TargetType = TargetType.Directory
        };

        target.TargetType.Should().Be(TargetType.Directory);
        target.From.Should().BeEmpty();
    }

    [Fact]
    public void Transfer_Constructor_ShouldInitializeCorrectly()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer
        {
            Id = 123,
            Name = "Test Transfer",
            FileId = 456,
            Hash = "abc123def456"
        };

        var transfer = new Transfer(config, putioTransfer);

        transfer.TransferId.Should().Be(123);
        transfer.Name.Should().Be("Test Transfer");
        transfer.FileId.Should().Be(456);
        transfer.Hash.Should().Be("abc123def456");
        transfer.Config.Should().BeSameAs(config);
    }

    [Fact]
    public void Transfer_Constructor_WithNullName_ShouldDefaultToUnknown()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer
        {
            Id = 123,
            Name = null
        };

        var transfer = new Transfer(config, putioTransfer);

        transfer.Name.Should().Be("Unknown");
    }

    [Fact]
    public void Transfer_GetHash_WithHash_ShouldReturnHash()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer
        {
            Id = 1,
            Hash = "abcd1234"
        };

        var transfer = new Transfer(config, putioTransfer);

        transfer.GetHash().Should().Be("abcd1234");
    }

    [Fact]
    public void Transfer_GetHash_WithNullHash_ShouldReturnDefault()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer
        {
            Id = 1,
            Hash = null
        };

        var transfer = new Transfer(config, putioTransfer);

        transfer.GetHash().Should().Be("0000");
    }

    [Fact]
    public void Transfer_ToString_ShouldFormatCorrectly()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer
        {
            Id = 1,
            Name = "Test Movie",
            Hash = "abcdef123456"
        };

        var transfer = new Transfer(config, putioTransfer);

        transfer.ToString().Should().Be("[abcd: Test Movie]");
    }

    [Fact]
    public void Transfer_ToString_WithNullHash_ShouldUseDefault()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer
        {
            Id = 1,
            Name = "Test Movie",
            Hash = null
        };

        var transfer = new Transfer(config, putioTransfer);

        transfer.ToString().Should().Be("[0000: Test Movie]");
    }

    [Fact]
    public void Transfer_SetAndGetTargets_ShouldWork()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1 };
        var transfer = new Transfer(config, putioTransfer);

        var targets = new List<DownloadTarget>
        {
            new() { To = "/path1", TargetType = TargetType.Directory, TopLevel = true },
            new() { To = "/path2", TargetType = TargetType.File, TopLevel = false }
        };

        transfer.SetTargets(targets);
        var result = transfer.GetTargets();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Transfer_GetTopLevel_ShouldReturnTopLevelTarget()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1 };
        var transfer = new Transfer(config, putioTransfer);

        var targets = new List<DownloadTarget>
        {
            new() { To = "/path1", TargetType = TargetType.Directory, TopLevel = true },
            new() { To = "/path2", TargetType = TargetType.File, TopLevel = false }
        };

        transfer.SetTargets(targets);
        var topLevel = transfer.GetTopLevel();

        topLevel.Should().NotBeNull();
        topLevel!.To.Should().Be("/path1");
        topLevel.TopLevel.Should().BeTrue();
    }

    [Fact]
    public void Transfer_GetTopLevel_WithNoTopLevel_ShouldReturnNull()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1 };
        var transfer = new Transfer(config, putioTransfer);

        var targets = new List<DownloadTarget>
        {
            new() { To = "/path1", TopLevel = false },
            new() { To = "/path2", TopLevel = false }
        };

        transfer.SetTargets(targets);
        var topLevel = transfer.GetTopLevel();

        topLevel.Should().BeNull();
    }

    [Fact]
    public void Transfer_GetFileTargets_ShouldReturnOnlyFiles()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1 };
        var transfer = new Transfer(config, putioTransfer);

        var targets = new List<DownloadTarget>
        {
            new() { To = "/path1", TargetType = TargetType.Directory },
            new() { To = "/path2", TargetType = TargetType.File },
            new() { To = "/path3", TargetType = TargetType.File }
        };

        transfer.SetTargets(targets);
        var fileTargets = transfer.GetFileTargets();

        fileTargets.Should().HaveCount(2);
        fileTargets.Should().OnlyContain(t => t.TargetType == TargetType.File);
    }

    [Fact]
    public void Transfer_GetTargets_WithNoTargets_ShouldReturnEmpty()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1 };
        var transfer = new Transfer(config, putioTransfer);

        var targets = transfer.GetTargets();

        targets.Should().BeEmpty();
    }

    [Fact]
    public void TransferMessageType_Values_ShouldBeCorrect()
    {
        ((int)TransferMessageType.QueuedForDownload).Should().Be(0);
        ((int)TransferMessageType.Downloaded).Should().Be(1);
        ((int)TransferMessageType.Imported).Should().Be(2);
    }

    [Fact]
    public void TransferMessage_ShouldStoreValues()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1, Name = "Test" };
        var transfer = new Transfer(config, putioTransfer);

        var message = new TransferMessage(TransferMessageType.Downloaded, transfer);

        message.Type.Should().Be(TransferMessageType.Downloaded);
        message.Transfer.Should().BeSameAs(transfer);
    }

    [Fact]
    public void DownloadStatus_Values_ShouldBeCorrect()
    {
        ((int)DownloadStatus.Success).Should().Be(0);
        ((int)DownloadStatus.Failed).Should().Be(1);
    }

    [Fact]
    public void DownloadTargetMessage_ShouldHaveCompletionSource()
    {
        var target = new DownloadTarget { To = "/test" };
        var message = new DownloadTargetMessage { Target = target };

        message.Target.Should().BeSameAs(target);
        (message.CompletionSource is not null).Should().BeTrue();
        message.CompletionSource.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void DownloadTargetMessage_CompletionSource_ShouldComplete()
    {
        var target = new DownloadTarget { To = "/test" };
        var message = new DownloadTargetMessage { Target = target };

        message.CompletionSource.SetResult(DownloadStatus.Success);

        message.CompletionSource.Task.IsCompleted.Should().BeTrue();
        message.CompletionSource.Task.Result.Should().Be(DownloadStatus.Success);
    }

    [Theory]
    [InlineData("sample", true)]
    [InlineData("Sample", true)]
    [InlineData("SAMPLE", true)]
    [InlineData("extras", true)]
    [InlineData("Extras", true)]
    [InlineData("movie", false)]
    [InlineData("featurettes", false)]
    public void ShouldSkipDirectory_ShouldMatchCaseInsensitively(string dirName, bool expected)
    {
        var skipDirs = new[] { "sample", "extras" };

        var result = DownloadHelpers.ShouldSkipDirectory(dirName, skipDirs);

        result.Should().Be(expected);
    }

    [Fact]
    public void ShouldSkipDirectory_WithEmptyList_ShouldReturnFalse()
    {
        var skipDirs = Array.Empty<string>();

        var result = DownloadHelpers.ShouldSkipDirectory("sample", skipDirs);

        result.Should().BeFalse();
    }

    [Fact]
    public void TargetType_Values_ShouldBeCorrect()
    {
        ((int)TargetType.Directory).Should().Be(0);
        ((int)TargetType.File).Should().Be(1);
    }

    [Fact]
    public void DownloadTarget_TopLevel_DefaultShouldBeFalse()
    {
        var target = new DownloadTarget { To = "/test" };

        target.TopLevel.Should().BeFalse();
    }

    [Fact]
    public void DownloadTarget_TransferHash_DefaultShouldBeEmpty()
    {
        var target = new DownloadTarget { To = "/test" };

        target.TransferHash.Should().BeEmpty();
    }

    [Fact]
    public void Transfer_ThreadSafety_SetAndGetTargets()
    {
        var config = new AppConfig();
        var putioTransfer = new PutioTransfer { Id = 1 };
        var transfer = new Transfer(config, putioTransfer);

        var targets1 = new List<DownloadTarget> { new() { To = "/path1" } };
        var targets2 = new List<DownloadTarget> { new() { To = "/path2" } };

        // Simulate concurrent access
        var task1 = Task.Run(() => transfer.SetTargets(targets1));
        var task2 = Task.Run(() => transfer.SetTargets(targets2));
        var task3 = Task.Run(() => transfer.GetTargets());

        var action = () => Task.WaitAll(task1, task2, task3);

        action.Should().NotThrow();
    }
}
