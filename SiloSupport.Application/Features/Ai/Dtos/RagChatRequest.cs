using System.Text.Json.Serialization;

namespace SiloSupport.Application;

/// <summary>Request body for POST /api/rag/chat/send, matching Silo.Application.AI.Shared.RagChatRequest.</summary>
public class RagChatRequest
{
    [JsonPropertyName("conversationId")]
    public Guid? ConversationId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("topK")]
    public int TopK { get; set; } = 5;

    [JsonPropertyName("isMainChat")]
    public bool IsMainChat { get; set; }

    [JsonPropertyName("docType")]
    public RagDocType DocType { get; set; } = RagDocType.Support;

    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
