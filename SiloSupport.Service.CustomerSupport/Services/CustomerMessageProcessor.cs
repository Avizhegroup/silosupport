using Microsoft.Extensions.Logging;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Service.CustomerSupport.Services;

/// <summary>
/// Processes one incoming Bale message: filters it (messages of support persons are never customer messages),
/// resolves the customer, asks the AI API for a priority, forwards the original text plus the AI analysis to the customer's destination group for that priority
/// and records the forwarded text. It never replies to the originating chat.
/// </summary>
public class CustomerMessageProcessor
{
    private readonly ICustomerGroupRepository _groups;
    private readonly ICustomerMemberRepository _members;
    private readonly ICustomerRepository _customers;
    private readonly ISupportPersonRepository _supportPersons;
    private readonly ICustomerGroupedMessageRepository _groupedMessages;
    private readonly IBaleBotClient _bale;
    private readonly IMessagePriorityClassifier _classifier;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerMessageProcessor> _logger;

    public CustomerMessageProcessor(
        ICustomerGroupRepository groups,
        ICustomerMemberRepository members,
        ICustomerRepository customers,
        ISupportPersonRepository supportPersons,
        ICustomerGroupedMessageRepository groupedMessages,
        IBaleBotClient bale,
        IMessagePriorityClassifier classifier,
        TimeProvider clock,
        ILogger<CustomerMessageProcessor> logger)
    {
        _groups = groups;
        _members = members;
        _customers = customers;
        _supportPersons = supportPersons;
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

        // 3b. Messages written by our own support persons (recognised by Bale user name) are not customer messages.
        if (await _supportPersons.FindByUsernameAsync(message.SenderUsername, cancellationToken) is not null)
        {
            _logger.LogDebug("Ignoring Bale message {MessageId}: sent by a support person", message.MessageId);
            return MessageProcessingOutcome.IgnoredSupportPerson;
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

        // 6-8. Resolve the customer. Every sender who is not a support person is a customer; the customer is the
        //      registered member's customer (matched by Bale user name or numeric id) or, for senders nobody
        //      registered, the customer whose Bale chat the message came from.
        var sender = await ResolveSenderAsync(message, cancellationToken);
        if (sender is null)
            return MessageProcessingOutcome.IgnoredUnknownMember;

        var customerId = sender.CustomerId;
        var customerName = sender.CustomerName;

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
            sender.SenderName,
            sender.SenderPhoneNumber,
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

    private sealed record ResolvedSender(int CustomerId, string CustomerName, string SenderName, string SenderPhoneNumber);

    private async Task<ResolvedSender?> ResolveSenderAsync(IncomingBaleMessage message, CancellationToken cancellationToken)
    {
        // a) A registered member (user name or numeric id).
        var matches = await _members.FindBySenderAsync(message.SenderId, message.SenderUsername, cancellationToken);
        var (member, ambiguous) = SelectMember(matches, message);
        if (ambiguous)
            return null;

        if (member is not null)
            return new ResolvedSender(member.CustomerId, member.Customer?.Name ?? string.Empty, member.Name, member.PhoneNumber);

        // b) Not registered: the customer owning the chat the message came from.
        var customer = await _customers.GetByBaleChatIdAsync(message.ChatId, cancellationToken);
        var shownId = message.SenderId?.ToString() ?? "-";
        var shownUsername = string.IsNullOrWhiteSpace(message.SenderUsername) ? "-" : "@" + message.SenderUsername.Trim().TrimStart('@');

        if (customer is null)
        {
            _logger.LogInformation(
                "Ignoring Bale message {MessageId}: the sender (id {SenderId}, user name {SenderUsername}) is not a support person or a registered member, " +
                "and chat {ChatId} is not the Bale chat of any customer. Register the member, or set the customer's Bale chat id",
                message.MessageId, shownId, shownUsername, message.ChatId);
            return null;
        }

        var senderName = shownUsername != "-" ? shownUsername : shownId != "-" ? shownId : "ناشناس";
        return new ResolvedSender(customer.Id, customer.Name, senderName, string.Empty);
    }

    /// <summary>
    /// A match by numeric sender id wins over a match by user name (ids never change, user names can). When the best
    /// matches belong to different customers the message is not routed at all: sending a customer's text to another
    /// customer's group would be worse than ignoring it.
    /// </summary>
    private (CustomerMember? Member, bool Ambiguous) SelectMember(IReadOnlyList<CustomerMember> matches, IncomingBaleMessage message)
    {
        if (matches.Count == 0)
            return (null, false);

        var byId = message.SenderId is null
            ? new List<CustomerMember>()
            : matches.Where(m => BaleIdentity.IsNumericIdMatch(m.BaleId, message.SenderId.Value)).ToList();

        var best = byId.Count > 0
            ? byId
            : matches.Where(m => BaleIdentity.IsUsernameMatch(m.BaleId, message.SenderUsername)).ToList();

        if (best.Count == 0)
            return (null, false);

        if (best.Select(m => m.CustomerId).Distinct().Count() > 1)
        {
            _logger.LogWarning(
                "Ignoring Bale message {MessageId}: the sender matches members of several customers ({CustomerIds}); fix the duplicate Bale IDs",
                message.MessageId, string.Join(", ", best.Select(m => m.CustomerId).Distinct()));
            return (null, true);
        }

        return (best.OrderBy(m => m.Id).First(), false);
    }

    private async Task<bool> IsOwnBotAsync(long? senderId, CancellationToken cancellationToken)
    {
        if (senderId is null)
            return false;

        var botId = await _bale.GetBotUserIdAsync(cancellationToken);
        return botId is not null && botId.Value == senderId.Value;
    }
}
