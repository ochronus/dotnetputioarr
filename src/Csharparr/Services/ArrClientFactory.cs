using Csharparr.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using System.Net.Sockets;

namespace Csharparr.Services;

public interface IArrClientFactory
{
    Task<(bool Imported, string? ServiceName)> CheckImportedMultiServiceAsync(
        string targetPath,
        IEnumerable<ArrServiceInfo> services,
        CancellationToken cancellationToken = default);
}

public class ArrClientFactory : IArrClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ArrClientFactory> _logger;

    public ArrClientFactory(IHttpClientFactory httpClientFactory, ILogger<ArrClientFactory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(bool Imported, string? ServiceName)> CheckImportedMultiServiceAsync(
        string targetPath,
        IEnumerable<ArrServiceInfo> services,
        CancellationToken cancellationToken = default)
    {
        foreach (var service in services)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("ArrClient");
                httpClient.BaseAddress = new Uri(service.Url);
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", service.ApiKey);

                var client = new ArrClient(httpClient);
                var imported = await client.CheckImportedAsync(targetPath, cancellationToken);
                if (imported)
                {
                    return (true, service.Name);
                }
            }
            catch (BrokenCircuitException)
            {
                // Circuit breaker is open - service is known to be down, no need for verbose logging
                _logger.LogDebug("Circuit breaker open for {ServiceName} at {ServiceUrl}, skipping check",
                    service.Name, service.Url);
                continue;
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException)
            {
                // Connection refused or network error - log concisely without full stack trace
                _logger.LogWarning("Cannot connect to {ServiceName} at {ServiceUrl}: {ErrorMessage}",
                    service.Name, service.Url, ex.InnerException.Message);
                continue;
            }
            catch (Exception ex)
            {
                // Unexpected error - log with full details
                _logger.LogWarning(ex, "Failed to check import status from {ServiceName} at {ServiceUrl}",
                    service.Name, service.Url);
                continue;
            }
        }

        return (false, null);
    }
}
