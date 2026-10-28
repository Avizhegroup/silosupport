using SiloSupport.Application.Dtos;

namespace SiloSupport.Service.CustomerSupport.Abstractions;

/// <summary>Port to the Bale Bot HTTP API (getUpdates long polling, getMe, sendMessage).</summary>
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
}
