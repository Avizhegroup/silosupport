using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SiloSupport.Service.CustomerChat.Configuration;
using SiloSupport.Service.CustomerChat.Models.Bale;
using SiloSupport.Service.CustomerChat.Services;
using SiloSupport.Service.CustomerChat.Configuration;
using SiloSupport.Service.CustomerChat.Models.Bale;
using SiloSupport.Service.CustomerChat.Services;

namespace SiloSupport.Service.CustomerChat.Workers;

/// <summary>
/// Long-polls the Bale Bot API for updates, forwards each user text message to the
/// Silo AI RAG endpoint, and sends the AI answer back to the originating Bale chat.
/// </summary>
public class BaleBotWorker : BackgroundService
{
    private const string FallbackErrorMessage =
        "متأسفانه در حال حاضر امکان پاسخگویی وجود ندارد. لطفاً کمی بعد دوباره تلاش کنید.";

    /// <summary>Maximum number of updates processed concurrently across all chats.</summary>
    private const int MaxConcurrentUpdates = 10;

    private readonly SemaphoreSlim _globalSemaphore = new(MaxConcurrentUpdates, MaxConcurrentUpdates);
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _perChatSemaphores = new();
    private readonly ConcurrentDictionary<long, Guid?> _chatConversations = new();

    private readonly BaleBotClient _baleClient;
    private readonly ISiloAiClient _siloAiClient;
    private readonly BaleOptions _options;
    private readonly ILogger<BaleBotWorker> _logger;

    public BaleBotWorker(
        BaleBotClient baleClient,
        ISiloAiClient siloAiClient,
        IOptions<BaleOptions> options,
        ILogger<BaleBotWorker> logger)
    {
        _baleClient = baleClient;
        _siloAiClient = siloAiClient;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bale bot worker started");

        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Polling Bale updates");

                var updates = await _baleClient.GetUpdatesAsync(
                    offset, _options.LongPollTimeoutSeconds, stoppingToken);

                // Advance the offset before dispatching processing so a failing
                // update is never fetched and processed again.
                foreach (var update in updates)
                {
                    if (update.UpdateId >= offset)
                        offset = update.UpdateId + 1;
                }

                var tasks = new List<Task>(updates.Count);
                foreach (var update in updates)
                    tasks.Add(ProcessUpdateAsync(update, stoppingToken));

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while polling Bale updates; retrying in 5 seconds");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Bale bot worker stopped");
    }

    private async Task ProcessUpdateAsync(BaleUpdate update, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Received Bale update {UpdateId}", update.UpdateId);

        var chat = update.Message?.Chat;
        var text = update.Message?.Text;
        var voice = update.Message?.Voice;

        if (chat is null || (string.IsNullOrWhiteSpace(text) && voice is null))
            return;

        var chatId = chat.Id;
        var chatSemaphore = _perChatSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));

        await _globalSemaphore.WaitAsync(stoppingToken);
        try
        {
            await chatSemaphore.WaitAsync(stoppingToken);
            try
            {
                if (voice is not null)
                    await HandleVoiceAsync(update.UpdateId, chatId, voice, stoppingToken);
                else
                    await HandleMessageAsync(update.UpdateId, chatId, text!, stoppingToken);
            }
            finally
            {
                chatSemaphore.Release();
            }
        }
        finally
        {
            _globalSemaphore.Release();
        }
    }

    private async Task HandleVoiceAsync(long updateId, long chatId, BaleVoice voice, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing voice message for chat {ChatId}", chatId);

        var audio = await _baleClient.DownloadFileAsync(voice.FileId, stoppingToken);
        if (audio is null || audio.Length == 0)
        {
            _logger.LogWarning("Failed to download voice for update {UpdateId} in chat {ChatId}", updateId, chatId);
            await _baleClient.SendMessageAsync(chatId, FallbackErrorMessage, stoppingToken);
            return;
        }

        _chatConversations.TryGetValue(chatId, out var conversationId);

        var contentType = "audio/wav";
        var fileName = "voice.wav";

        try
        {
            var originalLength = audio.Length;
            audio = OggOpusConverter.ToWav(audio);
            _logger.LogInformation(
                "Converted voice for chat {ChatId}: {OriginalBytes} bytes ogg/opus -> {WavBytes} bytes wav",
                chatId, originalLength, audio.Length);

            var debugPath = Path.Combine(Path.GetTempPath(), "silo-bale-voice-debug.wav");
            await File.WriteAllBytesAsync(debugPath, audio, stoppingToken);
            _logger.LogInformation("Saved debug WAV to {Path}", debugPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert voice for update {UpdateId} in chat {ChatId}", updateId, chatId);
            await _baleClient.SendMessageAsync(chatId, FallbackErrorMessage, stoppingToken);
            return;
        }

        var response = await _siloAiClient.SendVoiceAsync(
            conversationId, audio, fileName, contentType, RagDocType.Support, stoppingToken);

        if (response?.Audio is not { Length: > 0 })
        {
            _logger.LogWarning("Silo AI voice request failed for update {UpdateId} in chat {ChatId}", updateId, chatId);
            await _baleClient.SendMessageAsync(chatId, FallbackErrorMessage, stoppingToken);
            return;
        }

        if (response.ConversationId is not null)
            _chatConversations[chatId] = response.ConversationId;

        var sent = await _baleClient.SendVoiceAsync(chatId, response.Audio, response.ContentType, stoppingToken);
        if (!sent && !string.IsNullOrWhiteSpace(response.ResponseText))
            await _baleClient.SendMessageAsync(chatId, response.ResponseText, stoppingToken);
    }

    private async Task HandleMessageAsync(long updateId, long chatId, string text, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing message for chat {ChatId}", chatId);

        _chatConversations.TryGetValue(chatId, out var conversationId);

        if (conversationId is null)
        {
            _logger.LogInformation("Starting new Silo AI conversation for chat {ChatId}", chatId);
            conversationId = await _siloAiClient.StartNewSessionAsync(RagDocType.Support, stoppingToken);
            if (conversationId is not null)
                _chatConversations[chatId] = conversationId;
        }

        _logger.LogInformation("Sending request to Silo AI");
        var response = await _siloAiClient.SendAsync(conversationId, text, RagDocType.Support, stoppingToken);

        if (response is null || string.IsNullOrWhiteSpace(response.ResponseText))
        {
            _logger.LogWarning(
                "Silo AI request failed for update {UpdateId} in chat {ChatId}", updateId, chatId);

            _chatConversations.TryRemove(chatId, out _);
            await _baleClient.SendMessageAsync(chatId, FallbackErrorMessage, stoppingToken);
            return;
        }

        _logger.LogInformation("Received Silo AI response");
        _chatConversations[chatId] = response.ConversationId;

        _logger.LogInformation("Sending response to Bale chat {ChatId}", chatId);
        var sent = await _baleClient.SendMessageAsync(chatId, response.ResponseText, stoppingToken);
        if (!sent)
        {
            _logger.LogWarning(
                "Failed to send Bale message for update {UpdateId} in chat {ChatId}", updateId, chatId);
        }
    }
}
