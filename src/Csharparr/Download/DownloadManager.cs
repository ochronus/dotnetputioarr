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
    private readonly IPutioClient _putioClient;
    private readonly IArrClientFactory _arrClientFactory;
    private readonly ILogger<DownloadManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly Channel<TransferMessage> _transferChannel;
    private readonly Channel<DownloadTargetMessage> _downloadChannel;
    private readonly HashSet<ulong> _seenTransfers = [];
    private readonly Lock _seenLock = new();
    private readonly List<Task> _backgroundTasks = [];
    private readonly Lock _backgroundTasksLock = new();

    public DownloadManager(
        AppConfig config,
        IPutioClient putioClient,
        IArrClientFactory arrClientFactory,
        ILogger<DownloadManager> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _putioClient = putioClient;
        _arrClientFactory = arrClientFactory;
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
                        TrackBackgroundTask(WatchForImportWithErrorHandlingAsync(message.Transfer, cancellationToken));
                        break;

                    case TransferMessageType.Imported:
                        TrackBackgroundTask(WatchSeedingWithErrorHandlingAsync(message.Transfer, cancellationToken));
                        break;
                }
            }
            catch (PutioNotFoundException)
            {
                _logger.LogInformation("{Transfer}: no longer exists on put.io (deleted externally)", message.Transfer);
            }
            catch (PutioException ex)
            {
                _logger.LogWarning("Error processing {Transfer}: {Message}", message.Transfer, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing transfer message for {Transfer}", message.Transfer);
            }
        }
    }

    private async Task WatchForImportWithErrorHandlingAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        try
        {
            await WatchForImportAsync(transfer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, don't log as error
            _logger.LogDebug("{Transfer}: import watch cancelled", transfer);
        }
        catch (PutioNotFoundException)
        {
            // Transfer/file was deleted externally on put.io - this is expected
            _logger.LogInformation("{Transfer}: no longer exists on put.io (likely deleted externally)", transfer);
        }
        catch (PutioServerException ex)
        {
            // Server error - transient, will be retried by Polly
            _logger.LogWarning("{Transfer}: put.io server error ({Message}), will retry", transfer, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Transfer}: import watch failed unexpectedly", transfer);
        }
    }

    private async Task WatchSeedingWithErrorHandlingAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        try
        {
            await WatchSeedingAsync(transfer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, don't log as error
            _logger.LogDebug("{Transfer}: seeding watch cancelled", transfer);
        }
        catch (PutioNotFoundException)
        {
            // Transfer/file was deleted externally on put.io - this is expected, nothing to clean up
            _logger.LogInformation("{Transfer}: no longer exists on put.io (likely deleted externally)", transfer);
        }
        catch (PutioServerException ex)
        {
            // Server error - transient, will be retried by Polly
            _logger.LogWarning("{Transfer}: put.io server error ({Message}), will retry", transfer, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Transfer}: seeding watch failed unexpectedly", transfer);
        }
    }

    private void TrackBackgroundTask(Task task)
    {
        lock (_backgroundTasksLock)
        {
            // Clean up completed tasks to prevent unbounded growth
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
            _backgroundTasks.Add(task);
            _logger.LogDebug("Background tasks tracked: {Count}", _backgroundTasks.Count);
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
            catch (HttpRequestException ex)
            {
                // Network/HTTP errors during download - no stack trace needed
                _logger.LogWarning("Download failed for {Target}: {Message}", message.Target, ex.Message);
                message.CompletionSource.SetResult(DownloadStatus.Failed);
            }
            catch (PutioNotFoundException)
            {
                _logger.LogInformation("{Target}: file no longer exists on put.io", message.Target);
                message.CompletionSource.SetResult(DownloadStatus.Failed);
            }
            catch (PutioException ex)
            {
                _logger.LogWarning("Download failed for {Target}: {Message}", message.Target, ex.Message);
                message.CompletionSource.SetResult(DownloadStatus.Failed);
            }
            catch (IOException ex)
            {
                // File system errors - no stack trace needed
                _logger.LogWarning("Download failed for {Target}: {Message}", message.Target, ex.Message);
                message.CompletionSource.SetResult(DownloadStatus.Failed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error downloading target {Target}", message.Target);
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
                    catch (IOException ex)
                    {
                        _logger.LogWarning("{Target}: failed to create directory ({Message})", target, ex.Message);
                        return DownloadStatus.Failed;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning("{Target}: failed to create directory ({Message})", target, ex.Message);
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
                    _logger.LogWarning("{Target}: no URL found", target);
                    return DownloadStatus.Failed;
                }

                _logger.LogInformation("{Target}: download started", target);

                try
                {
                    await FetchFileAsync(target, cancellationToken);
                    _logger.LogInformation("{Target}: download succeeded", target);
                    return DownloadStatus.Success;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("{Target}: download failed ({Message})", target, ex.Message);
                    return DownloadStatus.Failed;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning("{Target}: download failed ({Message})", target, ex.Message);
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
            using var httpClient = _httpClientFactory.CreateClient("Downloads");
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

        return await RecurseDownloadTargetsAsync(
            transfer.FileId.Value,
            transfer.GetHash(),
            "",
            true,
            transfer.SaveParentId,
            cancellationToken);
    }

    private async Task<List<DownloadTarget>> RecurseDownloadTargetsAsync(
        long fileId,
        string hash,
        string basePath,
        bool topLevel,
        long? saveParentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = _config.DownloadDirectory;
        }

        var targets = new List<DownloadTarget>();
        var response = await _putioClient.ListFilesAsync(fileId, cancellationToken);
        string to;

        if (topLevel)
        {
            if (saveParentId.HasValue && saveParentId != _config.InstanceFolderId)
            {
                _logger.LogWarning("{Hash}: save_parent_id {Parent} does not match configured {Configured}", hash, saveParentId, _config.InstanceFolderId);
                return targets;
            }

            if (response.Parent.Id == _config.InstanceFolderId)
            {
                // Transfer root is the instance folder itself
                basePath = _config.DownloadDirectory;
            }
            else if (saveParentId.HasValue && saveParentId == _config.InstanceFolderId)
            {
                // Transfer root is a child of the instance folder; download directly under the configured directory
                basePath = _config.DownloadDirectory;
            }
            else
            {
                _logger.LogWarning("{Hash}: transfer parent {Parent} is not under configured instance folder {Configured}", hash, response.Parent.Id, _config.InstanceFolderId);
                return targets;
            }
        }

        to = Path.Combine(basePath, response.Parent.Name);

        switch (response.Parent.FileType.ToUpperInvariant())
        {
            case "FOLDER":
                if (DownloadHelpers.ShouldSkipDirectory(response.Parent.Name, _config.SkipDirectories))
                {
                    break;
                }

                var childTargets = new List<DownloadTarget>();
                foreach (var file in response.Files)
                {
                    var childTargetsForFile = await RecurseDownloadTargetsAsync(file.Id, hash, to, false, saveParentId, cancellationToken);
                    childTargets.AddRange(childTargetsForFile);
                }

                if (childTargets.Count > 0)
                {
                    targets.Add(new DownloadTarget(
                        To: to,
                        From: string.Empty,
                        TargetType: TargetType.Directory,
                        TopLevel: topLevel,
                        TransferHash: hash
                    ));

                    targets.AddRange(childTargets);
                }
                break;

            case "VIDEO":
            case "AUDIO":
            case "TEXT":
            case "FILE":
                if (!IsMediaFile(response.Parent.Name) && !string.Equals(response.Parent.FileType, "VIDEO", StringComparison.OrdinalIgnoreCase) && !string.Equals(response.Parent.FileType, "AUDIO", StringComparison.OrdinalIgnoreCase))
                {
                    // Only treat TEXT/FILE as targets when they look like subtitle or audio files
                    break;
                }

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

    private static bool IsSubtitle(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".srt" or ".sub" or ".vtt" or ".ssa" or ".ass";
    }

    private static bool IsAudioFile(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".flac" or ".cue" or ".m3u8";
    }

    private static bool IsMediaFile(string name)
    {
        return IsSubtitle(name) || IsAudioFile(name);
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
            var (imported, serviceName) = await _arrClientFactory.CheckImportedMultiServiceAsync(
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
                    await CleanupTransferAsync(transfer, cancellationToken);
                    _logger.LogInformation("{Transfer}: done seeding", transfer);
                    return;
                }
            }
            catch (PutioNotFoundException)
            {
                // Transfer was deleted externally - nothing more to do
                _logger.LogInformation("{Transfer}: no longer exists on put.io, cleanup complete", transfer);
                return;
            }
            catch (PutioServerException ex)
            {
                // Transient server error - will retry on next tick
                _logger.LogDebug("{Transfer}: put.io server error ({Message}), will retry", transfer, ex.Message);
            }
            catch (PutioException ex)
            {
                // Other put.io error - log briefly and retry
                _logger.LogWarning("{Transfer}: failed to get transfer status ({Message})", transfer, ex.Message);
            }
        }
    }

    /// <summary>
    /// Removes the transfer from put.io and deletes associated files.
    /// Handles "not found" errors gracefully (resource already deleted).
    /// </summary>
    private async Task CleanupTransferAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        // Remove transfer from put.io
        try
        {
            await _putioClient.RemoveTransferAsync(transfer.TransferId, cancellationToken);
            _logger.LogInformation("{Transfer}: removed from put.io", transfer);
        }
        catch (PutioNotFoundException)
        {
            _logger.LogDebug("{Transfer}: transfer already removed from put.io", transfer);
        }
        catch (PutioException ex)
        {
            _logger.LogWarning("{Transfer}: failed to remove transfer ({Message})", transfer, ex.Message);
        }

        // Delete remote files
        if (transfer.FileId.HasValue)
        {
            try
            {
                await _putioClient.DeleteFileAsync(transfer.FileId.Value, cancellationToken);
                _logger.LogInformation("{Transfer}: deleted remote files", transfer);
            }
            catch (PutioNotFoundException)
            {
                _logger.LogDebug("{Transfer}: remote files already deleted", transfer);
            }
            catch (PutioException ex)
            {
                _logger.LogWarning("{Transfer}: unable to delete remote files ({Message})", transfer, ex.Message);
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
                var transfers = await _putioClient.ListTransfersAsync(parentId: _config.InstanceFolderId, cancellationToken: cancellationToken);

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
            catch (PutioServerException ex)
            {
                // Transient server error - retry silently
                _logger.LogDebug("put.io server error ({Message}), will retry", ex.Message);
            }
            catch (PutioException ex)
            {
                _logger.LogWarning("List put.io transfers failed ({Message}), will retry", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error listing transfers, will retry");
            }
        }
    }

    private async Task CheckExistingTransfersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking unfinished transfers");

        try
        {
            var transfers = await _putioClient.ListTransfersAsync(parentId: _config.InstanceFolderId, cancellationToken: cancellationToken);

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
                    catch (PutioNotFoundException)
                    {
                        _logger.LogInformation("{Name}: no longer exists on put.io", name);
                    }
                    catch (PutioException ex)
                    {
                        _logger.LogWarning("Could not get target for {Name}: {Message}", name, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Unexpected error getting target for {Name}", name);
                    }
                }
            }
        }
        catch (PutioException ex)
        {
            _logger.LogError("Failed to list transfers: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing transfers");
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
