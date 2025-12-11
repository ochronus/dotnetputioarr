namespace Csharparr.Services;

/// <summary>
/// Exception thrown when a Put.io API operation fails
/// </summary>
public class PutioException : Exception
{
    public PutioException(string message) : base(message) { }
    public PutioException(string message, Exception innerException) : base(message, innerException) { }
}
