using System.CommandLine;
using System.CommandLine.Parsing;
using Csharparr.Commands;
using Csharparr.Configuration;
using Csharparr.Download;
using Csharparr.Services;
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
    builder.Services.AddSingleton<PutioClient>(sp => new PutioClient(config.Putio.ApiKey));
    builder.Services.AddHttpClient();
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
