using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloSupport.Infrastructure.Bale.Models;

/// <summary>Wrapper returned by every Bale Bot API call (documented fields: ok, result, description, error_code).</summary>
public class BaleApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }
}

/// <summary>Bale "User" object. The id may exceed 32 bits (up to 52 significant bits).</summary>
public class BaleUser
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }
}

/// <summary>Bale "Chat" object.</summary>
public class BaleChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>
/// Bale "Message" object reduced to the fields the routing flow needs. Media fields are kept as raw JSON only so
/// their presence can be detected; media is never downloaded or analysed.
/// </summary>
public class BaleMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("from")]
    public BaleUser? From { get; set; }

    [JsonPropertyName("chat")]
    public BaleChat? Chat { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("animation")]
    public JsonElement? Animation { get; set; }

    [JsonPropertyName("audio")]
    public JsonElement? Audio { get; set; }

    [JsonPropertyName("document")]
    public JsonElement? Document { get; set; }

    [JsonPropertyName("photo")]
    public JsonElement? Photo { get; set; }

    [JsonPropertyName("sticker")]
    public JsonElement? Sticker { get; set; }

    [JsonPropertyName("video")]
    public JsonElement? Video { get; set; }

    [JsonPropertyName("voice")]
    public JsonElement? Voice { get; set; }

    [JsonPropertyName("contact")]
    public JsonElement? Contact { get; set; }

    [JsonPropertyName("location")]
    public JsonElement? Location { get; set; }

    [JsonPropertyName("invoice")]
    public JsonElement? Invoice { get; set; }

    /// <summary>True when any non-text payload is present.</summary>
    public bool HasNonTextContent =>
        Present(Animation) || Present(Audio) || Present(Document) || Present(Photo) || Present(Sticker)
        || Present(Video) || Present(Voice) || Present(Contact) || Present(Location) || Present(Invoice);

    private static bool Present(JsonElement? element) =>
        element is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
}

/// <summary>A getUpdates entry. Only <c>message</c> updates are routed; other update kinds are skipped.</summary>
public class BaleUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public BaleMessage? Message { get; set; }
}

/// <summary>Body of the documented sendMessage call.</summary>
public class BaleSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public long ChatId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
