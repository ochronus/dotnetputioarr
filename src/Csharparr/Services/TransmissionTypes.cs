using System.Text.Json.Serialization;

namespace Csharparr.Services;

/// <summary>
/// Transmission RPC response
/// </summary>
public sealed class TransmissionResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = "success";

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Arguments { get; set; }
}

/// <summary>
/// Transmission RPC request
/// </summary>
public sealed class TransmissionRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Arguments { get; set; }
}

/// <summary>
/// Transmission session configuration
/// </summary>
public sealed class TransmissionConfig
{
    [JsonPropertyName("rpc-version")]
    public string RpcVersion { get; set; } = "18";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "14.0.0";

    [JsonPropertyName("download-dir")]
    public string DownloadDir { get; set; } = "/";

    [JsonPropertyName("seedRatioLimit")]
    public float SeedRatioLimit { get; set; } = 1.0f;

    [JsonPropertyName("seedRatioLimited")]
    public bool SeedRatioLimited { get; set; } = true;

    [JsonPropertyName("idle-seeding-limit")]
    public ulong IdleSeedingLimit { get; set; } = 100;

    [JsonPropertyName("idle-seeding-limit-enabled")]
    public bool IdleSeedingLimitEnabled { get; set; }

    /// <summary>
    /// Creates a default configuration with the specified download directory
    /// </summary>
    public static TransmissionConfig Default(string downloadDir) => new()
    {
        DownloadDir = downloadDir
    };
}

/// <summary>
/// Transmission torrent status values
/// </summary>
public enum TorrentStatus
{
    Stopped = 0,
    CheckWait = 1,
    Check = 2,
    Queued = 3,
    Downloading = 4,
    SeedingWait = 5,
    Seeding = 6
}

/// <summary>
/// Transmission torrent representation
/// </summary>
public sealed class TransmissionTorrent
{
    [JsonPropertyName("id")]
    public ulong Id { get; set; }

    [JsonPropertyName("hashString")]
    public string? HashString { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Unknown";

    [JsonPropertyName("downloadDir")]
    public string DownloadDir { get; set; } = string.Empty;

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("leftUntilDone")]
    public long LeftUntilDone { get; set; }

    [JsonPropertyName("isFinished")]
    public bool IsFinished { get; set; }

    [JsonPropertyName("eta")]
    public long Eta { get; set; }

    [JsonPropertyName("status")]
    public TorrentStatus Status { get; set; }

    [JsonPropertyName("secondsDownloading")]
    public long SecondsDownloading { get; set; }

    [JsonPropertyName("errorString")]
    public string? ErrorString { get; set; }

    [JsonPropertyName("downloadedEver")]
    public long DownloadedEver { get; set; }

    [JsonPropertyName("seedRatioLimit")]
    public float SeedRatioLimit { get; set; }

    [JsonPropertyName("seedRatioMode")]
    public uint SeedRatioMode { get; set; }

    [JsonPropertyName("seedIdleLimit")]
    public ulong SeedIdleLimit { get; set; }

    [JsonPropertyName("seedIdleMode")]
    public uint SeedIdleMode { get; set; }

    [JsonPropertyName("fileCount")]
    public uint FileCount { get; set; } = 1;

    /// <summary>
    /// Converts a put.io status string to a TorrentStatus
    /// </summary>
    public static TorrentStatus StatusFromString(string status) => status.ToUpperInvariant() switch
    {
        "STOPPED" or "COMPLETED" or "ERROR" => TorrentStatus.Stopped,
        "CHECKWAIT" or "PREPARING_DOWNLOAD" => TorrentStatus.CheckWait,
        "CHECK" or "COMPLETING" => TorrentStatus.Check,
        "QUEUED" or "IN_QUEUE" => TorrentStatus.Queued,
        "DOWNLOADING" => TorrentStatus.Downloading,
        "SEEDINGWAIT" => TorrentStatus.SeedingWait,
        "SEEDING" => TorrentStatus.Seeding,
        _ => TorrentStatus.CheckWait
    };

    /// <summary>
    /// Creates a TransmissionTorrent from a PutioTransfer
    /// </summary>
    public static TransmissionTorrent FromPutioTransfer(PutioTransfer transfer, string downloadDir)
    {
        var startedAt = DateTime.UtcNow;
        if (transfer.StartedAt is not null &&
            DateTime.TryParse(transfer.StartedAt, out var parsed))
        {
            startedAt = parsed;
        }

        var secondsDownloading = (long)(DateTime.UtcNow - startedAt).TotalSeconds;
        var name = transfer.Name ?? "Unknown";
        var totalSize = transfer.Size ?? 0;
        var downloaded = transfer.Downloaded ?? 0;
        var leftUntilDone = Math.Max(0, totalSize - downloaded);
        var eta = transfer.EstimatedTime ?? 0;

        return new TransmissionTorrent
        {
            Id = transfer.Id,
            HashString = transfer.Hash,
            Name = name,
            DownloadDir = downloadDir,
            TotalSize = totalSize,
            LeftUntilDone = leftUntilDone,
            IsFinished = transfer.FinishedAt is not null,
            Eta = eta,
            Status = StatusFromString(transfer.Status),
            SecondsDownloading = secondsDownloading,
            ErrorString = transfer.ErrorMessage,
            DownloadedEver = downloaded,
            SeedRatioLimit = 0.0f,
            SeedRatioMode = 0,
            SeedIdleLimit = 0,
            SeedIdleMode = 0,
            FileCount = 1
        };
    }
}

/// <summary>
/// Arguments for torrent-add method
/// </summary>
public sealed class TorrentAddArguments
{
    [JsonPropertyName("metainfo")]
    public string? Metainfo { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }
}

/// <summary>
/// Arguments for torrent-remove method
/// </summary>
public sealed class TorrentRemoveArguments
{
    [JsonPropertyName("ids")]
    public List<string> Ids { get; set; } = [];

    [JsonPropertyName("delete-local-data")]
    public bool DeleteLocalData { get; set; }
}

/// <summary>
/// Response for torrent-get method
/// </summary>
public sealed class TorrentGetResponse
{
    [JsonPropertyName("torrents")]
    public List<TransmissionTorrent> Torrents { get; set; } = [];
}
