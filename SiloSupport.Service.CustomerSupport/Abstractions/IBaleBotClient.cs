using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Abstractions;

/// <summary>Port to the Bale Bot HTTP API (getUpdates long polling, getMe, sendMessage, getFile and the send-file methods).</summary>
public interface IBaleBotClient
{
    /// <summary>
    /// Long-polls getUpdates. Throws when the request fails so the caller can back off; an empty list means
    /// no updates arrived within the timeout.
    /// </summary>
    Task<IReadOnlyList<BalePolledUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>Returns the bot's own Bale user id (getMe, cached), or null when it cannot be determined.</summary>
    Task<long?> GetBotUserIdAsync(CancellationToken cancellationToken);

    /// <summary>Sends a text message via sendMessage. True only when Bale confirmed success.</summary>
    Task<bool> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a file that already exists on the Bale servers (identified by its <paramref name="fileId"/>) to a chat with
    /// sendPhoto / sendVideo / sendVoice / sendAudio / sendDocument / sendAnimation, the text as its caption.
    /// True only when Bale confirmed success. Re-sending by file id has no size limit.
    /// </summary>
    Task<bool> SendAttachmentAsync(long chatId, AttachmentKind kind, string fileId, string? caption, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a file (getFile, then the file URL). Returns null - never throws for Bale problems - when the file is
    /// larger than <paramref name="maxBytes"/>, unavailable or the download fails.
    /// </summary>
    Task<BaleDownloadedFile?> DownloadFileAsync(string fileId, long maxBytes, CancellationToken cancellationToken);
}
