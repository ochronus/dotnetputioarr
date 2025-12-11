using System.Threading.Channels;
using Csharparr.Configuration;
using Csharparr.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Csharparr.Download;

/// <summary>
/// Manages download orchestration and worker coordination
/// </summary>
public sealed class DownloadManager : BackgroundService
{
    private readonly AppConfig _config;
    private readonly PutioClient _putioClient;
    private readonly ILogger<DownloadManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly Channel<TransferMessage> _transferChannel;
    private readonly Channel<DownloadTargetMessage> _downloadChannel;
    private readonly HashSet<ulong> _seenTransfers = [];
    private readonly Lock _seenLock = new();

    public DownloadManager(
        AppConfig config,
        PutioClient putioClient,
        ILogger<DownloadManager> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _putioClient = putioClient;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        _transferChannel = Channel.CreateBounded<TransferMessage>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _downloadChannel = Channel.CreateBounded<DownloadTargetMessage>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting download manager");

        // Start orchestration workers
        var orchestrationTasks = Enumerable.Range(0, _config.OrchestrationWorkers)
            .Select(i => OrchestrationWorkerAsync(i, stoppingToken))
            .ToList();

        // Start download workers
        var downloadTasks = Enumerable.Range(0, _config.DownloadWorkers)
            .Select(i => DownloadWorkerAsync(i, stoppingToken))
            .ToList();

        // Check existing transfers on startup
        await CheckExistingTransfersAsync(stoppingToken);

        // Start the transfer producer
        var producerTask = ProduceTransfersAsync(stoppingToken);

        // Wait for all tasks (they run until cancellation)
        await Task.WhenAll([producerTask, .. orchestrationTasks, .. downloadTasks]);
    }

    private async Task OrchestrationWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Orchestration worker {WorkerId} started", workerId);

        await foreach (var message in _transferChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                switch (message.Type)
                {
                    case TransferMessageType.QueuedForDownload:
                        await HandleQueuedForDownloadAsync(message.Transfer, cancellationToken);
                        break;

                    case TransferMessageType.Downloaded:
                        _ = WatchForImportAsync(message.Transfer, cancellationToken);
                        break;

                    case TransferMessageType.Imported:
                        _ = WatchSeedingAsync(message.Transfer, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing transfer message for {Transfer}", message.Transfer);
            }
        }
    }

    private async Task DownloadWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Download worker {WorkerId} started", workerId);

        await foreach (var message in _downloadChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var status = await DownloadTargetAsync(message.Target, cancellationToken);
                message.CompletionSource.SetResult(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading target {Target}", message.Target);
                message.CompletionSource.SetResult(DownloadStatus.Failed);
            }
        }
    }

    private async Task HandleQueuedForDownloadAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Transfer}: download started", transfer);

        var targets = await GetDownloadTargetsAsync(transfer, cancellationToken);
        if (targets.Count == 0)
        {
            _logger.LogWarning("{Transfer}: no download targets found", transfer);
            return;
        }

        // Queue all downloads and collect their completion tasks
        var downloadTasks = new List<Task<DownloadStatus>>();
        foreach (var target in targets)
        {
            var message = new DownloadTargetMessage { Target = target };
            await _downloadChannel.Writer.WriteAsync(message, cancellationToken);
            downloadTasks.Add(message.CompletionSource.Task);
        }

        // Wait for all downloads to complete
        var results = await Task.WhenAll(downloadTasks);
        var allSuccess = results.All(s => s == DownloadStatus.Success);

        if (allSuccess)
        {
            _logger.LogInformation("{Transfer}: download done", transfer);
            transfer.SetTargets(targets);
            await _transferChannel.Writer.WriteAsync(
                new TransferMessage(TransferMessageType.Downloaded, transfer),
                cancellationToken);
        }
        else
        {
            _logger.LogWarning("{Transfer}: not all targets downloaded", transfer);
        }
    }

    private async Task<DownloadStatus> DownloadTargetAsync(DownloadTarget target, CancellationToken cancellationToken)
    {
        switch (target.TargetType)
        {
            case TargetType.Directory:
                if (!Directory.Exists(target.To))
                {
                    try
                    {
                        Directory.CreateDirectory(target.To);
                        _logger.LogInformation("{Target}: directory created", target);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{Target}: failed to create directory", target);
                        return DownloadStatus.Failed;
                    }
                }
                return DownloadStatus.Success;

            case TargetType.File:
                if (File.Exists(target.To))
                {
                    _logger.LogInformation("{Target}: already exists", target);
                    return DownloadStatus.Success;
                }

                if (string.IsNullOrEmpty(target.From))
                {
                    _logger.LogError("{Target}: no URL found", target);
                    return DownloadStatus.Failed;
                }

                _logger.LogInformation("{Target}: download started", target);

                try
                {
                    await FetchFileAsync(target, cancellationToken);
                    _logger.LogInformation("{Target}: download succeeded", target);
                    return DownloadStatus.Success;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Target}: download failed", target);
                    return DownloadStatus.Failed;
                }

            default:
                return DownloadStatus.Failed;
        }
    }

    private async Task FetchFileAsync(DownloadTarget target, CancellationToken cancellationToken)
    {
        var tmpPath = target.To + ".downloading";

        // Create parent directory if needed
        var parentDir = Path.GetDirectoryName(target.To);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(target.From, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await contentStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { /* ignore */ }
            }
            throw;
        }

        // Rename to final location
        File.Move(tmpPath, target.To, overwrite: true);
    }

    private async Task<List<DownloadTarget>> GetDownloadTargetsAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Transfer}: generating targets", transfer);

        if (transfer.FileId is null)
        {
            _logger.LogError("{Transfer}: no file ID", transfer);
            return [];
        }

        return await RecurseDownloadTargetsAsync(transfer.FileId.Value, transfer.GetHash(), "", true, cancellationToken);
    }

    private async Task<List<DownloadTarget>> RecurseDownloadTargetsAsync(
        long fileId,
        string hash,
        string basePath,
        bool topLevel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = _config.DownloadDirectory;
        }

        var targets = new List<DownloadTarget>();
        var response = await _putioClient.ListFilesAsync(fileId, cancellationToken);
        var to = Path.Combine(basePath, response.Parent.Name);

        switch (response.Parent.FileType.ToUpperInvariant())
        {
            case "FOLDER":
                if (!DownloadHelpers.ShouldSkipDirectory(response.Parent.Name, _config.SkipDirectories))
                {
                    targets.Add(new DownloadTarget(
                        To: to,
                        From: string.Empty,
                        TargetType: TargetType.Directory,
                        TopLevel: topLevel,
                        TransferHash: hash
                    ));

                    foreach (var file in response.Files)
                    {
                        var childTargets = await RecurseDownloadTargetsAsync(file.Id, hash, to, false, cancellationToken);
                        targets.AddRange(childTargets);
                    }
                }
                break;

            case "VIDEO":
                var url = await _putioClient.GetFileUrlAsync(response.Parent.Id, cancellationToken);
                targets.Add(new DownloadTarget(
                    To: to,
                    From: url,
                    TargetType: TargetType.File,
                    TopLevel: topLevel,
                    TransferHash: hash
                ));
                break;
        }

        return targets;
    }

    private async Task WatchForImportAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Transfer}: watching imports", transfer);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.PollingInterval));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (await IsImportedAsync(transfer, cancellationToken))
            {
                _logger.LogInformation("{Transfer}: imported", transfer);

                // Clean up downloaded files
                var topLevel = transfer.GetTopLevel();
                if (topLevel is not null && (File.Exists(topLevel.To) || Directory.Exists(topLevel.To)))
                {
                    try
                    {
                        if (Directory.Exists(topLevel.To))
                        {
                            Directory.Delete(topLevel.To, recursive: true);
                        }
                        else
                        {
                            File.Delete(topLevel.To);
                        }
                        _logger.LogInformation("{TopLevel}: deleted", topLevel);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{TopLevel}: failed to delete", topLevel);
                    }
                }

                await _transferChannel.Writer.WriteAsync(
                    new TransferMessage(TransferMessageType.Imported, transfer),
                    cancellationToken);
                return;
            }
        }
    }

    private async Task<bool> IsImportedAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        var fileTargets = transfer.GetFileTargets();
        if (fileTargets.Count == 0)
        {
            return false;
        }

        var services = _config.GetArrServices().ToList();
        if (services.Count == 0)
        {
            return false;
        }

        foreach (var target in fileTargets)
        {
            var (imported, serviceName) = await ArrClient.CheckImportedMultiServiceAsync(
                target.To, services, cancellationToken);

            if (imported && serviceName is not null)
            {
                _logger.LogInformation("{Target}: found imported by {Service}", target, serviceName);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private async Task WatchSeedingAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Transfer}: watching seeding", transfer);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.PollingInterval));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var resp = await _putioClient.GetTransferAsync(transfer.TransferId, cancellationToken);

                if (resp.Status != "SEEDING")
                {
                    _logger.LogInformation("{Transfer}: stopped seeding", transfer);

                    // Remove transfer from put.io
                    try
                    {
                        await _putioClient.RemoveTransferAsync(transfer.TransferId, cancellationToken);
                        _logger.LogInformation("{Transfer}: removed from put.io", transfer);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Transfer}: failed to remove transfer", transfer);
                    }

                    // Delete remote files
                    if (transfer.FileId.HasValue)
                    {
                        try
                        {
                            await _putioClient.DeleteFileAsync(transfer.FileId.Value, cancellationToken);
                            _logger.LogInformation("{Transfer}: deleted remote files", transfer);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "{Transfer}: unable to delete remote files", transfer);
                        }
                    }

                    _logger.LogInformation("{Transfer}: done seeding", transfer);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Transfer}: failed to get transfer status", transfer);
            }
        }
    }

    private async Task ProduceTransfersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting to monitor transfers");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.PollingInterval));
        var lastLogTime = DateTime.UtcNow;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var transfers = await _putioClient.ListTransfersAsync(cancellationToken);

                foreach (var pt in transfers)
                {
                    if (IsSeen(pt.Id) || !pt.IsDownloadable)
                    {
                        continue;
                    }

                    var transfer = new Transfer(_config, pt);
                    _logger.LogInformation("{Transfer}: ready for download", transfer);

                    await _transferChannel.Writer.WriteAsync(
                        new TransferMessage(TransferMessageType.QueuedForDownload, transfer),
                        cancellationToken);

                    MarkSeen(pt.Id);
                }

                // Clean up seen list
                var activeIds = transfers.Select(t => t.Id).ToHashSet();
                CleanupSeen(activeIds);

                // Log status periodically
                if (DateTime.UtcNow - lastLogTime >= TimeSpan.FromMinutes(1))
                {
                    _logger.LogInformation("Active transfers: {Count}", transfers.Count);
                    foreach (var pt in transfers)
                    {
                        var transfer = new Transfer(_config, pt);
                        _logger.LogInformation("  {Transfer}", transfer);
                    }
                    lastLogTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "List put.io transfers failed. Retrying...");
            }
        }
    }

    private async Task CheckExistingTransfersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking unfinished transfers");

        try
        {
            var transfers = await _putioClient.ListTransfersAsync(cancellationToken);

            foreach (var pt in transfers)
            {
                var name = pt.Name ?? "??";
                var transfer = new Transfer(_config, pt);

                if (pt.IsDownloadable)
                {
                    _logger.LogInformation("Getting download target for {Name}", name);

                    try
                    {
                        var targets = await GetDownloadTargetsAsync(transfer, cancellationToken);
                        transfer.SetTargets(targets);

                        if (await IsImportedAsync(transfer, cancellationToken))
                        {
                            _logger.LogInformation("{Transfer}: already imported", transfer);
                            MarkSeen(transfer.TransferId);
                            await _transferChannel.Writer.WriteAsync(
                                new TransferMessage(TransferMessageType.Imported, transfer),
                                cancellationToken);
                        }
                        else
                        {
                            _logger.LogInformation("{Transfer}: not imported yet", transfer);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not get target for {Name}", name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list transfers");
        }

        _logger.LogInformation("Done checking for unfinished transfers. Starting to monitor transfers.");
    }

    private bool IsSeen(ulong id)
    {
        lock (_seenLock)
        {
            return _seenTransfers.Contains(id);
        }
    }

    private void MarkSeen(ulong id)
    {
        lock (_seenLock)
        {
            _seenTransfers.Add(id);
        }
    }

    private void CleanupSeen(HashSet<ulong> activeIds)
    {
        lock (_seenLock)
        {
            _seenTransfers.RemoveWhere(id => !activeIds.Contains(id));
        }
    }
}
