using System.Text.Json.Serialization;

namespace Csharparr.Services;

/// <summary>
/// Response model for Arr history API endpoint
/// </summary>
public sealed class HistoryResponse
{
    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("records")]
    public List<HistoryRecord> Records { get; set; } = [];
}

/// <summary>
/// Individual history record from Arr services
/// </summary>
public sealed class HistoryRecord
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, string> Data { get; set; } = [];
}
