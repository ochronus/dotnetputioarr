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
public sealed class DownloadTarget
{
    /// <summary>
    /// Source URL to download from (empty for directories)
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Destination path to download to
    /// </summary>
    public required string To { get; set; }

    /// <summary>
    /// Type of the target (file or directory)
    /// </summary>
    public TargetType TargetType { get; set; }

    /// <summary>
    /// Whether this is the top-level target for a transfer
    /// </summary>
    public bool TopLevel { get; set; }

    /// <summary>
    /// Hash of the parent transfer
    /// </summary>
    public string TransferHash { get; set; } = string.Empty;

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

    public Transfer(AppConfig config, PutioTransfer transfer)
    {
        Config = config;
        TransferId = transfer.Id;
        Name = transfer.Name ?? "Unknown";
        FileId = transfer.FileId;
        Hash = transfer.Hash;
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
