using System.Text.Json.Serialization;

namespace SiloSupport.Application;

/// <summary>Token accounting for a RAG chat model call, matching Silo.Application.AI.Shared.ChatTokenUsageDto.</summary>
public class ChatTokenUsageDto
{
    [JsonPropertyName("inputTokenCount")]
    public long InputTokenCount { get; set; }

    [JsonPropertyName("outputTokenCount")]
    public long OutputTokenCount { get; set; }

    [JsonPropertyName("cachedInputTokenCount")]
    public long CachedInputTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public long TotalTokenCount { get; set; }
}
