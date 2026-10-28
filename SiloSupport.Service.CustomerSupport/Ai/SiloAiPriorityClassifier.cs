using Microsoft.Extensions.Logging;
using SiloSupport.Application;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;

namespace SiloSupport.Service.CustomerSupport.Ai;

/// <summary>
/// Classifies a customer message with the existing Silo AI RAG chat API (<see cref="ISiloAiClient"/>, POST
/// /api/rag/chat/send). Each message is sent without a conversation id so conversations of different customers are
/// never mixed. The documented API returns free text, so the model is asked for strict JSON which is then validated.
/// </summary>
public class SiloAiPriorityClassifier : IMessagePriorityClassifier
{
    private readonly ISiloAiClient _ai;
    private readonly ILogger<SiloAiPriorityClassifier> _logger;

    public SiloAiPriorityClassifier(ISiloAiClient ai, ILogger<SiloAiPriorityClassifier> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<PriorityClassificationResult> ClassifyAsync(string customerMessage, CancellationToken cancellationToken)
    {
        RagChatResponse? response;
        try
        {
            response = await _ai.SendAsync(
                conversationId: null,
                PriorityClassificationPrompt.Build(customerMessage),
                RagDocType.GeneralChat,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Silo AI API call failed during priority classification");
            return PriorityClassificationResult.Failure("The AI API call failed.");
        }

        if (response is null)
            return PriorityClassificationResult.Failure("The AI API returned no response.");

        // SiloAiClient only sets StatusCode when the HTTP call was not successful.
        if (response.StatusCode is not null)
        {
            _logger.LogWarning("Silo AI API returned status {StatusCode} during priority classification", (int)response.StatusCode.Value);
            return PriorityClassificationResult.Failure($"The AI API returned HTTP {(int)response.StatusCode.Value}.");
        }

        if (!AiPriorityResultParser.TryParse(response.ResponseText, out var parsed, out var error))
            return PriorityClassificationResult.Failure(error ?? "The AI response could not be parsed.");

        return PriorityClassificationResult.Success(parsed!.Priority, parsed.Analysis);
    }
}
