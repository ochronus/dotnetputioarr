using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Csharparr.Configuration;

/// <summary>
/// Main application configuration
/// </summary>
public sealed class AppConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public string DownloadDirectory { get; set; } = string.Empty;
    public int DownloadWorkers { get; set; } = 4;
    public string Loglevel { get; set; } = "info";
    public int OrchestrationWorkers { get; set; } = 10;
    public string Password { get; set; } = string.Empty;
    public int PollingInterval { get; set; } = 10;
    public int Port { get; set; } = 9091;
    public List<string> SkipDirectories { get; set; } = ["sample", "extras"];
    public int Uid { get; set; } = 1000;
    public string Username { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public PutioConfig Putio { get; set; } = new();
    public ArrConfig? Sonarr { get; set; }
    public ArrConfig? Radarr { get; set; }
    public ArrConfig? Whisparr { get; set; }

    /// <summary>
    /// Gets the default configuration file path
    /// </summary>
    public static string DefaultConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "putioarr",
            "config.toml"
        );

    /// <summary>
    /// Loads configuration from a TOML file
    /// </summary>
    public static AppConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }

        var tomlContent = File.ReadAllText(configPath);
        var tomlTable = Toml.ToModel(tomlContent);

        var config = new AppConfig();

        // Parse top-level fields
        if (tomlTable.TryGetValue("bind_address", out var bindAddress))
            config.BindAddress = bindAddress?.ToString() ?? config.BindAddress;

        if (tomlTable.TryGetValue("download_directory", out var downloadDir))
            config.DownloadDirectory = downloadDir?.ToString() ?? config.DownloadDirectory;

        if (tomlTable.TryGetValue("download_workers", out var downloadWorkers))
            config.DownloadWorkers = Convert.ToInt32(downloadWorkers);

        if (tomlTable.TryGetValue("loglevel", out var loglevel))
            config.Loglevel = loglevel?.ToString() ?? config.Loglevel;

        if (tomlTable.TryGetValue("orchestration_workers", out var orchWorkers))
            config.OrchestrationWorkers = Convert.ToInt32(orchWorkers);

        if (tomlTable.TryGetValue("password", out var password))
            config.Password = password?.ToString() ?? config.Password;

        if (tomlTable.TryGetValue("polling_interval", out var pollingInterval))
            config.PollingInterval = Convert.ToInt32(pollingInterval);

        if (tomlTable.TryGetValue("port", out var port))
            config.Port = Convert.ToInt32(port);

        if (tomlTable.TryGetValue("uid", out var uid))
            config.Uid = Convert.ToInt32(uid);

        if (tomlTable.TryGetValue("username", out var username))
            config.Username = username?.ToString() ?? config.Username;

        if (tomlTable.TryGetValue("instance_name", out var instanceName))
            config.InstanceName = instanceName?.ToString() ?? config.InstanceName;

        if (tomlTable.TryGetValue("skip_directories", out var skipDirs) && skipDirs is TomlArray skipArray)
        {
            config.SkipDirectories = skipArray.Select(s => s?.ToString() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        // Parse putio section
        if (tomlTable.TryGetValue("putio", out var putioSection) && putioSection is TomlTable putioTable)
        {
            config.Putio = ParsePutioConfig(putioTable);
        }

        // Parse sonarr section
        if (tomlTable.TryGetValue("sonarr", out var sonarrSection) && sonarrSection is TomlTable sonarrTable)
        {
            config.Sonarr = ParseArrConfig(sonarrTable);
        }

        // Parse radarr section
        if (tomlTable.TryGetValue("radarr", out var radarrSection) && radarrSection is TomlTable radarrTable)
        {
            config.Radarr = ParseArrConfig(radarrTable);
        }

        // Parse whisparr section
        if (tomlTable.TryGetValue("whisparr", out var whisparrSection) && whisparrSection is TomlTable whisparrTable)
        {
            config.Whisparr = ParseArrConfig(whisparrTable);
        }

        return config;
    }

    private static PutioConfig ParsePutioConfig(TomlTable table)
    {
        var apiKey = table.TryGetValue("api_key", out var key)
            ? key?.ToString() ?? ""
            : "";
        return new PutioConfig(apiKey);
    }

    private static ArrConfig ParseArrConfig(TomlTable table)
    {
        var url = table.TryGetValue("url", out var u)
            ? u?.ToString() ?? ""
            : "";
        var apiKey = table.TryGetValue("api_key", out var key)
            ? key?.ToString() ?? ""
            : "";
        return new ArrConfig(url, apiKey);
    }

    /// <summary>
    /// Validates the configuration
    /// </summary>
    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Username))
            errors.Add("username is required");

        if (string.IsNullOrWhiteSpace(InstanceName))
            errors.Add("instance_name is required");
        else if (!Regex.IsMatch(InstanceName, "^[A-Za-z0-9]{3,10}$"))
            errors.Add("instance_name must be alphanumeric and 3-10 characters long");

        if (string.IsNullOrWhiteSpace(Password))
            errors.Add("password is required");

        if (string.IsNullOrWhiteSpace(DownloadDirectory))
            errors.Add("download_directory is required");

        if (string.IsNullOrWhiteSpace(Putio.ApiKey))
            errors.Add("putio.api_key is required");

        if (Sonarr is null && Radarr is null && Whisparr is null)
            errors.Add("at least one of sonarr, radarr, or whisparr must be configured");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration validation failed: {string.Join("; ", errors)}"
            );
        }
    }

    /// <summary>
    /// Gets all configured arr services
    /// </summary>
    public IEnumerable<ArrServiceInfo> GetArrServices()
    {
        if (Sonarr is not null)
            yield return new ArrServiceInfo("Sonarr", Sonarr.Url, Sonarr.ApiKey);

        if (Radarr is not null)
            yield return new ArrServiceInfo("Radarr", Radarr.Url, Radarr.ApiKey);

        if (Whisparr is not null)
            yield return new ArrServiceInfo("Whisparr", Whisparr.Url, Whisparr.ApiKey);
    }
}

/// <summary>
/// Put.io API configuration
/// </summary>
public sealed record PutioConfig(string ApiKey = "");

/// <summary>
/// Arr service (Sonarr/Radarr/Whisparr) configuration
/// </summary>
public sealed record ArrConfig(string Url = "", string ApiKey = "");

/// <summary>
/// Represents an arr service configuration with its name
/// </summary>
public readonly record struct ArrServiceInfo(string Name, string Url, string ApiKey);
