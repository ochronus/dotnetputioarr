using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Csharparr.Configuration;
using Csharparr.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Csharparr.Http;

/// <summary>
/// Controller handling Transmission RPC protocol endpoints
/// </summary>
[ApiController]
[Route("transmission")]
public class TransmissionController : ControllerBase
{
    private const string SessionId = "useless-session-id";

    private readonly AppConfig _config;
    private readonly IPutioClient _putioClient;
    private readonly ILogger<TransmissionController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TransmissionController(
        AppConfig config,
        IPutioClient putioClient,
        ILogger<TransmissionController> logger)
    {
        _config = config;
        _putioClient = putioClient;
        _logger = logger;
    }

    /// <summary>
    /// Handles GET requests to the Transmission RPC endpoint (for authentication)
    /// </summary>
    [HttpGet("rpc")]
    public IActionResult RpcGet()
    {
        if (!ValidateUser())
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Response.Headers["X-Transmission-Session-Id"] = SessionId;
        return StatusCode(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Handles POST requests to the Transmission RPC endpoint
    /// </summary>
    [HttpPost("rpc")]
    public async Task<IActionResult> RpcPost([FromBody] TransmissionRequest request, CancellationToken cancellationToken)
    {
        if (!ValidateUser())
        {
            Response.Headers["X-Transmission-Session-Id"] = SessionId;
            return StatusCode(StatusCodes.Status409Conflict);
        }

        try
        {
            object? arguments = request.Method switch
            {
                "session-get" => TransmissionConfig.Default(_config.DownloadDirectory),
                "session-stats" => await HandleSessionStatsAsync(cancellationToken),
                "torrent-get" => await HandleTorrentGetAsync(cancellationToken),
                "torrent-set" => null,
                "queue-move-top" => null,
                "torrent-remove" => await HandleTorrentRemoveAsync(request, cancellationToken),
                "torrent-add" => await HandleTorrentAddAsync(request, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown method: {request.Method}")
            };

            return Ok(new TransmissionResponse("success", arguments));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unknown method:"))
        {
            _logger.LogWarning("Unknown RPC method: {Method}", request.Method);
            return BadRequest(new { error = "unknown method" });
        }
        catch (PutioNotFoundException ex)
        {
            // Resource was deleted externally - not an error worth logging with stack trace
            _logger.LogInformation("Resource not found during RPC {Method}: {Message}", request.Method, ex.Message);
            return Ok(new TransmissionResponse("success", null));
        }
        catch (PutioException ex)
        {
            _logger.LogWarning("put.io error during RPC {Method}: {Message}", request.Method, ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling RPC request: {Method}", request.Method);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    private async Task<TorrentGetResponse> HandleTorrentGetAsync(CancellationToken cancellationToken)
    {
        var transfers = await _putioClient.ListTransfersAsync(parentId: _config.InstanceFolderId, cancellationToken: cancellationToken);

        var torrents = transfers
            .Select(t => TransmissionTorrent.FromPutioTransfer(t, _config.DownloadDirectory))
            .ToList();

        return new TorrentGetResponse(torrents);
    }

    private async Task<SessionStatsResponse> HandleSessionStatsAsync(CancellationToken cancellationToken)
    {
        var transfers = await _putioClient.ListTransfersAsync(parentId: _config.InstanceFolderId, cancellationToken: cancellationToken);

        // Aggregate statistics from all transfers
        var activeTorrentCount = 0;
        var pausedTorrentCount = 0;
        long downloadSpeed = 0;
        long uploadSpeed = 0;
        long totalDownloaded = 0;
        long totalUploaded = 0;
        long totalSecondsSeeding = 0;

        foreach (var transfer in transfers)
        {
            var status = transfer.Status.ToUpperInvariant();

            if (status is "DOWNLOADING" or "SEEDING")
            {
                activeTorrentCount++;
                downloadSpeed += transfer.DownSpeed ?? 0;
                uploadSpeed += transfer.UpSpeed ?? 0;
            }
            else if (status is "STOPPED")
            {
                pausedTorrentCount++;
            }

            totalDownloaded += transfer.Downloaded ?? 0;
            totalUploaded += transfer.Uploaded ?? 0;
            totalSecondsSeeding += transfer.SecondsSeeding ?? 0;
        }

        var cumulativeStats = new SessionStatsCumulative(
            UploadedBytes: totalUploaded,
            DownloadedBytes: totalDownloaded,
            FilesAdded: transfers.Count,
            SessionCount: 1,
            SecondsActive: totalSecondsSeeding
        );

        return new SessionStatsResponse(
            ActiveTorrentCount: activeTorrentCount,
            DownloadSpeed: downloadSpeed,
            UploadSpeed: uploadSpeed,
            PausedTorrentCount: pausedTorrentCount,
            TorrentCount: transfers.Count,
            CumulativeStats: cumulativeStats,
            CurrentStats: cumulativeStats
        );
    }

    private async Task<object?> HandleTorrentAddAsync(TransmissionRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments is null)
        {
            return null;
        }

        var argsJson = JsonSerializer.Serialize(request.Arguments);
        var args = JsonSerializer.Deserialize<TorrentAddArguments>(argsJson, JsonOptions);

        if (args is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(args.Metainfo))
        {
            // .torrent file encoded as base64
            var data = Convert.FromBase64String(args.Metainfo);
            await _putioClient.UploadFileAsync(data, _config.InstanceName, _config.InstanceFolderId, cancellationToken);
            _logger.LogInformation("[ffff: unknown]: torrent uploaded");
        }
        else if (!string.IsNullOrEmpty(args.Filename))
        {
            // Magnet link
            await _putioClient.AddTransferAsync(args.Filename, _config.InstanceName, _config.InstanceFolderId, cancellationToken);

            // Try to extract name from magnet link
            var name = "unknown";
            if (args.Filename.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(args.Filename);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var dn = query["dn"];
                    if (!string.IsNullOrEmpty(dn))
                    {
                        name = Uri.UnescapeDataString(dn);
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }

            _logger.LogInformation("[ffff: {Name}]: magnet link uploaded", name);
        }

        return null;
    }

    private async Task<object?> HandleTorrentRemoveAsync(TransmissionRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments is null)
        {
            return null;
        }

        var argsJson = JsonSerializer.Serialize(request.Arguments);
        var args = JsonSerializer.Deserialize<TorrentRemoveArguments>(argsJson, JsonOptions);

        if (args is null || args.Ids.Count == 0)
        {
            return null;
        }

        // Get all transfers to match by hash
        var transfers = await _putioClient.ListTransfersAsync(parentId: _config.InstanceFolderId, cancellationToken: cancellationToken);

        // Build a set of hashes to remove
        var hashSet = args.Ids.ToHashSet();

        // Find and remove matching transfers
        foreach (var transfer in transfers)
        {
            if (transfer.Hash is null)
            {
                continue;
            }

            if (hashSet.Contains(transfer.Hash))
            {
                try
                {
                    await _putioClient.RemoveTransferAsync(transfer.Id, cancellationToken);
                }
                catch (PutioNotFoundException)
                {
                    // Already removed - not an error
                    _logger.LogDebug("Transfer {TransferId} already removed", transfer.Id);
                }
                catch (PutioException ex)
                {
                    _logger.LogWarning("Failed to remove transfer {TransferId}: {Message}", transfer.Id, ex.Message);
                    continue;
                }

                if (transfer.UserfileExists && args.DeleteLocalData && transfer.FileId.HasValue)
                {
                    try
                    {
                        await _putioClient.DeleteFileAsync(transfer.FileId.Value, cancellationToken);
                    }
                    catch (PutioNotFoundException)
                    {
                        // Already deleted - not an error
                        _logger.LogDebug("File {FileId} already deleted", transfer.FileId);
                    }
                    catch (PutioException ex)
                    {
                        _logger.LogWarning("Failed to delete file {FileId}: {Message}", transfer.FileId, ex.Message);
                    }
                }
            }
        }

        return null;
    }

    private bool ValidateUser()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader))
        {
            return false;
        }

        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encoded = authHeader["Basic ".Length..];
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);

            if (parts.Length != 2)
            {
                return false;
            }

            var username = parts[0];
            var password = parts[1];

            return username == _config.Username && password == _config.Password;
        }
        catch
        {
            return false;
        }
    }
}
