using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiloSupport.Application;

namespace SiloSupport.Infrastructure.Shared;

/// <summary>Client that calls POST /api/rag/chat/send on the Silo AI API using a named HttpClient ("SiloAiClient").</summary>
public class SiloAiClient : ISiloAiClient
{
    public const string HttpClientName = "SiloAiClient";
    private const string SendEndpoint = "api/rag/chat/send";
    private const string VoiceEndpoint = "api/rag/chat/voice";
    private const string NewSessionEndpoint = "api/rag/chat/new-session";

    private readonly HttpClient _httpClient;
    private readonly RagAiOptions _options;
    private readonly ILogger<SiloAiClient> _logger;

    public SiloAiClient(IHttpClientFactory httpClientFactory, IOptions<RagAiOptions> options, ILogger<SiloAiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Guid?> StartNewSessionAsync(RagDocType docType, CancellationToken cancellationToken)
    {
        RagChatRequest request = new()
        {
            DocType = docType 
        };

        var response = await _httpClient.PostAsJsonAsync(NewSessionEndpoint, request, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<RagChatResponse>(cancellationToken: cancellationToken);
     
        return result?.ConversationId;
    }

    public async Task<RagChatResponse?> SendAsync(Guid? conversationId, string message, RagDocType docType, CancellationToken cancellationToken)
    {
        RagChatRequest request = new()
        {
            ConversationId = conversationId,
            Message = message,
            TopK = _options.TopK,
            IsMainChat = true,
            DocType = docType,
            Key = _options.Key
        };

        var response = await _httpClient.PostAsJsonAsync(SendEndpoint, request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Silo AI request failed with status code {StatusCode}",
                (int)response.StatusCode);

            return new()
            {
                StatusCode = response.StatusCode
            };
        }

        return await response.Content.ReadFromJsonAsync<RagChatResponse>(cancellationToken: cancellationToken);
    }

    public async Task<RagVoiceResponse?> SendVoiceAsync(Guid? conversationId, byte[] audio, string fileName, string contentType, RagDocType docType, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(audio);
        audioContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            ? mediaType
            : new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(audioContent, "audio", fileName);

        if (conversationId.HasValue)
            content.Add(new StringContent(conversationId.Value.ToString()), "conversationId");

        content.Add(new StringContent(docType.ToString()), "docType");
        content.Add(new StringContent("true"), "isMainChat");

        if (!string.IsNullOrWhiteSpace(_options.Key))
            content.Add(new StringContent(_options.Key), "key");

        using var response = await _httpClient.PostAsync(VoiceEndpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Silo AI voice request failed with status code {StatusCode}",
                (int)response.StatusCode);

            return new()
            {
                StatusCode = response.StatusCode
            };
        }

        var result = new RagVoiceResponse
        {
            Audio = await response.Content.ReadAsByteArrayAsync(cancellationToken),
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg",
            Transcript = ReadHeader(response, "X-Transcript"),
            ResponseText = ReadHeader(response, "X-Response-Text"),
            StatusCode = response.StatusCode
        };

        if (Guid.TryParse(ReadHeader(response, "X-Conversation-Id"), out var id))
            result.ConversationId = id;

        return result;
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;

        var value = values.FirstOrDefault();

        return value is null ? null : Uri.UnescapeDataString(value);
    }
}
