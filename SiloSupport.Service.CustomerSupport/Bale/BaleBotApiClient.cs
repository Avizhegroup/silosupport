using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Service.CustomerSupport.Bale.Models;

namespace SiloSupport.Service.CustomerSupport.Bale;

/// <summary>
/// Bale Bot API adapter (getUpdates, getMe, sendMessage) over the named HttpClient <see cref="HttpClientName"/>.
/// The client's base address is <c>{ApiBaseUrl}/bot{token}/</c>, so request URLs contain the bot token:
/// they must never be logged (see the HttpClient log-level note in appsettings.json).
/// </summary>
public class BaleBotApiClient : IBaleBotClient
{
    public const string HttpClientName = "BaleBotApi";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ILogger<BaleBotApiClient> _logger;
    private long? _botUserId;

    public BaleBotApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BaleBotOptions> options,
        ILogger<BaleBotApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _ = options; // reserved for future per-call settings; the client is configured at registration.
        _logger = logger;
    }

    public async Task<IReadOnlyList<BalePolledUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<BaleApiResponse<List<BaleUpdate>>>(
            $"getUpdates?offset={offset}&timeout={timeoutSeconds}", JsonOptions, cancellationToken);

        if (response is null || !response.Ok)
        {
            throw new InvalidOperationException(
                $"Bale getUpdates failed (error_code={response?.ErrorCode}): {response?.Description}");
        }

        return (response.Result ?? new List<BaleUpdate>())
            .Select(Map)
            .ToList();
    }

    public async Task<long?> GetBotUserIdAsync(CancellationToken cancellationToken)
    {
        if (_botUserId is not null)
            return _botUserId;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<BaleApiResponse<BaleUser>>("getMe", JsonOptions, cancellationToken);
            if (response?.Ok == true && response.Result?.Id is long id)
            {
                _botUserId = id;
                return id;
            }

            _logger.LogWarning("Bale getMe did not return a bot id (error_code={ErrorCode})", response?.ErrorCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bale getMe failed; relying on the is_bot flag only");
        }

        return null;
    }

    public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "sendMessage",
                new BaleSendMessageRequest { ChatId = chatId, Text = text },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Bale sendMessage failed for chat {ChatId} with status code {StatusCode}",
                    chatId, (int)response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<BaleApiResponse<JsonElement>>(JsonOptions, cancellationToken);
            if (body?.Ok == true)
                return true;

            _logger.LogWarning(
                "Bale sendMessage for chat {ChatId} returned ok=false (error_code={ErrorCode})",
                chatId, body?.ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending Bale message to chat {ChatId}", chatId);
        }

        return false;
    }

    private static BalePolledUpdate Map(BaleUpdate update)
    {
        var message = update.Message;
        if (message?.Chat is null)
            return new BalePolledUpdate(update.UpdateId, null);

        return new BalePolledUpdate(
            update.UpdateId,
            new IncomingBaleMessage(
                message.MessageId,
                message.Chat.Id,
                message.From?.Id,
                message.From?.IsBot ?? false,
                message.Text,
                message.HasNonTextContent));
    }
}
