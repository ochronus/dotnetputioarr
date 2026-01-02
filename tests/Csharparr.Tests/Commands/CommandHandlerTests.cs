using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Csharparr.Commands;
using Csharparr.Services;
using FluentAssertions;

namespace Csharparr.Tests.Commands;

public class CommandHandlerTests
{
    [Fact]
    public async Task CreateInstanceFolderAsync_WhenFolderExists_ReturnsExistingId()
    {
        var existingId = 55L;
        var client = new FakePutioClient([new PutioFileInfo(existingId, "myinstance", "", "FOLDER")]);

        var result = await CommandHandler.CreateInstanceFolderAsync("myinstance", "token", CancellationToken.None, client);

        result.Id.Should().Be(existingId);
        result.Created.Should().BeFalse();
        client.CreatedFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateInstanceFolderAsync_WhenFolderMissing_CreatesFolder()
    {
        var client = new FakePutioClient([]);

        var result = await CommandHandler.CreateInstanceFolderAsync("newinstance", "token", CancellationToken.None, client);

        result.Id.Should().BeGreaterThan(0);
        result.Created.Should().BeTrue();
        client.CreatedFolders.Should().HaveCount(1);
        client.CreatedFolders[0].Name.Should().Be("newinstance");
    }

    private sealed class FakePutioClient : IPutioClient
    {
        private readonly List<PutioFileInfo> _files;
        private long _nextId;

        public FakePutioClient(IEnumerable<PutioFileInfo> initialFiles)
        {
            _files = new List<PutioFileInfo>(initialFiles);
            _nextId = _files.Any() ? _files.Max(f => f.Id) + 1 : 100;
        }

        public List<PutioFileInfo> CreatedFolders { get; } = new();

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PutioTransfer>> ListTransfersAsync(string? source = null, long? parentId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<PutioTransfer> GetTransferAsync(ulong transferId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task RemoveTransferAsync(ulong transferId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<PutioFileInfo> CreateFolderAsync(string name, long parentId, CancellationToken cancellationToken = default)
        {
            var created = new PutioFileInfo(_nextId++, name, "", "FOLDER");
            _files.Add(created);
            CreatedFolders.Add(created);
            return Task.FromResult(created);
        }

        public Task AddTransferAsync(string url, string source, long? parentId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task UploadFileAsync(byte[] data, string source, long? parentId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ListFileResponse> ListFilesAsync(long fileId, CancellationToken cancellationToken = default)
        {
            var parent = new PutioFileInfo(fileId, "root", "", "FOLDER");
            return Task.FromResult(new ListFileResponse(_files, parent));
        }

        public Task<string> GetFileUrlAsync(long fileId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string> GetOobCodeAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string> CheckOobAsync(string oobCode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
