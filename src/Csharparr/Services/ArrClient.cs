using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Csharparr.Services;

/// <summary>
/// Client for interacting with Arr services (Sonarr/Radarr/Whisparr)
/// </summary>
public sealed class ArrClient : IDisposable
{
    public const string HttpClientName = "ArrClient";

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private bool _disposed;

    public ArrClient(string baseUrl, string apiKey) : this(baseUrl, apiKey, null) { }

    public ArrClient(string baseUrl, string apiKey, HttpClient? httpClient)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
    }

    /// <summary>
    /// Checks if a file has been imported by checking the history
    /// </summary>
    public async Task<bool> CheckImportedAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var inspected = 0;
        var page = 0;

        while (true)
        {
            var url = $"{_baseUrl}/api/v3/history?includeSeries=false&includeEpisode=false&page={page}&pageSize=1000";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
                throw new ArrClientException($"Error checking history from {_baseUrl}: {response.StatusCode}{errorBody}");
            }

            var historyResponse = await response.Content.ReadFromJsonAsync<HistoryResponse>(cancellationToken: cancellationToken)
                ?? throw new ArrClientException("Failed to deserialize history response");

            foreach (var record in historyResponse.Records)
            {
                if (record.EventType == "downloadFolderImported")
                {
                    if (record.Data.TryGetValue("droppedPath", out var droppedPath) && droppedPath == targetPath)
                    {
                        return true;
                    }
                }
                inspected++;
            }

            if (historyResponse.TotalRecords > inspected)
            {
                page++;
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Checks if a file has been imported by any of the configured services
    /// </summary>
    public static async Task<(bool Imported, string? ServiceName)> CheckImportedMultiServiceAsync(
        string targetPath,
        IEnumerable<Configuration.ArrServiceInfo> services,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var service in services)
        {
            try
            {
                using var client = new ArrClient(service.Url, service.ApiKey);
                var imported = await client.CheckImportedAsync(targetPath, cancellationToken);
                if (imported)
                {
                    return (true, service.Name);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to check import status from {ServiceName} at {ServiceUrl}",
                    service.Name, service.Url);
                continue;
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Attempts to read the error response body for better error messages
    /// </summary>
    private static async Task<string> TryReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                // Truncate very long responses
                if (body.Length > 500)
                {
                    body = body[..500] + "...";
                }
                return $" - {body}";
            }
        }
        catch
        {
            // Ignore errors reading the body
        }
        return string.Empty;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
