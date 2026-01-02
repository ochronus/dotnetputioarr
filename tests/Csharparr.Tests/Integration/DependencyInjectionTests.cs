using Csharparr.Configuration;
using Csharparr.Http;
using Csharparr.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http.Headers;

namespace Csharparr.Tests.Integration;

/// <summary>
/// Integration tests to verify dependency injection configuration
/// </summary>
public class DependencyInjectionTests
{
    private readonly AppConfig _testConfig = new()
    {
        Username = "testuser",
        Password = "testpass",
        DownloadDirectory = "/downloads",
        Putio = new PutioConfig("test-api-key"),
        Sonarr = new ArrConfig("http://sonarr", "key"),
        InstanceName = "testinst",
        InstanceFolderId = 1,
        Port = 9091,
        BindAddress = "0.0.0.0",
        Loglevel = "info"
    };

    [Fact]
    public void ServiceProvider_ShouldResolve_TransmissionController()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var action = () => serviceProvider.GetRequiredService<TransmissionController>();
        action.Should().NotThrow("TransmissionController should be resolvable from DI container");
    }

    [Fact]
    public void ServiceProvider_ShouldResolve_HealthController()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var action = () => serviceProvider.GetRequiredService<HealthController>();
        action.Should().NotThrow("HealthController should be resolvable from DI container");
    }

    [Fact]
    public void ServiceProvider_ShouldResolve_IPutioClient()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var action = () => serviceProvider.GetRequiredService<IPutioClient>();
        action.Should().NotThrow("IPutioClient should be resolvable from DI container");
    }

    [Fact]
    public void ServiceProvider_ShouldResolve_IArrClientFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var action = () => serviceProvider.GetRequiredService<IArrClientFactory>();
        action.Should().NotThrow("IArrClientFactory should be resolvable from DI container");
    }

    [Fact]
    public void TransmissionController_ShouldInject_IPutioClient_NotConcreteType()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var controller = serviceProvider.GetRequiredService<TransmissionController>();

        // Assert
        controller.Should().NotBeNull("TransmissionController should be created successfully");
        // The fact that we can create it means it's using IPutioClient, not PutioClient
    }

    [Fact]
    public void HealthController_ShouldInject_IPutioClient()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var controller = serviceProvider.GetRequiredService<HealthController>();

        // Assert
        controller.Should().NotBeNull("HealthController should be created successfully");
    }

    [Fact]
    public void ServiceProvider_WithMultipleScopes_ShouldResolve_Controllers()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Create multiple scopes to simulate concurrent requests
        using (var scope1 = serviceProvider.CreateScope())
        {
            var action1 = () => scope1.ServiceProvider.GetRequiredService<TransmissionController>();
            action1.Should().NotThrow("TransmissionController should resolve in scope 1");
        }

        using (var scope2 = serviceProvider.CreateScope())
        {
            var action2 = () => scope2.ServiceProvider.GetRequiredService<TransmissionController>();
            action2.Should().NotThrow("TransmissionController should resolve in scope 2");
        }

        using (var scope3 = serviceProvider.CreateScope())
        {
            var action3 = () => scope3.ServiceProvider.GetRequiredService<HealthController>();
            action3.Should().NotThrow("HealthController should resolve in scope 3");
        }
    }

    [Fact]
    public void ServiceProvider_ConcurrentResolution_ShouldNotFail()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Simulate concurrent request handling
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            using var scope = serviceProvider.CreateScope();
            var transmissionController = scope.ServiceProvider.GetRequiredService<TransmissionController>();
            var healthController = scope.ServiceProvider.GetRequiredService<HealthController>();

            transmissionController.Should().NotBeNull();
            healthController.Should().NotBeNull();
        })).ToArray();

        var action = () => Task.WaitAll(tasks);
        action.Should().NotThrow("Concurrent DI resolution should not fail");
    }

    /// <summary>
    /// Configure services similar to Program.cs to simulate production DI setup
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        // Add configuration
        services.AddSingleton(_testConfig);

        // Add logging
        services.AddLogging(builder => builder.AddConsole());

        // Configure typed HttpClient for PutioClient (matching Program.cs)
        services.AddHttpClient<IPutioClient, PutioClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _testConfig.Putio.ApiKey);
        });

        // Configure named HttpClient for ArrClient
        services.AddHttpClient("ArrClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // Configure named HttpClient for Downloads
        services.AddHttpClient("Downloads", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30);
        });

        // Add other services
        services.AddSingleton<IArrClientFactory, ArrClientFactory>();

        // Add controllers as transient (they're created per request)
        services.AddTransient<TransmissionController>();
        services.AddTransient<HealthController>();
    }
}
