using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Csharparr.Services;

/// <summary>
/// Put.io API client for interacting with the put.io service
/// </summary>
public sealed class PutioClient : IDisposable
{
    private const string BaseUrl = "https://api.put.io/v2";
    private const string UploadUrl = "https://upload.put.io/v2";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PutioClient(string apiToken) : this(apiToken, null) { }

    public PutioClient(string apiToken, HttpClient? httpClient)
    {
        _apiToken = apiToken;
        _httpClient = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
    }

    /// <summary>
    /// Gets account information from put.io
    /// </summary>
    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/account/info", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error getting put.io account info: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<AccountInfoResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize account info response");

        return result.Info;
    }

    /// <summary>
    /// Lists all transfers
    /// </summary>
    public async Task<IReadOnlyList<PutioTransfer>> ListTransfersAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/transfers/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error getting put.io transfers: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<ListTransferResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize transfer list response");

        return result.Transfers;
    }

    /// <summary>
    /// Gets a specific transfer by ID
    /// </summary>
    public async Task<PutioTransfer> GetTransferAsync(ulong transferId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/transfers/{transferId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error getting put.io transfer id:{transferId}: {response.StatusCode}");
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
        using var content = new MultipartFormDataContent
        {
            { new StringContent(transferId.ToString()), "transfer_ids" }
        };

        var response = await _httpClient.PostAsync($"{BaseUrl}/transfers/remove", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error removing put.io transfer id:{transferId}: {response.StatusCode}");
        }
    }

    /// <summary>
    /// Deletes a file or directory
    /// </summary>
    public async Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(fileId.ToString()), "file_ids" }
        };

        var response = await _httpClient.PostAsync($"{BaseUrl}/files/delete", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error removing put.io file/directory id:{fileId}: {response.StatusCode}");
        }
    }

    /// <summary>
    /// Adds a new transfer from a URL or magnet link
    /// </summary>
    public async Task AddTransferAsync(string url, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(url), "url" }
        };

        var response = await _httpClient.PostAsync($"{BaseUrl}/transfers/add", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error adding url: {url} to put.io: {response.StatusCode}");
        }
    }

    /// <summary>
    /// Uploads a torrent file
    /// </summary>
    public async Task UploadFileAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "foo.torrent");
        content.Add(new StringContent("foo.torrent"), "filename");

        var response = await _httpClient.PostAsync($"{UploadUrl}/files/upload", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error uploading file to put.io: {response.StatusCode}");
        }
    }

    /// <summary>
    /// Lists files in a directory
    /// </summary>
    public async Task<ListFileResponse> ListFilesAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/files/list?parent_id={fileId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error listing put.io file/directory id:{fileId}: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<ListFileResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize file list response");
    }

    /// <summary>
    /// Gets the download URL for a file
    /// </summary>
    public async Task<string> GetFileUrlAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/files/{fileId}/url", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error getting url for put.io file id:{fileId}: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<UrlResponse>(JsonOptions, cancellationToken)
            ?? throw new PutioException("Failed to deserialize URL response");

        return result.Url;
    }

    /// <summary>
    /// Gets a new OOB code for authentication
    /// </summary>
    public static async Task<string> GetOobCodeAsync(CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync("https://api.put.io/v2/oauth2/oob/code?app_id=6487", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error getting put.io OOB: {response.StatusCode}");
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
    public static async Task<string> CheckOobAsync(string oobCode, CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"https://api.put.io/v2/oauth2/oob/code/{oobCode}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PutioException($"Error checking put.io OOB {oobCode}: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken)
            ?? throw new PutioException("Failed to deserialize OOB check response");

        if (!result.TryGetValue("oauth_token", out var token))
        {
            throw new PutioException("OAuth token not found in response");
        }

        return token;
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
