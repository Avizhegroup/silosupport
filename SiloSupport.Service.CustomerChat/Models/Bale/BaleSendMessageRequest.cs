using System.Text.Json.Serialization;

namespace Silo.Bot.Support.Models.Bale;

/// <summary>Request body for the sendMessage Bale API call.</summary>
public class BaleSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public long ChatId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
