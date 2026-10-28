using Microsoft.Extensions.Logging;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Service.CustomerSupport.Services;

/// <summary>
/// Processes one incoming Bale message: filters it, identifies the customer member, asks the AI API for a
/// priority, forwards the original text plus the AI analysis to the customer's destination group for that priority
/// and records the forwarded text. It never replies to the originating chat.
/// </summary>
public class CustomerMessageProcessor
{
    private readonly ICustomerGroupRepository _groups;
    private readonly ICustomerMemberRepository _members;
    private readonly ICustomerGroupedMessageRepository _groupedMessages;
    private readonly IBaleBotClient _bale;
    private readonly IMessagePriorityClassifier _classifier;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerMessageProcessor> _logger;

    public CustomerMessageProcessor(
        ICustomerGroupRepository groups,
        ICustomerMemberRepository members,
        ICustomerGroupedMessageRepository groupedMessages,
        IBaleBotClient bale,
        IMessagePriorityClassifier classifier,
        TimeProvider clock,
        ILogger<CustomerMessageProcessor> logger)
    {
        _groups = groups;
        _members = members;
        _groupedMessages = groupedMessages;
        _bale = bale;
        _classifier = classifier;
        _clock = clock;
        _logger = logger;
    }

    public async Task<MessageProcessingOutcome> ProcessAsync(IncomingBaleMessage message, CancellationToken cancellationToken)
    {
        // 1-2. Messages from any configured destination group are ignored: this prevents re-processing our own forwards.
        if (await _groups.BaleIdExistsAsync(message.ChatId, null, cancellationToken))
        {
            _logger.LogInformation(
                "Ignoring Bale message {MessageId}: chat {ChatId} is a configured priority group",
                message.MessageId, message.ChatId);
            return MessageProcessingOutcome.IgnoredPriorityGroup;
        }

        // 3. Ignore messages sent by the bot itself.
        if (message.SenderIsBot || await IsOwnBotAsync(message.SenderId, cancellationToken))
        {
            _logger.LogDebug("Ignoring Bale message {MessageId}: sent by a bot", message.MessageId);
            return MessageProcessingOutcome.IgnoredBotSender;
        }

        // 4. Only ordinary text messages are supported in this phase (no voice, images, files, ...).
        if (message.HasNonTextContent || message.Text is null)
        {
            _logger.LogInformation(
                "Ignoring unsupported Bale message {MessageId} in chat {ChatId}: not an ordinary text message",
                message.MessageId, message.ChatId);
            return MessageProcessingOutcome.IgnoredUnsupportedMessage;
        }

        // 5. Ignore empty or whitespace-only text.
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            _logger.LogDebug("Ignoring Bale message {MessageId}: empty text", message.MessageId);
            return MessageProcessingOutcome.IgnoredEmptyText;
        }

        // 6-8. Identify the sender, find the registered member and resolve the customer.
        if (message.SenderId is null)
        {
            _logger.LogInformation("Ignoring Bale message {MessageId}: no sender id", message.MessageId);
            return MessageProcessingOutcome.IgnoredUnknownMember;
        }

        var member = await _members.FindByBaleIdAsync(message.SenderId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        if (member is null)
        {
            _logger.LogInformation(
                "Ignoring Bale message {MessageId}: sender {SenderBaleId} is not a registered customer member",
                message.MessageId, message.SenderId.Value);
            return MessageProcessingOutcome.IgnoredUnknownMember;
        }

        var customerId = member.CustomerId;
        var customerName = member.Customer?.Name ?? string.Empty;

        // 9-10. Ask the AI API for the priority. Failures are logged and stop processing of this message.
        PriorityClassificationResult classification;
        try
        {
            classification = await _classifier.ClassifyAsync(message.Text, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AI API failure while classifying Bale message {MessageId} of customer {CustomerId}",
                message.MessageId, customerId);
            return MessageProcessingOutcome.ClassificationFailed;
        }

        if (!classification.Succeeded)
        {
            _logger.LogWarning(
                "Invalid or unusable AI result for Bale message {MessageId} of customer {CustomerId}: {Reason}",
                message.MessageId, customerId, classification.Error);
            return MessageProcessingOutcome.ClassificationFailed;
        }

        // 11. Destination group for exactly this customer and priority.
        var destination = await _groups.GetByCustomerAndPriorityAsync(customerId, classification.Priority, cancellationToken);
        if (destination is null)
        {
            _logger.LogWarning(
                "Customer {CustomerId} has no destination group configured for priority {Priority}; message {MessageId} was not forwarded",
                customerId, classification.Priority, message.MessageId);
            return MessageProcessingOutcome.DestinationGroupMissing;
        }

        // 12. Format the original message and the actual AI analysis.
        var text = ForwardedMessageFormatter.Format(new ForwardedMessageContext(
            customerName,
            member.Name,
            member.PhoneNumber,
            classification.Priority,
            message.Text,
            classification.Analysis));

        // 13. Send to the destination group (never to the source chat).
        bool sent;
        try
        {
            sent = await _bale.SendMessageAsync(destination.BaleId, text, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Bale send failed for message {MessageId}: destination group {GroupId} (customer {CustomerId})",
                message.MessageId, destination.Id, customerId);
            return MessageProcessingOutcome.SendFailed;
        }

        if (!sent)
        {
            _logger.LogError(
                "Bale send failed for message {MessageId}: destination group {GroupId} (customer {CustomerId}); nothing was recorded",
                message.MessageId, destination.Id, customerId);
            return MessageProcessingOutcome.SendFailed;
        }

        // 14. Only after a successful send: persist exactly the text that was sent.
        try
        {
            await _groupedMessages.AddAsync(
                new CustomerGroupedMessage
                {
                    GroupId = destination.Id,
                    Message = text,
                    IsResolved = false,
                    CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Database persistence failed after message {MessageId} was sent to group {GroupId} (customer {CustomerId}); the message is in Bale but not in the history",
                message.MessageId, destination.Id, customerId);
            return MessageProcessingOutcome.PersistenceFailed;
        }

        _logger.LogInformation(
            "Forwarded Bale message {MessageId} of customer {CustomerId} to group {GroupId} with priority {Priority}",
            message.MessageId, customerId, destination.Id, classification.Priority);
        return MessageProcessingOutcome.Forwarded;
    }

    private async Task<bool> IsOwnBotAsync(long? senderId, CancellationToken cancellationToken)
    {
        if (senderId is null)
            return false;

        var botId = await _bale.GetBotUserIdAsync(cancellationToken);
        return botId is not null && botId.Value == senderId.Value;
    }
}
