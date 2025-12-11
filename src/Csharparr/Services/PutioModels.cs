namespace Csharparr.Services;

/// <summary>
/// Response wrapper for account info API call
/// </summary>
public sealed record AccountInfoResponse(AccountInfo Info);

/// <summary>
/// Put.io account information
/// </summary>
public sealed record AccountInfo(
    string Username = "",
    string Mail = "",
    bool AccountActive = false
);

/// <summary>
/// Response wrapper for list transfers API call
/// </summary>
public sealed record ListTransferResponse(List<PutioTransfer> Transfers);

/// <summary>
/// Response wrapper for get transfer API call
/// </summary>
public sealed record GetTransferResponse(PutioTransfer Transfer);

/// <summary>
/// Represents a Put.io transfer (download task)
/// </summary>
public sealed record PutioTransfer(
    ulong Id,
    string? Hash = null,
    string? Name = null,
    long? Size = null,
    long? Downloaded = null,
    string? FinishedAt = null,
    long? EstimatedTime = null,
    string Status = "",
    string? StartedAt = null,
    string? ErrorMessage = null,
    long? FileId = null,
    bool UserfileExists = false
)
{
    /// <summary>
    /// Returns true if the transfer has a file_id and can be downloaded
    /// </summary>
    public bool IsDownloadable => FileId.HasValue;
}

/// <summary>
/// Response wrapper for list files API call
/// </summary>
public sealed record ListFileResponse(
    List<PutioFileInfo> Files,
    PutioFileInfo Parent
);

/// <summary>
/// Represents a Put.io file or directory
/// </summary>
public sealed record PutioFileInfo(
    long Id,
    string Name = "",
    string ContentType = "",
    string FileType = ""
);

/// <summary>
/// Response wrapper for file URL API call
/// </summary>
public sealed record UrlResponse(string Url = "");
