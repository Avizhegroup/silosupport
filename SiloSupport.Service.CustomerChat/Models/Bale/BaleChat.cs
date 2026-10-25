using System.Text.Json.Serialization;

namespace Silo.Bot.Support.Models.Bale;

/// <summary>The chat (conversation) where the message was sent.</summary>
public class BaleChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
