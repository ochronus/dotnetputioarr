namespace Csharparr.Services;

/// <summary>
/// Exception thrown when an error occurs in the Arr client
/// </summary>
public class ArrClientException : Exception
{
    public ArrClientException(string message) : base(message) { }
    public ArrClientException(string message, Exception innerException) : base(message, innerException) { }
}
