namespace Csharparr.Services;

public interface IPutioClient
{
    Task<AccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PutioTransfer>> ListTransfersAsync(string? source = null, CancellationToken cancellationToken = default);
    Task<PutioTransfer> GetTransferAsync(ulong transferId, CancellationToken cancellationToken = default);
    Task RemoveTransferAsync(ulong transferId, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default);
    Task<PutioFileInfo> CreateFolderAsync(string name, long parentId, CancellationToken cancellationToken = default);
    Task AddTransferAsync(string url, string source, long? parentId = null, CancellationToken cancellationToken = default);
    Task UploadFileAsync(byte[] data, string source, long? parentId = null, CancellationToken cancellationToken = default);
    Task<ListFileResponse> ListFilesAsync(long fileId, CancellationToken cancellationToken = default);
    Task<string> GetFileUrlAsync(long fileId, CancellationToken cancellationToken = default);
    Task<string> GetOobCodeAsync(CancellationToken cancellationToken = default);
    Task<string> CheckOobAsync(string oobCode, CancellationToken cancellationToken = default);
}
