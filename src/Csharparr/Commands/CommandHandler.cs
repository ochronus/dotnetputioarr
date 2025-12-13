using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Csharparr.Configuration;
using Csharparr.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Csharparr.Commands;

/// <summary>
/// Handles CLI command setup and execution
/// </summary>
public static class CommandHandler
{
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Creates the root command with all subcommands
    /// </summary>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("put.io to sonarr/radarr/whisparr proxy - C# port");

        // Config path option (shared across commands)
        var configOption = new Option<string>("-c", "--config")
        {
            Description = "Path to config file",
            DefaultValueFactory = _ => AppConfig.DefaultConfigPath
        };

        // Run command
        var runCommand = new Command("run", "Run the proxy");
        runCommand.Add(configOption);
        runCommand.SetAction(parseResult =>
        {
            var configPath = parseResult.GetValue(configOption) ?? AppConfig.DefaultConfigPath;
            RunProxy(configPath);
        });

        // Get-token command
        var getTokenCommand = new Command("get-token", "Generate a put.io API token");
        getTokenCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            await GetTokenAsync(cancellationToken);
        });

        // Generate-config command
        var generateConfigCommand = new Command("generate-config", "Generate a config file");
        var generateConfigOption = new Option<string>("-c", "--config")
        {
            Description = "Path to config file",
            DefaultValueFactory = _ => AppConfig.DefaultConfigPath
        };
        generateConfigCommand.Add(generateConfigOption);
        generateConfigCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var configPath = parseResult.GetValue(generateConfigOption) ?? AppConfig.DefaultConfigPath;
            await GenerateConfigAsync(configPath, cancellationToken);
        });

        // Version command
        var versionCommand = new Command("version", "Print the version number");
        versionCommand.SetAction((parseResult) =>
        {
            Console.WriteLine($"csharparr version {Version}");
        });

        rootCommand.Add(runCommand);
        rootCommand.Add(getTokenCommand);
        rootCommand.Add(generateConfigCommand);
        rootCommand.Add(versionCommand);

        return rootCommand;
    }

    /// <summary>
    /// Runs the proxy server
    /// </summary>
    private static void RunProxy(string configPath)
    {
        // This is handled by Program.cs to set up the full ASP.NET Core pipeline
        // We store the config path for later use
        Environment.SetEnvironmentVariable("CSHARPARR_CONFIG_PATH", configPath);

        // The actual server startup is in Program.cs
        // This handler just validates and prepares
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Configuration file not found: {configPath}");
            Console.Error.WriteLine("Run 'csharparr generate-config' to create one.");
            Environment.Exit(1);
        }

        try
        {
            var config = AppConfig.Load(configPath);
            config.Validate();

            Console.WriteLine($"Configuration validated successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Gets a new Put.io API token via OOB authentication
    /// </summary>
    public static async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine();

        // Since this runs outside the DI container, we create the client manually.
        using var httpClient = new HttpClient();
        var logger = NullLogger<PutioClient>.Instance;
        var client = new PutioClient(httpClient, logger);

        // Get OOB code
        string oobCode;
        try
        {
            oobCode = await client.GetOobCodeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to get OOB code: {ex.Message}");
            throw;
        }

        Console.WriteLine($"Go to https://put.io/link and enter the code: {oobCode}");
        Console.WriteLine("Waiting for token...");

        // Poll for token every 3 seconds
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            try
            {
                var token = await client.CheckOobAsync(oobCode, cancellationToken);
                Console.WriteLine($"Put.io API token: {token}");
                return token;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Not linked yet, continue waiting
            }
        }
    }

    /// <summary>
    /// Generates a configuration file
    /// </summary>
    public static async Task GenerateConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Generating config {configPath}");

        // Get Put.io token
        var putioApiKey = await GetTokenAsync(cancellationToken);

        // Config template
        var configTemplate = $"""
            # Required. Username and password that sonarr/radarr use to connect to the proxy
            username = "myusername"
            password = "mypassword"

            # Required. Directory where the proxy will download files to. This directory has to be readable by
            # sonarr/radarr in order to import downloads
            download_directory = "/path/to/downloads"

            # Optional bind address, default "0.0.0.0"
            bind_address = "0.0.0.0"

            # Optional TCP port, default 9091
            port = 9091

            # Optional log level, default "info"
            loglevel = "info"

            # Optional UID, default 1000. Change the owner of the downloaded files to this UID. Requires root.
            uid = 1000

            # Optional polling interval in secs, default 10.
            polling_interval = 10

            # Optional skip directories when downloding, default ["sample", "extras"]
            skip_directories = ["sample", "extras"]

            # Optional number of orchestration workers, default 10. Unless there are many changes coming from
            # put.io, you shouldn't have to touch this number. 10 is already overkill.
            orchestration_workers = 10

            # Optional number of download workers, default 4. This controls how many downloads we run in parallel.
            download_workers = 4

            [putio]
            # Required. Putio API key. You can generate one using 'csharparr get-token'
            api_key = "{putioApiKey}"

            # Both [sonarr] and [radarr] are optional, but you'll need at least one of them
            [sonarr]
            url = "http://mysonarrhost:8989/sonarr"
            # Can be found in Settings -> General
            api_key = "MYSONARRAPIKEY"

            [radarr]
            url = "http://myradarrhost:7878/radarr"
            # Can be found in Settings -> General
            api_key = "MYRADARRAPIKEY"

            [whisparr]
            url = "http://mywhisparrhost:6969/whisparr"
            # Can be found in Settings -> General
            api_key = "MYWHISPARRAPIKEY"
            """;

        // Check if config file already exists and back it up
        if (File.Exists(configPath))
        {
            var backupPath = configPath + ".bak";
            Console.WriteLine($"Backing up config {configPath}");
            File.Move(configPath, backupPath, overwrite: true);
        }

        // Create parent directory if it doesn't exist
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write config file
        Console.WriteLine($"Writing {configPath}");
        await File.WriteAllTextAsync(configPath, configTemplate);

        Console.WriteLine("Configuration file generated successfully.");
        Console.WriteLine("Please edit the file to configure username, password, download directory, and arr services.");
    }
}
