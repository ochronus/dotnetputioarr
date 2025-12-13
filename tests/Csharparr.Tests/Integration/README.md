# Integration Tests

This directory contains integration tests that verify the application's dependency injection configuration and component interactions.

## DependencyInjectionTests

These tests were added to prevent a production bug where `TransmissionController` was attempting to inject the concrete type `PutioClient` instead of the interface `IPutioClient`.

### The Bug

**Production Error:**
```
System.InvalidOperationException: Unable to resolve service for type 'Csharparr.Services.PutioClient' 
while attempting to activate 'Csharparr.Http.TransmissionController'.
```

**Root Cause:**
- In `Program.cs`, `PutioClient` was registered as: `AddHttpClient<IPutioClient, PutioClient>()`
- This registers `IPutioClient` in the DI container, not `PutioClient` directly
- `TransmissionController` was injecting `PutioClient` (concrete type) instead of `IPutioClient` (interface)
- The DI container couldn't resolve the concrete type, causing intermittent failures

**Why it was "random":**
- HttpClient-based services are scoped by default
- Under certain timing conditions (concurrent requests, scope disposal), resolution would fail
- Made the bug difficult to reproduce consistently

### Test Coverage

The `DependencyInjectionTests` class verifies:

1. **`ServiceProvider_ShouldResolve_TransmissionController`**
   - Ensures `TransmissionController` can be resolved from the DI container
   - Would have caught the original bug immediately

2. **`ServiceProvider_ShouldResolve_HealthController`**
   - Ensures `HealthController` can be resolved (uses IPutioClient correctly)

3. **`ServiceProvider_ShouldResolve_IPutioClient`**
   - Verifies `IPutioClient` is properly registered

4. **`ServiceProvider_ShouldResolve_IArrClientFactory`**
   - Verifies `IArrClientFactory` is properly registered

5. **`TransmissionController_ShouldInject_IPutioClient_NotConcreteType`**
   - Specifically tests that `TransmissionController` uses interface injection

6. **`ServiceProvider_WithMultipleScopes_ShouldResolve_Controllers`**
   - Tests that controllers can be resolved across multiple scopes
   - Simulates sequential request handling

7. **`ServiceProvider_ConcurrentResolution_ShouldNotFail`**
   - Tests concurrent DI resolution to catch race conditions
   - Simulates the production scenario that caused the bug

### Running the Tests

```bash
# Run all integration tests
dotnet test --filter "FullyQualifiedName~Integration"

# Run only DI tests
dotnet test --filter "FullyQualifiedName~DependencyInjectionTests"
```

### Best Practices

When adding new controllers or services:

1. **Always inject interfaces, not concrete types**
   ```csharp
   // ✅ Good
   public MyController(IPutioClient putioClient) { }
   
   // ❌ Bad
   public MyController(PutioClient putioClient) { }
   ```

2. **Add DI resolution tests for new controllers**
   - Add a test in `DependencyInjectionTests` when creating new controllers
   - This ensures they can be resolved from the DI container

3. **Match test DI configuration to production**
   - The `ConfigureServices` method in the tests should mirror `Program.cs`
   - Keep them in sync when modifying service registrations

### Related Files

- `src/Csharparr/Program.cs` - Production DI configuration
- `src/Csharparr/Http/TransmissionController.cs` - Fixed to use `IPutioClient`
- `src/Csharparr/Services/PutioClient.cs` - The service implementation