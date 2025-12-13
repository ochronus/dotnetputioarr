using Csharparr.Configuration;
using Csharparr.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Csharparr.Http;

[ApiController]
[Route("/health")]
public class HealthController : ControllerBase
{
    private readonly IPutioClient _putioClient;
    private readonly AppConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthController> _logger;

    public HealthController(IPutioClient putioClient, AppConfig config, IHttpClientFactory httpClientFactory, ILogger<HealthController> logger)
    {
        _putioClient = putioClient;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> HealthCheck()
    {
        _logger.LogInformation("Performing health check");

        // Check Put.io API connectivity
        try
        {
            await _putioClient.GetAccountInfoAsync();
            _logger.LogInformation("Put.io API connectivity is OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Put.io API connectivity is down");
            return StatusCode(503, "Put.io API connectivity is down");
        }

        // Check download directory writability
        try
        {
            var testFilePath = Path.Combine(_config.DownloadDirectory, ".healthcheck");
            await System.IO.File.WriteAllTextAsync(testFilePath, "health check");
            System.IO.File.Delete(testFilePath);
            _logger.LogInformation("Download directory is writable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download directory is not writable");
            return StatusCode(503, "Download directory is not writable");
        }

        // Check Arr service connectivity
        foreach (var service in _config.GetArrServices())
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("ArrClient");
                httpClient.BaseAddress = new Uri(service.Url);
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", service.ApiKey);
                var response = await httpClient.GetAsync("api/v3/system/status");
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("{ServiceName} connectivity is OK", service.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName} connectivity is down", service.Name);
                return StatusCode(503, $"{service.Name} connectivity is down");
            }
        }

        return Ok("OK");
    }
}
