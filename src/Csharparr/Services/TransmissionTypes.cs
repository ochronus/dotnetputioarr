using System.Text.Json.Serialization;

namespace Csharparr.Services;

/// <summary>
/// Transmission RPC response
/// </summary>
public sealed record TransmissionResponse(
    [property: JsonPropertyName("result")]
    string Result = "success",

    [property: JsonPropertyName("arguments")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Arguments = null
);

/// <summary>
/// Transmission RPC request
/// </summary>
public sealed record TransmissionRequest(
    [property: JsonPropertyName("method")]
    string Method = "",

    [property: JsonPropertyName("arguments")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Arguments = null
);

/// <summary>
/// Transmission session configuration
/// </summary>
public sealed record TransmissionConfig(
    [property: JsonPropertyName("rpc-version")]
    string RpcVersion = "18",

    [property: JsonPropertyName("version")]
    string Version = "14.0.0",

    [property: JsonPropertyName("download-dir")]
    string DownloadDir = "/",

    [property: JsonPropertyName("seedRatioLimit")]
    float SeedRatioLimit = 1.0f,

    [property: JsonPropertyName("seedRatioLimited")]
    bool SeedRatioLimited = true,

    [property: JsonPropertyName("idle-seeding-limit")]
    ulong IdleSeedingLimit = 100,

    [property: JsonPropertyName("idle-seeding-limit-enabled")]
    bool IdleSeedingLimitEnabled = false
)
{
    /// <summary>
    /// Creates a default configuration with the specified download directory
    /// </summary>
    public static TransmissionConfig Default(string downloadDir) => new(DownloadDir: downloadDir);
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
public sealed record TorrentAddArguments(
    [property: JsonPropertyName("metainfo")]
    string? Metainfo = null,

    [property: JsonPropertyName("filename")]
    string? Filename = null
);

/// <summary>
/// Arguments for torrent-remove method
/// </summary>
public sealed record TorrentRemoveArguments(
    [property: JsonPropertyName("ids")]
    List<string> Ids,

    [property: JsonPropertyName("delete-local-data")]
    bool DeleteLocalData = false
);

/// <summary>
/// Response for torrent-get method
/// </summary>
public sealed record TorrentGetResponse(
    [property: JsonPropertyName("torrents")]
    List<TransmissionTorrent> Torrents
);
