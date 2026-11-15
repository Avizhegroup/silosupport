using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Models;

/// <summary>
/// A Bale message reduced to what the routing flow needs. The Infrastructure adapter maps the Bale JSON to this.
/// </summary>
/// <param name="MessageId">Bale message id inside the chat.</param>
/// <param name="ChatId">Chat/group the message originated from.</param>
/// <param name="SenderId">Bale user id of the sender, or null when Bale did not provide one.</param>
/// <param name="SenderIsBot">True when Bale flags the sender as a bot.</param>
/// <param name="Text">The plain text of an ordinary text message; null for non-text messages.</param>
/// <param name="HasNonTextContent">True when the message carries voice, audio, photo, document, video, sticker, etc.</param>
/// <param name="SenderUsername">Bale user name of the sender without '@' (for example "ali"), or null when the sender has none.</param>
/// <param name="Attachment">The photo, video, voice message, audio, document or animation of the message, if any.
/// For such a message <paramref name="Text"/> is its caption (null when it has none).</param>
public sealed record IncomingBaleMessage(
    long MessageId,
    long ChatId,
    long? SenderId,
    bool SenderIsBot,
    string? Text,
    bool HasNonTextContent,
    string? SenderUsername = null,
    IncomingAttachment? Attachment = null);

/// <summary>A file sent with a Bale message, as described by Bale.</summary>
/// <param name="FileId">The Bale file id: it downloads the file (getFile) and sends it again (sendPhoto, ...).</param>
/// <param name="FileSize">Size in bytes when Bale reported it.</param>
public sealed record IncomingAttachment(
    AttachmentKind Kind,
    string FileId,
    string? FileUniqueId,
    string? FileName,
    string? MimeType,
    long? FileSize);

/// <summary>A file downloaded from Bale.</summary>
public sealed record BaleDownloadedFile(byte[] Content, string? FilePath);

/// <summary>One getUpdates entry: its id (to advance the offset) and the message when it carried one.</summary>
public sealed record BalePolledUpdate(long UpdateId, IncomingBaleMessage? Message);
