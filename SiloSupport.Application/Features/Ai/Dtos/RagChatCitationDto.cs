using System.Text.Json.Serialization;

namespace SiloSupport.Application;

/// <summary>Citation entry returned alongside a RAG chat answer, matching Silo.Application.AI.Shared.RagChatCitationDto.</summary>
public class RagChatCitationDto
{
    [JsonPropertyName("chunkId")]
    public Guid ChunkId { get; set; }

    [JsonPropertyName("documentId")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("similarity")]
    public double Similarity { get; set; }

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;
}
