using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Csharparr.Services;

/// <summary>
/// Put.io API client for interacting with the put.io service
/// </summary>
public sealed class PutioClient : IPutioClient
{
    private const string BaseUrl = "https://api.put.io/v2";
    private const string UploadUrl = "https://upload.put.io/v2";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PutioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PutioClient(HttpClient httpClient, ILogger<PutioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets account information from put.io
    /// </summary>
    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting account info from put.io");

        var response = await _httpClient.GetAsync($"{BaseUrl}/account/info", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error getting put.io account info: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<AccountInfoResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize account info response");

        return result.Info;
    }

    /// <summary>
    /// Lists all transfers, optionally filtering by source
    /// </summary>
    public async Task<IReadOnlyList<PutioTransfer>> ListTransfersAsync(string? source = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing transfers from put.io{Source}", source is null ? string.Empty : $" for source {source}");

        var url = $"{BaseUrl}/transfers/list";
        if (!string.IsNullOrEmpty(source))
        {
            url += $"?source={Uri.EscapeDataString(source)}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error getting put.io transfers: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<ListTransferResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize transfer list response");

        if (string.IsNullOrEmpty(source))
        {
            return result.Transfers;
        }

        return result.Transfers
            .Where(t => string.Equals(t.Source, source, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets a specific transfer by ID
    /// </summary>
    public async Task<PutioTransfer> GetTransferAsync(ulong transferId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting transfer {TransferId} from put.io", transferId);

        var response = await _httpClient.GetAsync($"{BaseUrl}/transfers/{transferId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error getting put.io transfer id:{transferId}: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<GetTransferResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize transfer response");

        return result.Transfer;
    }

    /// <summary>
    /// Removes a transfer
    /// </summary>
    public async Task RemoveTransferAsync(ulong transferId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Removing transfer {TransferId} from put.io", transferId);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(transferId.ToString()), "transfer_ids" }
        };

        var response = await _httpClient.PostAsync($"{BaseUrl}/transfers/remove", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error removing put.io transfer id:{transferId}: {response.StatusCode}{errorBody}");
        }
    }

    /// <summary>
    /// Deletes a file or directory
    /// </summary>
    public async Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting file {FileId} from put.io", fileId);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(fileId.ToString()), "file_ids" }
        };

        var response = await _httpClient.PostAsync($"{BaseUrl}/files/delete", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error removing put.io file/directory id:{fileId}: {response.StatusCode}{errorBody}");
        }
    }

    /// <summary>
    /// Creates a folder under the specified parent
    /// </summary>
    public async Task<PutioFileInfo> CreateFolderAsync(string name, long parentId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating folder {Name} under parent {ParentId}", name, parentId);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },
            { new StringContent(parentId.ToString()), "parent_id" }
        };

        var response = await _httpClient.PostAsync($"{BaseUrl}/files/create-folder", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error creating folder {name}: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<CreateFolderResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize create folder response");

        return result.File;
    }

    /// <summary>
    /// Adds a new transfer from a URL or magnet link
    /// </summary>
    public async Task AddTransferAsync(string url, string source, long? parentId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Adding transfer to put.io: {Url} with source {Source}", url, source);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(url), "url" },
            { new StringContent(source), "source" }
        };

        if (parentId.HasValue)
        {
            content.Add(new StringContent(parentId.Value.ToString()), "save_parent_id");
        }

        var response = await _httpClient.PostAsync($"{BaseUrl}/transfers/add", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error adding url: {url} to put.io: {response.StatusCode}{errorBody}");
        }
    }

    /// <summary>
    /// Uploads a torrent file
    /// </summary>
    public async Task UploadFileAsync(byte[] data, string source, long? parentId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Uploading torrent file to put.io ({Size} bytes) with source {Source}", data.Length, source);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "foo.torrent");
        content.Add(new StringContent("foo.torrent"), "filename");
        content.Add(new StringContent(source), "source");

        if (parentId.HasValue)
        {
            content.Add(new StringContent(parentId.Value.ToString()), "parent_id");
        }

        var response = await _httpClient.PostAsync($"{UploadUrl}/files/upload", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error uploading file to put.io: {response.StatusCode}{errorBody}");
        }
    }

    /// <summary>
    /// Lists files in a directory
    /// </summary>
    public async Task<ListFileResponse> ListFilesAsync(long fileId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing files in directory {FileId} from put.io", fileId);

        var response = await _httpClient.GetAsync($"{BaseUrl}/files/list?parent_id={fileId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error listing put.io file/directory id:{fileId}: {response.StatusCode}{errorBody}");
        }

        return await response.Content.ReadFromJsonAsync<ListFileResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize file list response");
    }

    /// <summary>
    /// Gets the download URL for a file
    /// </summary>
    public async Task<string> GetFileUrlAsync(long fileId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting download URL for file {FileId} from put.io", fileId);

        var response = await _httpClient.GetAsync($"{BaseUrl}/files/{fileId}/url", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error getting url for put.io file id:{fileId}: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<UrlResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize URL response");

        return result.Url;
    }

    /// <summary>
    /// Gets a new OOB code for authentication
    /// </summary>
    public async Task<string> GetOobCodeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("https://api.put.io/v2/oauth2/oob/code?app_id=6487", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error getting put.io OOB: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken)
            ?? throw new PutioException("Failed to deserialize OOB response");

        if (!result.TryGetValue("code", out var code))
        {
            throw new PutioException("OOB code not found in response");
        }

        return code;
    }

    /// <summary>
    /// Checks if the OOB code has been linked and returns the OAuth token
    /// </summary>
    public async Task<string> CheckOobAsync(string oobCode, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"https://api.put.io/v2/oauth2/oob/code/{oobCode}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);
            throw new PutioException($"Error checking put.io OOB {oobCode}: {response.StatusCode}{errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken)
            ?? throw new PutioException("Failed to deserialize OOB check response");

        if (!result.TryGetValue("oauth_token", out var token))
        {
            throw new PutioException("OAuth token not found in response");
        }

        return token;
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
