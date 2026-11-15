using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloSupport.Service.CustomerSupport.Bale.Models;

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

    /// <summary>Bale user name without '@' (optional; users without one have none).</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

/// <summary>Bale "Chat" object.</summary>
public class BaleChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>Bale Video / Voice / Audio / Document / Animation object (the fields the bot needs).</summary>
public class BaleFileInfo
{
    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }

    [JsonPropertyName("file_unique_id")]
    public string? FileUniqueId { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    /// <summary>Size in bytes; Bale documents it as up to 52 significant bits, so it is a long.</summary>
    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }
}

/// <summary>One size of a Bale photo (the message carries several sizes of the same picture).</summary>
public class BalePhotoSize
{
    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }

    [JsonPropertyName("file_unique_id")]
    public string? FileUniqueId { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }
}

/// <summary>Result of getFile: where the file can be downloaded from (valid for one hour).</summary>
public class BaleFile
{
    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }
}

/// <summary>
/// Bale "Message" object reduced to the fields the routing flow needs. Photos, videos, voice messages, audio files,
/// documents and animations are read as files (downloaded and forwarded); stickers, contacts, locations and invoices
/// are only detected, they are not supported.
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

    /// <summary>The caption of a photo, video, voice message, audio file, document or animation.</summary>
    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("animation")]
    public BaleFileInfo? Animation { get; set; }

    [JsonPropertyName("audio")]
    public BaleFileInfo? Audio { get; set; }

    [JsonPropertyName("document")]
    public BaleFileInfo? Document { get; set; }

    [JsonPropertyName("photo")]
    public List<BalePhotoSize>? Photo { get; set; }

    [JsonPropertyName("sticker")]
    public JsonElement? Sticker { get; set; }

    [JsonPropertyName("video")]
    public BaleFileInfo? Video { get; set; }

    [JsonPropertyName("voice")]
    public BaleFileInfo? Voice { get; set; }

    [JsonPropertyName("contact")]
    public JsonElement? Contact { get; set; }

    [JsonPropertyName("location")]
    public JsonElement? Location { get; set; }

    [JsonPropertyName("invoice")]
    public JsonElement? Invoice { get; set; }

    /// <summary>True when any non-text payload (file or otherwise) is present.</summary>
    public bool HasNonTextContent =>
        Animation is not null || Audio is not null || Document is not null || Photo is { Count: > 0 }
        || Video is not null || Voice is not null
        || Present(Sticker) || Present(Contact) || Present(Location) || Present(Invoice);

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

/// <summary>Body of the documented getFile call.</summary>
public class BaleGetFileRequest
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;
}

/// <summary>Body of the documented sendMessage call.</summary>
public class BaleSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public long ChatId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
