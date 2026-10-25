using System.Text.Json.Serialization;

namespace Silo.Bot.Support.Models.Bale;

/// <summary>A single update received from the Bale Bot API via getUpdates.</summary>
public class BaleUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public BaleMessage? Message { get; set; }
}
