using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Csharparr.Services;

/// <summary>
/// Client for interacting with Arr services (Sonarr/Radarr/Whisparr)
/// </summary>
public sealed class ArrClient : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private bool _disposed;

    public ArrClient(string baseUrl, string apiKey) : this(baseUrl, apiKey, null) { }

    public ArrClient(string baseUrl, string apiKey, HttpClient? httpClient)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
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
                throw new ArrClientException($"Error checking history from {_baseUrl}: {response.StatusCode}");
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
            catch (Exception)
            {
                // Log the error but continue checking other services
                continue;
            }
        }

        return (false, null);
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

#region API Response Models

public sealed class HistoryResponse
{
    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("records")]
    public List<HistoryRecord> Records { get; set; } = [];
}

public sealed class HistoryRecord
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, string> Data { get; set; } = [];
}

#endregion

#region Exceptions

public class ArrClientException : Exception
{
    public ArrClientException(string message) : base(message) { }
    public ArrClientException(string message, Exception innerException) : base(message, innerException) { }
}

#endregion
