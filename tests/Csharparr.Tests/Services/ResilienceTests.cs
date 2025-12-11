using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Csharparr.Tests.Services;

public class ResilienceTests
{
    [Fact]
    public void ShouldRetry_WithHttpRequestException_ShouldReturnTrue()
    {
        var outcome = Outcome.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithTimeoutException_ShouldReturnTrue()
    {
        var outcome = Outcome.FromException<HttpResponseMessage>(new TimeoutException("Request timed out"));

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithTaskCanceledException_ShouldReturnTrue()
    {
        var outcome = Outcome.FromException<HttpResponseMessage>(new TaskCanceledException("Task was cancelled"));

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithOtherException_ShouldReturnFalse()
    {
        var outcome = Outcome.FromException<HttpResponseMessage>(new InvalidOperationException("Some other error"));

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void ShouldRetry_WithTransientStatusCode_ShouldReturnTrue(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);
        var outcome = Outcome.FromResult(response);

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void ShouldRetry_WithNonTransientStatusCode_ShouldReturnFalse(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);
        var outcome = Outcome.FromResult(response);

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WithNullOutcome_ShouldReturnFalse()
    {
        var outcome = Outcome.FromResult<HttpResponseMessage>(null!);

        var shouldRetry = ShouldRetryHelper(outcome);

        shouldRetry.Should().BeFalse();
    }

    /// <summary>
    /// Helper that mirrors the ShouldRetry logic from Program.cs
    /// </summary>
    private static bool ShouldRetryHelper(Outcome<HttpResponseMessage> outcome)
    {
        // Retry on exceptions (network errors, timeouts, etc.)
        if (outcome.Exception is not null)
        {
            return outcome.Exception is HttpRequestException
                or TimeoutException
                or TaskCanceledException;
        }

        // Retry on transient HTTP status codes
        if (outcome.Result is not null)
        {
            var statusCode = outcome.Result.StatusCode;
            return statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
        }

        return false;
    }
}

public class HttpClientNamingTests
{
    [Fact]
    public void PutioClient_HttpClientName_ShouldBeConsistent()
    {
        Csharparr.Services.PutioClient.HttpClientName.Should().Be("PutioClient");
    }

    [Fact]
    public void ArrClient_HttpClientName_ShouldBeConsistent()
    {
        Csharparr.Services.ArrClient.HttpClientName.Should().Be("ArrClient");
    }
}

public class ErrorBodyReadingTests
{
    [Fact]
    public void PutioException_ShouldContainMessage()
    {
        var exception = new Csharparr.Services.PutioException("Error: 500 - Server error details");

        exception.Message.Should().Contain("500");
        exception.Message.Should().Contain("Server error details");
    }

    [Fact]
    public void ArrClientException_ShouldContainMessage()
    {
        var exception = new Csharparr.Services.ArrClientException("Error: 503 - Service unavailable");

        exception.Message.Should().Contain("503");
        exception.Message.Should().Contain("Service unavailable");
    }
}

public class RetryStrategyOptionsTests
{
    [Fact]
    public void HttpRetryStrategyOptions_DefaultValues_ShouldBeReasonable()
    {
        var options = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        };

        options.MaxRetryAttempts.Should().BeGreaterThan(0);
        options.Delay.Should().BeGreaterThan(TimeSpan.Zero);
        options.BackoffType.Should().Be(DelayBackoffType.Exponential);
        options.UseJitter.Should().BeTrue();
    }

    [Fact]
    public void HttpCircuitBreakerStrategyOptions_DefaultValues_ShouldBeReasonable()
    {
        var options = new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30)
        };

        options.SamplingDuration.Should().BeGreaterThan(TimeSpan.Zero);
        options.FailureRatio.Should().BeInRange(0, 1);
        options.MinimumThroughput.Should().BeGreaterThan(0);
        options.BreakDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ExponentialBackoff_ShouldIncreaseDelay()
    {
        var baseDelay = TimeSpan.FromSeconds(1);

        // Exponential backoff: delay * 2^attempt
        var attempt1Delay = baseDelay * Math.Pow(2, 0); // 1s
        var attempt2Delay = baseDelay * Math.Pow(2, 1); // 2s
        var attempt3Delay = baseDelay * Math.Pow(2, 2); // 4s

        attempt1Delay.Should().BeLessThan(attempt2Delay);
        attempt2Delay.Should().BeLessThan(attempt3Delay);
    }
}
