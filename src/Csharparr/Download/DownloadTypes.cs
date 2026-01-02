using Csharparr.Configuration;
using Csharparr.Services;

namespace Csharparr.Download;

/// <summary>
/// Represents the type of download target
/// </summary>
public enum TargetType
{
    Directory,
    File
}

/// <summary>
/// Represents a file or directory to be downloaded
/// </summary>
/// <param name="To">Destination path to download to</param>
/// <param name="From">Source URL to download from (empty for directories)</param>
/// <param name="TargetType">Type of the target (file or directory)</param>
/// <param name="TopLevel">Whether this is the top-level target for a transfer</param>
/// <param name="TransferHash">Hash of the parent transfer</param>
public sealed record DownloadTarget(
    string To,
    string From = "",
    TargetType TargetType = TargetType.File,
    bool TopLevel = false,
    string TransferHash = ""
)
{
    public override string ToString()
    {
        var hash = TransferHash.Length > 4 ? TransferHash[..4] : TransferHash;
        return $"[{hash}: {To}]";
    }
}

/// <summary>
/// Represents a put.io transfer being processed
/// </summary>
public sealed class Transfer
{
    private readonly Lock _lock = new();
    private List<DownloadTarget> _targets = [];

    public string Name { get; }
    public long? FileId { get; }
    public string? Hash { get; }
    public ulong TransferId { get; }
    public AppConfig Config { get; }
    public long? SaveParentId { get; }

    public Transfer(AppConfig config, PutioTransfer transfer)
    {
        Config = config;
        TransferId = transfer.Id;
        Name = transfer.Name ?? "Unknown";
        FileId = transfer.FileId;
        Hash = transfer.Hash;
        SaveParentId = transfer.SaveParentId;
    }

    /// <summary>
    /// Gets the hash or a default value
    /// </summary>
    public string GetHash() => Hash ?? "0000";

    /// <summary>
    /// Sets the download targets for this transfer
    /// </summary>
    public void SetTargets(List<DownloadTarget> targets)
    {
        lock (_lock)
        {
            _targets = targets;
        }
    }

    /// <summary>
    /// Gets the download targets for this transfer
    /// </summary>
    public IReadOnlyList<DownloadTarget> GetTargets()
    {
        lock (_lock)
        {
            return [.. _targets];
        }
    }

    /// <summary>
    /// Gets the top-level download target
    /// </summary>
    public DownloadTarget? GetTopLevel()
    {
        lock (_lock)
        {
            return _targets.FirstOrDefault(t => t.TopLevel);
        }
    }

    /// <summary>
    /// Gets only file targets (not directories)
    /// </summary>
    public IReadOnlyList<DownloadTarget> GetFileTargets()
    {
        lock (_lock)
        {
            return _targets.Where(t => t.TargetType == TargetType.File).ToList();
        }
    }

    public override string ToString()
    {
        var hash = Hash is not null && Hash.Length >= 4 ? Hash[..4] : "0000";
        return $"[{hash}: {Name}]";
    }
}

/// <summary>
/// Type of transfer message for the orchestration pipeline
/// </summary>
public enum TransferMessageType
{
    /// <summary>
    /// The transfer is ready to be downloaded
    /// </summary>
    QueuedForDownload,

    /// <summary>
    /// The transfer has been downloaded
    /// </summary>
    Downloaded,

    /// <summary>
    /// The transfer has been imported by arr services
    /// </summary>
    Imported
}

/// <summary>
/// Message about transfer state changes
/// </summary>
public sealed record TransferMessage(TransferMessageType Type, Transfer Transfer);

/// <summary>
/// Result of a download operation
/// </summary>
public enum DownloadStatus
{
    Success,
    Failed
}

/// <summary>
/// Message to download a specific target
/// </summary>
public sealed class DownloadTargetMessage
{
    public required DownloadTarget Target { get; init; }
    public TaskCompletionSource<DownloadStatus> CompletionSource { get; } = new();
}

/// <summary>
/// Helper methods for download operations
/// </summary>
public static class DownloadHelpers
{
    /// <summary>
    /// Checks if a directory should be skipped based on configuration
    /// </summary>
    public static bool ShouldSkipDirectory(string name, IEnumerable<string> skipDirs)
    {
        var lowerName = name.ToLowerInvariant();
        return skipDirs.Any(d => d.Equals(lowerName, StringComparison.OrdinalIgnoreCase));
    }
}
