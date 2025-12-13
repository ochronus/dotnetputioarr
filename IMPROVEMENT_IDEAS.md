# Improvement Ideas

This document tracks potential improvements for csharparr, organized by priority.

---

## 🔴 High Priority (Error Handling & Reliability)

### 1. ~~Silent Exception Swallowing in ArrClient~~ ✅ DONE (v0.1.2)

**Location:** `Services/ArrClient.cs` - `CheckImportedMultiServiceAsync`

The comment says "log the error" but exceptions are silently swallowed. This hides connectivity issues with Arr services.

```csharp
catch (Exception)
{
    // Log the error but continue checking other services
    continue;
}
```

**Fix:** Actually log the exception before continuing.

**Status:** Fixed in v0.1.2 - Added `ILogger` parameter to `CheckImportedMultiServiceAsync` and logging warnings on failure.

---

### 2. ~~Fire-and-Forget Tasks Without Tracking~~ ✅ DONE (v0.1.2)

**Location:** `Download/DownloadManager.cs` - `OrchestrationWorkerAsync`

Tasks for `WatchForImportAsync` and `WatchSeedingAsync` are started with `_ =` and never tracked. If these tasks fail or hang, there's no visibility, and they could accumulate unbounded.

**Fix:** Track these tasks in a collection, log failures, or use a bounded concurrent task tracker.

**Status:** Fixed in v0.1.2 - Added `TrackBackgroundTask` method with automatic cleanup, plus error-handling wrappers that log at appropriate levels (Debug for cancellation, Error for unexpected failures).

---

### 3. ~~No Retry Logic for HTTP Calls~~ ✅ DONE (v0.1.3)

All `PutioClient` and `ArrClient` HTTP calls fail immediately on any error. Network glitches, rate limits, or temporary outages cause immediate failures with no recovery.

**Fix:** Add [Polly](https://github.com/App-vNext/Polly) or similar retry policies with exponential backoff.

**Status:** Fixed in v0.1.3 - Added Polly 8.x with `Microsoft.Extensions.Http.Resilience`. Configured retry policies (3 attempts, exponential backoff with jitter) and circuit breakers for Put.io API, Arr services, and downloads.

---

### 4. ~~Insufficient Error Details in Exceptions~~ ✅ DONE (v0.1.3)

**Location:** `Services/PutioClient.cs`

Exceptions only capture the HTTP status code, not the response body which often contains useful error details from the API.

```csharp
throw new PutioException($"Error getting put.io account info: {response.StatusCode}");
```

**Fix:** Read and include the response body in exception messages when available.

**Status:** Fixed in v0.1.3 - Added `TryReadErrorBodyAsync` helper to both `PutioClient` and `ArrClient` that reads and includes response body (truncated to 500 chars) in exception messages.

---

## 🟠 Medium Priority (Resource Management & Performance)

### 5. HttpClient Management

**Location:** `Services/PutioClient.cs`, `Program.cs`

`PutioClient` is registered as a singleton but creates its own `HttpClient` internally. This bypasses proper socket management and can lead to socket exhaustion.

**Fix:** Inject `IHttpClientFactory` and use named/typed clients.

---

### 6. ArrClient Created/Disposed in Loop

**Location:** `Services/ArrClient.cs` - `CheckImportedMultiServiceAsync`

A new `HttpClient` is created and disposed for each service in the loop. Inefficient and wasteful.

**Fix:** Refactor to use pooled HTTP clients via `IHttpClientFactory`.

---

### 7. ~~Hardcoded Version Out of Sync~~ ✅ DONE (v0.1.8)

**Location:** `Commands/CommandHandler.cs`

```csharp
public const string Version = "0.5.0";
```

This constant doesn't match the version in the csproj file and must be manually updated.

**Fix:** Read version from assembly metadata at runtime:
```csharp
Assembly.GetExecutingAssembly().GetName().Version?.ToString()
```

**Status:** Fixed in v0.1.8 - Changed `Version` from a hardcoded constant to a property that reads from assembly metadata using `Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)`. Version is now only maintained in the .csproj file.

---

### 8. Unused `uid` Configuration Option

The `uid` config option is documented, parsed, and stored but never actually used to change file ownership.

**Fix:** Either implement the chown functionality (requires platform-specific code) or remove the option from config/docs.

---

## 🟡 Lower Priority (Features & Improvements)

### 9. No Health Check Endpoint

No way for container orchestrators (Docker, Kubernetes) to verify service health.

**Fix:** Add a `/health` endpoint that checks:
- Put.io API connectivity
- Arr service connectivity (optional)
- Download directory writability

---

### 10. Only VIDEO File Types Downloaded

**Location:** `Download/DownloadManager.cs` - `RecurseDownloadTargetsAsync`

Only files with `FileType == "VIDEO"` are processed. Subtitles, NFO files, and other media-adjacent files are silently ignored.

**Fix:** Make downloadable file types configurable, or download all non-skipped files by default.

---

### 11. No Download Resume Capability

If a download is interrupted (crash, restart, network issue), it starts from scratch. For large files, this wastes bandwidth and time.

**Fix:** Implement HTTP Range requests to resume partial downloads. The `.downloading` temp file approach is already in place.

---

### 12. No Graceful Shutdown Handling

In-progress downloads aren't tracked during shutdown, potentially resulting in partial/corrupt files.

**Fix:** 
- Track active downloads
- On shutdown signal, wait for in-progress downloads to complete (with timeout)
- Or clean up partial files

---

### 13. Missing Configuration Validation

- No URL format validation for Arr service URLs
- No bounds checking on numeric configs (negative `polling_interval` would behave oddly)
- No warning about config file permissions (file contains credentials)

**Fix:** Add validation in `AppConfig.Validate()` for:
- Valid URL formats
- Positive integers for workers, intervals, port
- Warn if config file is world-readable

---

### 14. Transmission Protocol Response Gaps

**Location:** `Http/TransmissionController.cs`

Some RPC methods return `null` silently:
```csharp
"torrent-set" => null,
"queue-move-top" => null,
```

The `torrent-add` method doesn't return info about the added torrent, which some clients might expect.

**Fix:** Return proper response objects matching Transmission protocol spec.

---

## 🟢 Nice to Have (Observability & Polish)

### 15. No Metrics/Telemetry

No observability features:
- No Prometheus metrics endpoint
- No OpenTelemetry tracing
- No correlation IDs for tracking transfers through the pipeline

**Fix:** Add optional metrics endpoint with:
- Active downloads count
- Download speeds
- Transfer state counts (queued, downloading, seeding, etc.)
- Error rates

---

### 16. No Bandwidth Limiting

Downloads run at maximum speed with no throttling option.

**Fix:** Add optional `max_download_speed` config to limit bandwidth usage.

---

### 17. Test Coverage Gaps

Current state:
- Good model/unit tests ✓
- Missing integration tests
- No tests for `DownloadManager` (the most complex component)

**Fix:** 
- Add integration tests with mocked HTTP responses
- Add `DownloadManager` unit tests using mocked dependencies

---

## Implementation Order Suggestion

1. ~~Fix logging in `ArrClient` (#1) - quick win~~ ✅ DONE
2. ~~Fix fire-and-forget task tracking (#2)~~ ✅ DONE
3. ~~Add retry logic with Polly (#3) - high impact~~ ✅ DONE
4. ~~Add error response bodies (#4)~~ ✅ DONE
5. Add health check endpoint (#9)
6. ~~Fix version handling (#7)~~ ✅ DONE
7. Refactor HttpClient usage (#5, #6) - partially done via Polly integration
8. Everything else based on need