using Csharparr.Configuration;
using FluentAssertions;

namespace Csharparr.Tests.Configuration;

public class AppConfigTests
{
    [Fact]
    public void DefaultValues_ShouldBeSetCorrectly()
    {
        var config = new AppConfig();

        config.BindAddress.Should().Be("0.0.0.0");
        config.DownloadWorkers.Should().Be(4);
        config.OrchestrationWorkers.Should().Be(10);
        config.Loglevel.Should().Be("info");
        config.PollingInterval.Should().Be(10);
        config.Port.Should().Be(9091);
        config.Uid.Should().Be(1000);
        config.SkipDirectories.Should().BeEquivalentTo(["sample", "extras"]);
    }

    [Fact]
    public void Load_WithValidToml_ShouldParseCorrectly()
    {
        var configPath = Path.GetTempFileName();
        try
        {
            var tomlContent = """
                username = "testuser"
                password = "testpass"
                download_directory = "/downloads"
                bind_address = "127.0.0.1"
                port = 8080
                loglevel = "debug"
                uid = 500
                polling_interval = 5
                skip_directories = ["sample"]
                orchestration_workers = 5
                download_workers = 2

                [putio]
                api_key = "test-api-key"

                [sonarr]
                url = "http://localhost:8989"
                api_key = "sonarr-key"

                [radarr]
                url = "http://localhost:7878"
                api_key = "radarr-key"
                """;

            File.WriteAllText(configPath, tomlContent);

            var config = AppConfig.Load(configPath);

            config.Username.Should().Be("testuser");
            config.Password.Should().Be("testpass");
            config.DownloadDirectory.Should().Be("/downloads");
            config.BindAddress.Should().Be("127.0.0.1");
            config.Port.Should().Be(8080);
            config.Loglevel.Should().Be("debug");
            config.Uid.Should().Be(500);
            config.PollingInterval.Should().Be(5);
            config.OrchestrationWorkers.Should().Be(5);
            config.DownloadWorkers.Should().Be(2);
            config.Putio.ApiKey.Should().Be("test-api-key");
            config.Sonarr.Should().NotBeNull();
            config.Sonarr!.Url.Should().Be("http://localhost:8989");
            config.Sonarr.ApiKey.Should().Be("sonarr-key");
            config.Radarr.Should().NotBeNull();
            config.Radarr!.Url.Should().Be("http://localhost:7878");
            config.Radarr.ApiKey.Should().Be("radarr-key");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Load_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        var action = () => AppConfig.Load("/nonexistent/path/config.toml");

        action.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Validate_WithValidConfig_ShouldNotThrow()
    {
        var config = new AppConfig
        {
            Username = "user",
            Password = "pass",
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig { ApiKey = "key" },
            Sonarr = new ArrConfig { Url = "http://localhost", ApiKey = "key" }
        };

        var action = () => config.Validate();

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithMissingUsername_ShouldThrow()
    {
        var config = new AppConfig
        {
            Password = "pass",
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig { ApiKey = "key" },
            Sonarr = new ArrConfig { Url = "http://localhost", ApiKey = "key" }
        };

        var action = () => config.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*username is required*");
    }

    [Fact]
    public void Validate_WithMissingPassword_ShouldThrow()
    {
        var config = new AppConfig
        {
            Username = "user",
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig { ApiKey = "key" },
            Sonarr = new ArrConfig { Url = "http://localhost", ApiKey = "key" }
        };

        var action = () => config.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*password is required*");
    }

    [Fact]
    public void Validate_WithMissingDownloadDirectory_ShouldThrow()
    {
        var config = new AppConfig
        {
            Username = "user",
            Password = "pass",
            Putio = new PutioConfig { ApiKey = "key" },
            Sonarr = new ArrConfig { Url = "http://localhost", ApiKey = "key" }
        };

        var action = () => config.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*download_directory is required*");
    }

    [Fact]
    public void Validate_WithMissingPutioApiKey_ShouldThrow()
    {
        var config = new AppConfig
        {
            Username = "user",
            Password = "pass",
            DownloadDirectory = "/downloads",
            Sonarr = new ArrConfig { Url = "http://localhost", ApiKey = "key" }
        };

        var action = () => config.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*putio.api_key is required*");
    }

    [Fact]
    public void Validate_WithNoArrConfigured_ShouldThrow()
    {
        var config = new AppConfig
        {
            Username = "user",
            Password = "pass",
            DownloadDirectory = "/downloads",
            Putio = new PutioConfig { ApiKey = "key" }
        };

        var action = () => config.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one of sonarr, radarr, or whisparr must be configured*");
    }

    [Fact]
    public void GetArrServices_WithNoServices_ShouldReturnEmpty()
    {
        var config = new AppConfig();

        var services = config.GetArrServices().ToList();

        services.Should().BeEmpty();
    }

    [Fact]
    public void GetArrServices_WithOnlySonarr_ShouldReturnOne()
    {
        var config = new AppConfig
        {
            Sonarr = new ArrConfig { Url = "http://sonarr", ApiKey = "key1" }
        };

        var services = config.GetArrServices().ToList();

        services.Should().HaveCount(1);
        services[0].Name.Should().Be("Sonarr");
        services[0].Url.Should().Be("http://sonarr");
        services[0].ApiKey.Should().Be("key1");
    }

    [Fact]
    public void GetArrServices_WithAllServices_ShouldReturnThree()
    {
        var config = new AppConfig
        {
            Sonarr = new ArrConfig { Url = "http://sonarr:8989", ApiKey = "sonarr-key" },
            Radarr = new ArrConfig { Url = "http://radarr:7878", ApiKey = "radarr-key" },
            Whisparr = new ArrConfig { Url = "http://whisparr:6969", ApiKey = "whisparr-key" }
        };

        var services = config.GetArrServices().ToList();

        services.Should().HaveCount(3);
        services.Should().Contain(s => s.Name == "Sonarr");
        services.Should().Contain(s => s.Name == "Radarr");
        services.Should().Contain(s => s.Name == "Whisparr");
    }

    [Fact]
    public void DefaultConfigPath_ShouldNotBeEmpty()
    {
        var path = AppConfig.DefaultConfigPath;

        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith("config.toml");
        path.Should().Contain("putioarr");
    }

    [Fact]
    public void Load_WithPartialConfig_ShouldUseDefaults()
    {
        var configPath = Path.GetTempFileName();
        try
        {
            var tomlContent = """
                username = "user"
                password = "pass"
                download_directory = "/downloads"

                [putio]
                api_key = "key"

                [sonarr]
                url = "http://localhost"
                api_key = "key"
                """;

            File.WriteAllText(configPath, tomlContent);

            var config = AppConfig.Load(configPath);

            // Explicitly set values
            config.Username.Should().Be("user");

            // Default values should be preserved
            config.BindAddress.Should().Be("0.0.0.0");
            config.Port.Should().Be(9091);
            config.DownloadWorkers.Should().Be(4);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void PutioConfig_DefaultApiKey_ShouldBeEmpty()
    {
        var putioConfig = new PutioConfig();

        putioConfig.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public void ArrConfig_DefaultValues_ShouldBeEmpty()
    {
        var arrConfig = new ArrConfig();

        arrConfig.Url.Should().BeEmpty();
        arrConfig.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public void ArrServiceInfo_ShouldStoreValues()
    {
        var info = new ArrServiceInfo("TestService", "http://test", "test-key");

        info.Name.Should().Be("TestService");
        info.Url.Should().Be("http://test");
        info.ApiKey.Should().Be("test-key");
    }
}
