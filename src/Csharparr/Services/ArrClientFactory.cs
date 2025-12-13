using Csharparr.Configuration;
using Microsoft.Extensions.Logging;

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check import status from {ServiceName} at {ServiceUrl}",
                    service.Name, service.Url);
                continue;
            }
        }

        return (false, null);
    }
}
