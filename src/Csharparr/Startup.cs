using Csharparr.Commands;
using Csharparr.Configuration;
using Csharparr.Download;
using Csharparr.Services;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Serilog;
using Serilog.Events;
using System.Net.Http.Headers;
using System.Net;

namespace Csharparr;

public class Startup
{
    public IConfiguration Configuration { get; }
    private readonly AppConfig _config;

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
        _config = new AppConfig(); // Default, will be overwritten by DI if configured
        Configuration.Bind(_config); // Attempt to bind configuration to AppConfig
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Add AppConfig explicitly after binding from IConfiguration
        services.AddSingleton(_config);

        // Configure Serilog
        var logLevel = _config.Loglevel.ToLowerInvariant() switch
        {
            "trace" or "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "info" or "information" => LogEventLevel.Information,
            "warn" or "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" or "critical" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));


        // Configure typed HttpClient for PutioClient with resilience policies
        services.AddHttpClient<IPutioClient, PutioClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.Putio.ApiKey);
        })
        .RemoveAllLoggers()
        .AddResilienceHandler("putio-resilience", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });
            builder.AddTimeout(TimeSpan.FromSeconds(10));
        });

        // Configure named HttpClient for ArrClient with resilience policies
        services.AddHttpClient("ArrClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .RemoveAllLoggers()
        .AddResilienceHandler("arr-resilience", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(60),
                FailureRatio = 0.5,
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });
            builder.AddTimeout(TimeSpan.FromSeconds(30));
        });

        // Default HttpClient for downloads (longer timeout, simpler retry)
        services.AddHttpClient("Downloads", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30);
        })
        .RemoveAllLoggers()
        .AddResilienceHandler("download-resilience", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(5),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });
        });

        services.AddSingleton<IArrClientFactory, ArrClientFactory>();
        services.AddHostedService<DownloadManager>();

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null; // Preserve property names as-is
            });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = (httpContext, elapsed, ex) =>
                ex != null || httpContext.Response.StatusCode >= 400
                    ? Serilog.Events.LogEventLevel.Information
                    : Serilog.Events.LogEventLevel.Debug;
        });

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        // Retry on exceptions (network errors, timeouts, etc.)
        if (outcome.Exception is not null)
        {
            return outcome.Exception is HttpRequestException
                or TimeoutException
                or TaskCanceledException;
        }

        // Retry on transient HTTP status codes
        if (outcome.Result is not null)
        {
            var statusCode = outcome.Result.StatusCode;
            return statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
        }

        return false;
    }
}
