using SiloSupport.Application.Dtos;

namespace SiloSupport.Service.CustomerSupport.Abstractions;

/// <summary>Port that asks the existing Silo AI API to classify a customer message.</summary>
public interface IMessagePriorityClassifier
{
    /// <summary>Never throws for AI/API problems (except cancellation); returns a failed result instead.</summary>
    Task<PriorityClassificationResult> ClassifyAsync(string customerMessage, CancellationToken cancellationToken);
}
