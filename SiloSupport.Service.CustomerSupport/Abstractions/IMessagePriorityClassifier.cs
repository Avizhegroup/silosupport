using SiloSupport.Application.Dtos;

namespace SiloSupport.Service.CustomerSupport.Abstractions;

/// <summary>Port that asks the existing Silo AI API to classify a customer message.</summary>
public interface IMessagePriorityClassifier
{
    /// <summary>
    /// Classifies the message together with the customer's recent open issues, so a follow-up is judged with its
    /// history and the AI can say which issue it continues. Never throws for AI/API problems (except cancellation);
    /// returns a failed result instead.
    /// </summary>
    Task<PriorityClassificationResult> ClassifyAsync(
        string customerMessage,
        IReadOnlyList<ThreadContextDto> openThreads,
        CancellationToken cancellationToken);
}
