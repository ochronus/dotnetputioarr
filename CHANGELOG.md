# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.1.2]: https://github.com/ochronus/csharparr/releases/tag/v0.1.2
[0.1.1]: https://github.com/ochronus/csharparr/releases/tag/v0.1.1
[0.1.0]: https://github.com/ochronus/csharparr/releases/tag/v0.1.0