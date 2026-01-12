# AI Agent Instructions for csharparr

This document provides guidelines for AI agents working on this .NET 10/C# 14 codebase. Follow these conventions to maintain consistency and code quality.

## Project Overview

**csharparr** is a Transmission RPC-compatible proxy that bridges put.io with *arr services (Sonarr/Radarr/Whisparr). It acts as a background service that monitors transfers, downloads files, and manages imports.

**Tech Stack:**
- .NET 10.0 / C# 14
- ASP.NET Core for HTTP server
- Serilog for structured logging
- Microsoft.Extensions.Http.Resilience + Polly for HTTP resilience
- System.CommandLine for CLI
- Tomlyn for TOML configuration parsing
- xUnit + Moq + FluentAssertions for testing

## Code Style and Structure

### General Principles
- Write concise, idiomatic C# code following .NET conventions
- Prefer simplicity over cleverness; keep solutions focused and minimal
- Avoid over-engineering—don't add features, abstractions, or "improvements" beyond what's requested
- Use object-oriented patterns with functional elements (LINQ, pattern matching) where appropriate

### File Organization
```
src/Csharparr/
├── Commands/        # CLI command handlers
├── Configuration/   # Config parsing (TOML) and validation
├── Download/        # Download orchestration and worker management
├── Http/            # HTTP controllers (Transmission RPC protocol)
├── Services/        # API clients (Put.io, Arr services)
├── Program.cs       # Entry point with DI configuration
tests/Csharparr.Tests/
├── Configuration/   # Config tests
├── Download/        # Download manager tests
├── Http/            # Controller tests
├── Services/        # Client tests
├── Integration/     # DI integration tests
```

### Naming Conventions
- **PascalCase**: Class names, method names, public members, properties, enums
- **camelCase**: Local variables, method parameters
- **_camelCase**: Private fields (prefix with underscore)
- **UPPER_CASE**: Constants (use sparingly; prefer `const` or `static readonly`)
- **I prefix**: Interface names (e.g., `IPutioClient`, `IArrClientFactory`)

### C# 14/.NET 10 Features to Use
- **File-scoped namespaces**: `namespace Csharparr.Services;`
- **Record types**: For DTOs and immutable data (e.g., `record PutioTransfer(...)`)
- **Pattern matching**: In switch expressions and type checks
- **Nullable reference types**: Always enabled; handle nullability explicitly
- **Top-level statements**: Used in Program.cs
- **Collection expressions**: Use `[]` for empty collections
- **Lock keyword**: Use `Lock` type for thread synchronization (not `object`)
- **Primary constructors**: For simple classes/records when appropriate
- **Raw string literals**: For multi-line strings or strings with special chars

```csharp
// Good: Modern C# patterns used in this project
public sealed class PutioClient : IPutioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PutioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Constructor injection
    public PutioClient(HttpClient httpClient, ILogger<PutioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }
}
```

### Syntax and Formatting
- Use `var` when the type is obvious from the right-hand side
- Use explicit types when they improve clarity
- Use expression-bodied members for simple one-liners
- Use null-conditional (`?.`) and null-coalescing (`??`, `??=`) operators
- Use string interpolation over concatenation
- Keep methods focused and under ~50 lines when possible

## Asynchronous Programming

### Async/Await Patterns
- All I/O-bound operations must be async
- Always pass `CancellationToken` through the call chain
- Use `cancellationToken = default` for optional parameters
- Suffix async methods with `Async`

```csharp
// Good: Proper async pattern with cancellation
public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
{
    _logger.LogDebug("Getting account info from put.io");
    var response = await _httpClient.GetAsync($"{BaseUrl}/account/info", cancellationToken);
    // ...
}
```

### Cancellation Handling
- Catch `OperationCanceledException` separately from other exceptions
- Log cancellation at Debug level (expected during shutdown), not Error

```csharp
// Good: Proper cancellation handling
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    _logger.LogDebug("{Transfer}: operation cancelled", transfer);
}
catch (Exception ex)
{
    _logger.LogError(ex, "{Transfer}: operation failed unexpectedly", transfer);
}
```

### Background Services
- Inherit from `BackgroundService` for long-running tasks
- Use `Channel<T>` for producer-consumer patterns with bounded queues
- Track background tasks to prevent resource leaks
- Clean up completed tasks periodically

```csharp
// Good: Channel-based work distribution
private readonly Channel<TransferMessage> _transferChannel;

public DownloadManager(...)
{
    _transferChannel = Channel.CreateBounded<TransferMessage>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
}

// Consumer pattern
await foreach (var message in _transferChannel.Reader.ReadAllAsync(cancellationToken))
{
    // Process message
}
```

## HTTP Client Management

### Typed and Named Clients
- Use **typed clients** for service-specific APIs (`AddHttpClient<IPutioClient, PutioClient>`)
- Use **named clients** for shared configurations (`AddHttpClient("ArrClient")`)
- Configure timeouts per client based on operation type
- Use `IHttpClientFactory` for creating clients in background services

### Resilience with Polly
- Use `Microsoft.Extensions.Http.Resilience` (not deprecated `Polly.Extensions.Http`)
- Configure retry with exponential backoff and jitter
- Add circuit breaker for cascading failure protection
- Set appropriate timeouts per operation type

```csharp
// Good: Resilience configuration
builder.Services.AddHttpClient<IPutioClient, PutioClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .RemoveAllLoggers()  // Suppress verbose framework logs
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
            BreakDuration = TimeSpan.FromSeconds(30)
        });
    });
```

### Retry Conditions
Retry on:
- Network errors (`HttpRequestException`)
- Timeouts (`TimeoutException`, `TaskCanceledException`)
- Transient HTTP status codes: 408, 429, 500, 502, 503, 504

## Dependency Injection

### Registration Patterns
- Register configuration as singleton: `services.AddSingleton(config)`
- Use interfaces for all services to enable testing
- Register typed HTTP clients with their interfaces
- Use factories for dynamic client creation

```csharp
// Good: DI registration
builder.Services.AddSingleton(config);
builder.Services.AddHttpClient<IPutioClient, PutioClient>(...);
builder.Services.AddSingleton<IArrClientFactory, ArrClientFactory>();
builder.Services.AddHostedService<DownloadManager>();
```

### Critical: Avoid DI Misconfiguration
- Always register services with their interfaces, not concrete types
- Write integration tests to verify DI resolution (see `tests/Integration/`)
- Test concurrent resolution to catch thread-safety issues

## Error Handling

### Exception Strategy
- Use exceptions for exceptional cases, not control flow
- Create domain-specific exceptions (`PutioException`, `ArrClientException`)
- Include response body in HTTP error messages for debugging (truncated to 500 chars)

```csharp
// Good: Error body capture
private static async Task<string> TryReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
{
    try
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            if (body.Length > 500)
                body = body[..500] + "...";
            return $" - {body}";
        }
    }
    catch { /* Ignore errors reading the body */ }
    return string.Empty;
}
```

### Circuit Breaker Handling
- Log `BrokenCircuitException` at Debug level (expected when circuit is open)
- Log socket errors as Warning without full stack trace
- Continue processing other operations when one service fails

## Logging

### Serilog Structured Logging
- Use structured logging with message templates, not string interpolation
- Use appropriate log levels consistently:
  - `Debug`: Detailed operational info (method entries, HTTP requests)
  - `Information`: Important events (startup, transfers, completions)
  - `Warning`: Recovered errors, expected failures (circuit breaker, service down)
  - `Error`: Failures requiring attention
  - `Fatal`: Application-terminating errors

```csharp
// Good: Structured logging
_logger.LogInformation("{Transfer}: download started", transfer);
_logger.LogDebug("Getting transfer {TransferId} from put.io", transferId);
_logger.LogWarning(ex, "{Transfer}: failed to get transfer status", transfer);

// Bad: String interpolation in logs
_logger.LogInformation($"Transfer {transfer.Id} started");  // Don't do this
```

### Log Level Overrides
- Override framework logs to reduce noise:
  - Microsoft: Warning
  - Microsoft.Hosting.Lifetime: Information
  - Polly: Warning

## Thread Safety

### Synchronization Patterns
- Use `Lock` type (C# 13+) instead of `lock(object)`
- Use thread-safe collections or protect shared state with locks
- Clean up resources (completed tasks, expired entries) periodically

```csharp
// Good: Modern lock pattern
private readonly HashSet<ulong> _seenTransfers = [];
private readonly Lock _seenLock = new();

private bool IsSeen(ulong id)
{
    lock (_seenLock)
    {
        return _seenTransfers.Contains(id);
    }
}
```

## Testing

### Framework and Libraries
- **xUnit**: Test runner
- **Moq**: Mocking dependencies
- **FluentAssertions**: Readable assertions
- **Microsoft.AspNetCore.Mvc.Testing**: Integration tests

### Test Organization
```
tests/Csharparr.Tests/
├── Configuration/AppConfigTests.cs
├── Download/DownloadTypesTests.cs
├── Http/TransmissionControllerTests.cs
├── Services/PutioClientTests.cs
├── Integration/DependencyInjectionTests.cs
```

### Testing Patterns
- Test happy paths and error conditions
- Mock HTTP clients and external dependencies
- Write integration tests for DI configuration
- Use FluentAssertions for readable test code

```csharp
// Good: FluentAssertions pattern
result.Should().NotBeNull();
result.Transfers.Should().HaveCount(2);
result.Status.Should().Be("COMPLETED");

// Good: Mock setup
var mockPutioClient = new Mock<IPutioClient>();
mockPutioClient
    .Setup(c => c.GetTransferAsync(It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(expectedTransfer);
```

### Critical: DI Integration Tests
Always verify new controllers and services can be resolved from DI:

```csharp
[Fact]
public void CanResolveTransmissionController()
{
    using var factory = new WebApplicationFactory<Program>();
    var scope = factory.Services.CreateScope();
    var controller = scope.ServiceProvider.GetService<TransmissionController>();
    controller.Should().NotBeNull();
}
```

## Configuration

### TOML Format
- Use Tomlyn for parsing TOML configuration
- Validate configuration at startup with detailed error messages
- Provide all validation errors at once (don't fail-fast)

### Required Fields
- `username`, `password`: HTTP basic auth
- `instance_name`: 3-10 alphanumeric characters
- `instance_folder_id`: Put.io folder ID
- `download_directory`: Local download path
- `putio.api_key`: Put.io API token
- At least one arr service configured

## API Design

### Transmission RPC Compatibility
- Implement Transmission RPC protocol for *arr service compatibility
- Use attribute routing: `[Route("/transmission/rpc")]`
- Preserve JSON property names: `PropertyNamingPolicy = null`

### Health Endpoint
- Provide `/health` endpoint for container orchestration
- Return simple 200 OK response

## Docker and Deployment

### Multi-stage Build
- Use multi-stage Dockerfile for smaller images
- Run as non-root user for security
- Include HEALTHCHECK for container orchestration
- Support multiple platforms: linux-x64, linux-arm64, osx-x64, osx-arm64

## What NOT to Do

- Don't add features or refactor beyond what's requested
- Don't add comments, docstrings, or type annotations to unchanged code
- Don't create helpers or abstractions for one-time operations
- Don't add error handling for scenarios that can't happen
- Don't use `Polly.Extensions.Http` (deprecated)—use `Microsoft.Extensions.Http.Resilience`
- Don't log at Error level for expected conditions (cancellation, circuit breaker)
- Don't use string interpolation in log messages
- Don't register concrete types when interfaces exist

## References

- [Microsoft .NET Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [C# Coding Guidelines](https://csharpcodingguidelines.com/)
- [Build Resilient HTTP Apps](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [Polly Documentation](https://www.pollydocs.org)
- [CancellationToken Best Practices](https://devblogs.microsoft.com/premier-developer/recommended-patterns-for-cancellationtoken/)
