using System.Text.Json.Serialization;

namespace Silo.Bot.Support.Models.Bale;

/// <summary>A message sent by a user inside a Bale chat.</summary>
public class BaleMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("chat")]
    public BaleChat? Chat { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("voice")]
    public BaleVoice? Voice { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}
