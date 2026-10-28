namespace SiloSupport.Application.Dtos;

/// <summary>
/// A Bale message reduced to what the routing flow needs. The Infrastructure adapter maps the Bale JSON to this.
/// </summary>
/// <param name="MessageId">Bale message id inside the chat.</param>
/// <param name="ChatId">Chat/group the message originated from.</param>
/// <param name="SenderId">Bale user id of the sender, or null when Bale did not provide one.</param>
/// <param name="SenderIsBot">True when Bale flags the sender as a bot.</param>
/// <param name="Text">The plain text of an ordinary text message; null for non-text messages.</param>
/// <param name="HasNonTextContent">True when the message carries voice, audio, photo, document, video, sticker, etc.</param>
public sealed record IncomingBaleMessage(
    long MessageId,
    long ChatId,
    long? SenderId,
    bool SenderIsBot,
    string? Text,
    bool HasNonTextContent);

/// <summary>One getUpdates entry: its id (to advance the offset) and the message when it carried one.</summary>
public sealed record BalePolledUpdate(long UpdateId, IncomingBaleMessage? Message);
