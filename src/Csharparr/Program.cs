using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using Csharparr.Commands;
using Csharparr.Configuration;
using Csharparr.Download;
using Csharparr.Services;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Serilog;
using Serilog.Events;

// Handle CLI commands first
if (args.Length > 0 && args[0] != "run")
{
    var rootCommand = CommandHandler.CreateRootCommand();
    var parseResult = rootCommand.Parse(args);
    await parseResult.InvokeAsync();
    return;
}

// Parse config path from args
var configPath = AppConfig.DefaultConfigPath;
for (var i = 0; i < args.Length; i++)
{
    if ((args[i] == "-c" || args[i] == "--config") && i + 1 < args.Length)
    {
        configPath = args[i + 1];
        break;
    }
}

// Skip "run" command if present
var webArgs = args.Length > 0 && args[0] == "run"
    ? args.Skip(1).ToArray()
    : args;

// Load configuration
AppConfig config;
try
{
    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"Configuration file not found: {configPath}");
        Console.Error.WriteLine("Run 'csharparr generate-config' to create one.");
        return;
    }

    config = AppConfig.Load(configPath);
    config.Validate();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load configuration: {ex.Message}");
    return;
}

// Configure Serilog
var logLevel = config.Loglevel.ToLowerInvariant() switch
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
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Debug)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting csharparr, version {Version}", CommandHandler.Version);

    // Verify put.io API key
    using (var client = new PutioClient(config.Putio.ApiKey))
    {
        try
        {
            await client.GetAccountInfoAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to verify put.io API key");
            return;
        }
    }

    // Build the web application
    var builder = WebApplication.CreateBuilder(webArgs);

    // Configure Serilog
    builder.Host.UseSerilog();

    // Add services
    builder.Services.AddSingleton(config);

    // Configure HttpClient for PutioClient with resilience policies
    builder.Services.AddHttpClient(PutioClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddResilienceHandler("putio-resilience", builder =>
        {
            // Retry policy: retry on transient errors with exponential backoff
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });

            // Circuit breaker: prevent hammering a failing service
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });

            // Timeout per attempt
            builder.AddTimeout(TimeSpan.FromSeconds(10));
        });

    // Configure HttpClient for ArrClient with resilience policies
    builder.Services.AddHttpClient(ArrClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        })
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
    builder.Services.AddHttpClient("Downloads", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30);
        })
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

    // Register PutioClient with HttpClientFactory
    builder.Services.AddSingleton<PutioClient>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(PutioClient.HttpClientName);
        var logger = sp.GetRequiredService<ILogger<PutioClient>>();
        return new PutioClient(config.Putio.ApiKey, httpClient, logger);
    });

    builder.Services.AddHostedService<DownloadManager>();

    // Add controllers
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = null; // Preserve property names as-is
        });

    // Configure Kestrel
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(config.Port);
    });

    var app = builder.Build();

    // Configure middleware
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, elapsed, ex) =>
            ex != null || httpContext.Response.StatusCode >= 400
                ? Serilog.Events.LogEventLevel.Information
                : Serilog.Events.LogEventLevel.Debug;
    });

    app.MapControllers();

    Log.Information("Starting web server at http://{BindAddress}:{Port}", config.BindAddress, config.Port);

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Determines if a request should be retried based on the outcome
/// </summary>
static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
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
