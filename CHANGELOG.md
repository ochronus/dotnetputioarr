# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.1] - 2025-12-13

### Fixed

- **Critical Bug**: Fixed `TransmissionController` dependency injection issue that caused intermittent 500 errors in production
  - Controller was injecting concrete `PutioClient` type instead of `IPutioClient` interface
  - This caused `System.InvalidOperationException: Unable to resolve service for type 'Csharparr.Services.PutioClient'`
  - Error occurred randomly due to HttpClient scoped service lifetime and race conditions
- Fixed `Startup.cs` to properly use `UseEndpoints()` for controller mapping

### Added

- **Integration Tests**: Added comprehensive DI container integration tests (`DependencyInjectionTests`)
  - 8 new tests verify all controllers can be resolved from the DI container
  - Tests include concurrent resolution scenarios to catch race conditions
  - Would have caught the production bug before deployment
  - Total test count increased from 190 to 198 tests

### Changed

- Updated `IMPROVEMENT_IDEAS.md` to mark improvements #5, #6, and #9 as completed (they were already implemented in v0.2.0)

## [0.2.0] - 2025-12-13

### Added

- **Health Check Endpoint**: New `/health` endpoint for container orchestrators (Docker, Kubernetes)
  - Verifies Put.io API connectivity
  - Checks all configured Arr services (Sonarr/Radarr/Whisparr) connectivity
  - Tests download directory writability
  - Returns HTTP 200 OK when healthy, HTTP 503 when any check fails
  - Resolves improvement idea #9 (Health Check Endpoint)

### Changed

- **Major Refactoring**: Reworked all HTTP clients to use `IHttpClientFactory` for proper socket management and improved performance, resolving potential socket exhaustion issues.
- `PutioClient` is now registered as a typed client, simplifying its instantiation and management.
- Introduced `ArrClientFactory` to create `ArrClient` instances on-demand, preventing the inefficient creation and disposal of clients in loops.
- All client-related tests were refactored to use mocked dependencies, improving test isolation and reliability.
- This resolves improvement ideas #5 (`HttpClient` Management) and #6 (`ArrClient` Created/Disposed in Loop).

## [0.1.8] - 2025-12-13

### Changed

- Version is now read from assembly metadata at runtime instead of being hardcoded
- Version only needs to be maintained in the .csproj file (single source of truth)

## [0.1.5] - 2025-12-12

### Fixed

- Properly disabled verbose `IHttpClientFactory` logging (e.g., "Start processing HTTP request", "Sending HTTP request", "Received HTTP response headers") by using `RemoveAllLoggers()` on all HTTP client configurations

## [0.1.4] - 2025-12-12

### Fixed

- Attempted to suppress `System.Net.Http.HttpClient` framework logs via Serilog override (did not work - see 0.1.5)

## [0.1.3] - 2025-12-11

### Added

- HTTP resilience with Polly 8.x for automatic retries on transient failures
- Retry policies for Put.io API, Arr services, and file downloads with exponential backoff
- Circuit breaker to prevent hammering failing services
- Error response body included in exception messages for better debugging
- Debug-level logging for all Put.io API operations
- New tests for resilience behavior and retry logic

### Changed

- Refactored `PutioClient` to use `IHttpClientFactory` with named clients
- Refactored `ArrClient` to use `IHttpClientFactory` with named clients
- Downloads now use a dedicated HTTP client with appropriate timeout settings
- Polly logging set to Warning level to reduce noise

### Technical Details

- Retry up to 3 times with exponential backoff (1s, 2s, 4s) plus jitter
- Circuit breaker opens after 50% failure rate over 30 seconds
- Retries on: network errors, timeouts, 408, 429, 500, 502, 503, 504

## [0.1.2] - 2025-12-11

### Changed

- `ArrClient.CheckImportedMultiServiceAsync` now logs warnings when service checks fail instead of silently swallowing exceptions
- Background tasks for import and seeding watches are now tracked and properly handle errors
- Added error handling wrappers for `WatchForImportAsync` and `WatchSeedingAsync` that:
  - Log cancellation at Debug level (expected during shutdown)
  - Log unexpected errors at Error level
  - Clean up completed tasks to prevent unbounded memory growth

### Added

- New tests for `ArrClient` including logging verification
- New tests for `DownloadManager` background task tracking behavior

## [0.1.1] - 2025-12-11

### Changed

- HTTP request logging now uses DEBUG level by default to reduce log verbosity
- Errors and failed requests (status >= 400) still log at INFO level

## [0.1.0] - 2025-12-11

### Added

- Initial release of Csharparr, a C#/.NET port of [putioarr](https://github.com/ochronus/goputioarr)
- Put.io integration for cloud torrent management
- Arr services support (Sonarr, Radarr, Whisparr) for media library integration
- HTTP server for handling download requests from Arr services
- Download queue management with configurable concurrency
- Multi-platform support: linux-x64, linux-arm64, osx-x64, osx-arm64
- Docker images for amd64 and arm64 architectures
- Configuration via YAML file with environment variable overrides
- CLI commands:
  - `run` - Start the download service
  - `login` - Authenticate with Put.io via OAuth
  - `config` - Display current configuration

### Technical Highlights

- Built with .NET 10 and C# 14
- Modern C# features: records, pattern matching, file-scoped namespaces
- Self-contained, single-file executable builds
- Comprehensive test coverage
- CI/CD with GitHub Actions
- Dependabot configured for automated dependency updates

[0.2.0]: https://github.com/ochronus/csharparr/releases/tag/v0.2.0
[0.1.8]: https://github.com/ochronus/csharparr/releases/tag/v0.1.8
[0.1.5]: https://github.com/ochronus/csharparr/releases/tag/v0.1.5
[0.1.4]: https://github.com/ochronus/csharparr/releases/tag/v0.1.4
[0.1.3]: https://github.com/ochronus/csharparr/releases/tag/v0.1.3
[0.1.2]: https://github.com/ochronus/csharparr/releases/tag/v0.1.2
[0.1.1]: https://github.com/ochronus/csharparr/releases/tag/v0.1.1
[0.1.0]: https://github.com/ochronus/csharparr/releases/tag/v0.1.0