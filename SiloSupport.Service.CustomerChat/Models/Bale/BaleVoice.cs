using System.Text.Json.Serialization;

namespace SiloSupport.Service.CustomerChat.Models.Bale;

/// <summary>A voice message attached to a Bale message.</summary>
public class BaleVoice
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }
}
