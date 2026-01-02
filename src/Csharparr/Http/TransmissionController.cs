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
            _logger.LogWarning(ex, "Unknown method: {Method}", request.Method);
            return BadRequest(new { error = "unknown method" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC request: {Method}", request.Method);
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove transfer {TransferId}", transfer.Id);
                    continue;
                }

                if (transfer.UserfileExists && args.DeleteLocalData && transfer.FileId.HasValue)
                {
                    try
                    {
                        await _putioClient.DeleteFileAsync(transfer.FileId.Value, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete file {FileId}", transfer.FileId);
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
