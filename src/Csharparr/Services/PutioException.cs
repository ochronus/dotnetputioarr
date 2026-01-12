using System.Net;

namespace Csharparr.Services;

/// <summary>
/// Exception thrown when a Put.io API operation fails
/// </summary>
public class PutioException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public PutioException(string message) : base(message) { }
    public PutioException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
    public PutioException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a put.io resource is not found (404).
/// This typically indicates external deletion (user deleted file/transfer on put.io directly).
/// </summary>
public class PutioNotFoundException : PutioException
{
    public PutioNotFoundException(string message) : base(message, HttpStatusCode.NotFound) { }
}

/// <summary>
/// Exception thrown when put.io rate limits requests (429).
/// </summary>
public class PutioRateLimitException : PutioException
{
    public TimeSpan? RetryAfter { get; }

    public PutioRateLimitException(string message, TimeSpan? retryAfter = null)
        : base(message, HttpStatusCode.TooManyRequests)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Exception thrown when put.io returns a server error (5xx).
/// These are typically transient and worth retrying.
/// </summary>
public class PutioServerException : PutioException
{
    public PutioServerException(string message, HttpStatusCode statusCode)
        : base(message, statusCode) { }
}
