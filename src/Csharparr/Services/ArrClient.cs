using System.Net.Http.Json;

namespace Csharparr.Services;

/// <summary>
/// Client for interacting with Arr services (Sonarr/Radarr/Whisparr)
/// </summary>
public sealed class ArrClient
{
    private readonly HttpClient _httpClient;

    public ArrClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
            var url = $"api/v3/history?includeSeries=false&includeEpisode=false&page={page}&pageSize=1000";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
                throw new ArrClientException($"Error checking history from {_httpClient.BaseAddress}: {response.StatusCode}{errorBody}");
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
}
