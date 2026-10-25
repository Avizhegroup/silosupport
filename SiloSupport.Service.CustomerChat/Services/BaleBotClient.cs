using System.Text.Json;
using Microsoft.Extensions.Options;
using Silo.Bot.Support.Configuration;
using Silo.Bot.Support.Models.Bale;

namespace Silo.Bot.Support.Services;

/// <summary>Thin wrapper over the Bale Bot HTTP API using a named HttpClient ("BaleClient").</summary>
public class BaleBotClient 
{
    public const string HttpClientName = "BaleClient";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly BaleOptions _options;
    private readonly ILogger<BaleBotClient> _logger;

    public BaleBotClient(IHttpClientFactory httpClientFactory, IOptions<BaleOptions> options, ILogger<BaleBotClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Downloads a file (e.g. a voice message) by its Bale file id, or returns null on failure.</summary>
    public async Task<byte[]?> DownloadFileAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = await _httpClient.GetFromJsonAsync<BaleApiResponse<BaleFile>>(
                $"getFile?file_id={Uri.EscapeDataString(fileId)}", JsonOptions, cancellationToken);

            var filePath = fileInfo?.Result?.FilePath;
            if (fileInfo?.Ok != true || string.IsNullOrWhiteSpace(filePath))
                return null;

            var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/file/bot{_options.BotToken}/{filePath}";
            return await _httpClient.GetByteArrayAsync(url, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading Bale file {FileId}", fileId);
            return null;
        }
    }

    /// <summary>Sends an audio answer to the chat using sendVoice.</summary>
    public async Task<bool> SendVoiceAsync(long chatId, byte[] audio, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");

            var audioContent = new ByteArrayContent(audio);
            audioContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
                ? mediaType
                : new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(audioContent, "voice", contentType.Contains("ogg") ? "answer.ogg" : "answer.mp3");

            using var response = await _httpClient.PostAsync("sendVoice", form, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            _logger.LogWarning(
                "Bale sendVoice failed for chat {ChatId} with status code {StatusCode}",
                chatId,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending Bale voice to chat {ChatId}", chatId);
        }

        return false;
    }

    public async Task<List<BaleUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var url = $"getUpdates?offset={offset}&timeout={timeoutSeconds}";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<BaleApiResponse<List<BaleUpdate>>>(url, JsonOptions, cancellationToken);
            if (response?.Ok == true && response.Result != null)
                return response.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Bale getUpdates (offset={Offset})", offset);
        }

        return new List<BaleUpdate>();
    }

    public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var body = new BaleSendMessageRequest { ChatId = chatId, Text = text };
        try
        {
            var response = await _httpClient.PostAsJsonAsync("sendMessage", body, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            _logger.LogWarning(
                "Bale sendMessage failed for chat {ChatId} with status code {StatusCode}",
                chatId,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending Bale message to chat {ChatId}", chatId);
        }

        return false;
    }
}
