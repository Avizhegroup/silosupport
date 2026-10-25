namespace SiloSupport.Application;

/// <summary>Client for the Silo AI RAG chat API.</summary>
public interface ISiloAiClient
{
    /// <summary>Starts a brand-new RAG conversation and returns the resulting conversation id, or null on failure.</summary>
    Task<Guid?> StartNewSessionAsync(RagDocType docType, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a message to the Silo AI RAG chat endpoint and returns the answer and updated conversation id.
    /// <paramref name="docType"/> overrides the configured default document type for this call, when provided.
    /// </summary>
    Task<RagChatResponse?> SendAsync(Guid? conversationId, string message, RagDocType docType, CancellationToken cancellationToken);

    /// <summary>Sends recorded user speech to POST /api/rag/chat/voice and returns the spoken answer.</summary>
    Task<RagVoiceResponse?> SendVoiceAsync(Guid? conversationId, byte[] audio, string fileName, string contentType, RagDocType docType, CancellationToken cancellationToken);
}
