using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Enums;
using SiloSupport.Service.CustomerSupport.Bale.Models;

namespace SiloSupport.Service.CustomerSupport.Bale;

/// <summary>
/// Bale Bot API adapter (getUpdates, getMe, sendMessage, getFile, sendPhoto/sendVideo/sendVoice/sendAudio/sendDocument/sendAnimation)
/// over the named HttpClient <see cref="HttpClientName"/>.
/// The client's base address is <c>{ApiBaseUrl}/bot{token}/</c>, so request URLs contain the bot token:
/// they must never be logged (see the HttpClient log-level note in appsettings.json).
/// </summary>
public class BaleBotApiClient : IBaleBotClient
{
    public const string HttpClientName = "BaleBotApi";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly BaleBotOptions _options;
    private readonly ILogger<BaleBotApiClient> _logger;
    private long? _botUserId;

    public BaleBotApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BaleBotOptions> options,
        ILogger<BaleBotApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
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

    public async Task<bool> SendAttachmentAsync(long chatId, AttachmentKind kind, string fileId, string? caption, CancellationToken cancellationToken)
    {
        var (method, field) = kind switch
        {
            AttachmentKind.Photo => ("sendPhoto", "photo"),
            AttachmentKind.Video => ("sendVideo", "video"),
            AttachmentKind.Voice => ("sendVoice", "voice"),
            AttachmentKind.Audio => ("sendAudio", "audio"),
            AttachmentKind.Animation => ("sendAnimation", "animation"),
            _ => ("sendDocument", "document")
        };

        var body = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            [field] = fileId
        };

        if (!string.IsNullOrWhiteSpace(caption))
            body["caption"] = caption;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(method, body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Bale {Method} failed for chat {ChatId} with status code {StatusCode}",
                    method, chatId, (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<BaleApiResponse<JsonElement>>(JsonOptions, cancellationToken);
            if (result?.Ok == true)
                return true;

            _logger.LogWarning(
                "Bale {Method} for chat {ChatId} returned ok=false (error_code={ErrorCode})",
                method, chatId, result?.ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception calling Bale {Method} for chat {ChatId}", method, chatId);
        }

        return false;
    }

    public async Task<BaleDownloadedFile?> DownloadFileAsync(string fileId, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            // 1. getFile tells where the file can be fetched from.
            using var infoResponse = await _httpClient.PostAsJsonAsync("getFile", new BaleGetFileRequest { FileId = fileId }, cancellationToken);
            if (!infoResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bale getFile failed with status code {StatusCode}", (int)infoResponse.StatusCode);
                return null;
            }

            var info = await infoResponse.Content.ReadFromJsonAsync<BaleApiResponse<BaleFile>>(JsonOptions, cancellationToken);
            var filePath = info?.Result?.FilePath;
            if (info?.Ok != true || string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogWarning("Bale getFile returned no file path (error_code={ErrorCode})", info?.ErrorCode);
                return null;
            }

            if (info.Result?.FileSize is long reportedSize && reportedSize > maxBytes)
            {
                _logger.LogInformation("Bale file is {Size} bytes, over the {Max}-byte download limit; it will not be stored", reportedSize, maxBytes);
                return null;
            }

            // 2. Download it. The URL contains the bot token, so it is never logged.
            var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/file/bot{_options.BotToken}/{filePath.TrimStart('/')}";
            using var fileResponse = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!fileResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bale file download failed with status code {StatusCode}", (int)fileResponse.StatusCode);
                return null;
            }

            if (fileResponse.Content.Headers.ContentLength is long length && length > maxBytes)
            {
                _logger.LogInformation("Bale file is {Size} bytes, over the {Max}-byte download limit; it will not be stored", length, maxBytes);
                return null;
            }

            await using var stream = await fileResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    _logger.LogInformation("Bale file exceeded the {Max}-byte download limit while downloading; it will not be stored", maxBytes);
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return new BaleDownloadedFile(buffer.ToArray(), filePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Downloading a Bale file failed");
            return null;
        }
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
                // The caption of a file is its text.
                message.Text ?? message.Caption,
                message.HasNonTextContent,
                message.From?.Username,
                MapAttachment(message)));
    }

    /// <summary>
    /// The file of a message: the largest size of a photo, a video, a voice message, an audio file, an animation or a
    /// document. An animation is also reported as a document by Bale, so it is checked first.
    /// </summary>
    private static IncomingAttachment? MapAttachment(BaleMessage message)
    {
        var photo = message.Photo?
            .Where(p => !string.IsNullOrWhiteSpace(p.FileId))
            .OrderByDescending(p => p.FileSize ?? 0)
            .ThenByDescending(p => (long)(p.Width ?? 0) * (p.Height ?? 0))
            .FirstOrDefault();

        if (photo is not null)
            return new IncomingAttachment(AttachmentKind.Photo, photo.FileId!, photo.FileUniqueId, null, "image/jpeg", photo.FileSize);

        return From(AttachmentKind.Video, message.Video)
            ?? From(AttachmentKind.Voice, message.Voice)
            ?? From(AttachmentKind.Audio, message.Audio)
            ?? From(AttachmentKind.Animation, message.Animation)
            ?? From(AttachmentKind.Document, message.Document);

        static IncomingAttachment? From(AttachmentKind kind, BaleFileInfo? file) =>
            file is null || string.IsNullOrWhiteSpace(file.FileId)
                ? null
                : new IncomingAttachment(kind, file.FileId, file.FileUniqueId, file.FileName, file.MimeType, file.FileSize);
    }
}
