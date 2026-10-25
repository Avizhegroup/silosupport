using System.Net;

namespace SiloSupport.Application;

/// <summary>Response of POST /api/rag/chat/voice: spoken answer audio plus text data from response headers.</summary>
public class RagVoiceResponse
{
    public byte[]? Audio { get; set; }

    public string ContentType { get; set; } = "audio/mpeg";

    public string? Transcript { get; set; }

    public string? ResponseText { get; set; }

    public Guid? ConversationId { get; set; }

    public HttpStatusCode? StatusCode { get; set; }
}
