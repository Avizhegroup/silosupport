using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Services;

/// <summary>
/// Processes one incoming Bale message: filters it (messages of support persons are never customer messages),
/// resolves the customer, asks the AI API for a priority (together with the customer's recent open issues, so follow-ups
/// are judged with their history and attached to the right issue), forwards the original text plus the AI analysis - and
/// the photo, video, voice message, audio file or document the customer sent - to the customer's destination group for that
/// priority, and records the forwarded text, the issue and the stored file. It never replies to the originating chat.
/// </summary>
public class CustomerMessageProcessor
{
    private readonly ICustomerGroupRepository _groups;
    private readonly ICustomerMemberRepository _members;
    private readonly ICustomerRepository _customers;
    private readonly ISupportPersonRepository _supportPersons;
    private readonly ISupportThreadRepository _threads;
    private readonly ICustomerGroupedMessageRepository _groupedMessages;
    private readonly IBaleBotClient _bale;
    private readonly IMessagePriorityClassifier _classifier;
    private readonly BaleBotOptions _baleOptions;
    private readonly ThreadingOptions _threadingOptions;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerMessageProcessor> _logger;

    public CustomerMessageProcessor(
        ICustomerGroupRepository groups,
        ICustomerMemberRepository members,
        ICustomerRepository customers,
        ISupportPersonRepository supportPersons,
        ISupportThreadRepository threads,
        ICustomerGroupedMessageRepository groupedMessages,
        IBaleBotClient bale,
        IMessagePriorityClassifier classifier,
        IOptions<BaleBotOptions> baleOptions,
        IOptions<ThreadingOptions> threadingOptions,
        TimeProvider clock,
        ILogger<CustomerMessageProcessor> logger)
    {
        _groups = groups;
        _members = members;
        _customers = customers;
        _supportPersons = supportPersons;
        _threads = threads;
        _groupedMessages = groupedMessages;
        _bale = bale;
        _classifier = classifier;
        _baleOptions = baleOptions.Value;
        _threadingOptions = threadingOptions.Value;
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

        // 4. Ordinary text messages and files (photo, video, voice, audio, document, animation) are supported.
        //    Anything else (sticker, contact, location, invoice, ...) is ignored.
        var attachment = message.Attachment;
        if (attachment is null && (message.HasNonTextContent || message.Text is null))
        {
            _logger.LogInformation(
                "Ignoring unsupported Bale message {MessageId} in chat {ChatId}: neither text nor a supported file",
                message.MessageId, message.ChatId);
            return MessageProcessingOutcome.IgnoredUnsupportedMessage;
        }

        // 5. Ignore empty or whitespace-only text (a file without a caption is fine).
        var hasText = !string.IsNullOrWhiteSpace(message.Text);
        if (attachment is null && !hasText)
        {
            _logger.LogDebug("Ignoring Bale message {MessageId}: empty text", message.MessageId);
            return MessageProcessingOutcome.IgnoredEmptyText;
        }

        var customerText = hasText ? message.Text!.Trim() : null;

        // 6-8. Resolve the customer. Every sender who is not a support person is a customer; the customer is the
        //      registered member's customer (matched by Bale user name or numeric id) or, for senders nobody
        //      registered, the customer whose Bale chat the message came from.
        var sender = await ResolveSenderAsync(message, cancellationToken);
        if (sender is null)
            return MessageProcessingOutcome.IgnoredUnknownMember;

        var customerId = sender.CustomerId;
        var customerName = sender.CustomerName;

        // 9. The customer's recent open issues, so a follow-up is judged with its history. A failure here only costs the
        //    context; the message is still classified (as a new issue).
        var openThreads = await LoadOpenThreadsAsync(customerId, cancellationToken);

        // 10. Ask the AI API for the priority and the issue. Failures are logged and stop processing of this message.
        //     A file without text cannot be read by the AI, so it is described instead.
        var classifyText = customerText ?? DescribeFileForAi(attachment!.Kind);

        PriorityClassificationResult classification;
        try
        {
            classification = await _classifier.ClassifyAsync(classifyText, openThreads, cancellationToken);
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

        var priority = classification.Priority;
        if (customerText is null && priority < GroupPriority.Medium)
        {
            // The content of a file sent without any text is unknown, so it is never filed as Low.
            priority = GroupPriority.Medium;
        }

        // 11. Destination group for exactly this customer and priority.
        var destination = await _groups.GetByCustomerAndPriorityAsync(customerId, priority, cancellationToken);
        if (destination is null)
        {
            _logger.LogWarning(
                "Customer {CustomerId} has no destination group configured for priority {Priority}; message {MessageId} was not forwarded",
                customerId, priority, message.MessageId);
            return MessageProcessingOutcome.DestinationGroupMissing;
        }

        // 12. Which issue does the message belong to: an open one the AI named (only ids that were offered count), else a new one.
        var now = _clock.GetUtcNow().UtcDateTime;
        var continued = classification.ThreadId is int requestedId
            ? openThreads.FirstOrDefault(t => t.ThreadId == requestedId)
            : null;

        if (classification.ThreadId is not null && continued is null)
        {
            _logger.LogWarning(
                "The AI linked Bale message {MessageId} to issue {ThreadId}, which was not offered; it starts a new issue",
                message.MessageId, classification.ThreadId);
        }

        SupportThread? newThread = null;
        string threadTitle;
        if (continued is not null)
        {
            threadTitle = continued.Title;
        }
        else
        {
            threadTitle = Truncate(
                !string.IsNullOrWhiteSpace(classification.ThreadTitle)
                    ? classification.ThreadTitle!
                    : customerText is not null ? Excerpt(customerText) : ForwardedMessageFormatter.Label(attachment!.Kind) + " (بدون متن)",
                200);
            newThread = new SupportThread { CustomerId = customerId, Title = threadTitle, CreatedAtUtc = now, LastActivityUtc = now };
        }

        // 13. Format the original message and the actual AI analysis.
        var text = ForwardedMessageFormatter.Format(new ForwardedMessageContext(
            customerName,
            sender.SenderName,
            sender.SenderPhoneNumber,
            priority,
            customerText ?? string.Empty,
            classification.Analysis,
            threadTitle,
            continued is not null,
            attachment?.Kind,
            attachment?.FileName));

        // 14. Send to the destination group (never to the source chat): the file with the text as its caption, or just the text.
        bool sent;
        try
        {
            sent = attachment is null
                ? await _bale.SendMessageAsync(destination.BaleId, text, cancellationToken)
                : await SendWithFileAsync(destination.BaleId, attachment, text, cancellationToken);
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

        // 15. Keep the file for the Web panel. A download problem never loses the message: the file was already forwarded by its Bale id.
        var storedAttachment = attachment is null ? null : await BuildAttachmentAsync(attachment, cancellationToken);

        // 16. Only after a successful send: persist exactly the text that was sent (with the issue and the file).
        try
        {
            await _groupedMessages.AddAsync(
                new CustomerGroupedMessage
                {
                    GroupId = destination.Id,
                    Message = text,
                    OriginalText = customerText is null ? null : Truncate(customerText, 4000),
                    IsResolved = false,
                    CreatedAtUtc = now,
                    ThreadId = continued?.ThreadId,
                    Thread = newThread,
                    Attachment = storedAttachment
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
            "Forwarded Bale message {MessageId} of customer {CustomerId} to group {GroupId} with priority {Priority} ({Issue}{File})",
            message.MessageId, customerId, destination.Id, priority,
            continued is not null ? $"continues issue {continued.ThreadId}" : "new issue",
            attachment is null ? string.Empty : $", {attachment.Kind}{(storedAttachment?.Content is null ? " not stored" : " stored")}");
        return MessageProcessingOutcome.Forwarded;
    }

    private async Task<IReadOnlyList<ThreadContextDto>> LoadOpenThreadsAsync(int customerId, CancellationToken cancellationToken)
    {
        if (_threadingOptions.MaxThreadsInContext <= 0 || _threadingOptions.MessagesPerThread <= 0)
            return Array.Empty<ThreadContextDto>();

        try
        {
            var since = _clock.GetUtcNow().UtcDateTime.AddHours(-Math.Max(1, _threadingOptions.OpenWindowHours));
            return await _threads.GetOpenContextAsync(
                customerId, since, _threadingOptions.MaxThreadsInContext, _threadingOptions.MessagesPerThread, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading the open issues of customer {CustomerId} failed; the message is classified without history", customerId);
            return Array.Empty<ThreadContextDto>();
        }
    }

    /// <summary>
    /// Sends the file to the group. When Bale refuses the file, the text still goes out so support does not miss the
    /// message; the file is then only available in the Web panel when it could be downloaded.
    /// </summary>
    private async Task<bool> SendWithFileAsync(long chatId, IncomingAttachment attachment, string text, CancellationToken cancellationToken)
    {
        if (await _bale.SendAttachmentAsync(chatId, attachment.Kind, attachment.FileId, text, cancellationToken))
            return true;

        _logger.LogWarning("Bale refused the {Kind} file; sending the text only", attachment.Kind);
        return await _bale.SendMessageAsync(chatId, text + "\n\n⚠️ ارسال پیوست در بله ناموفق بود.", cancellationToken);
    }

    private async Task<MessageAttachment> BuildAttachmentAsync(IncomingAttachment attachment, CancellationToken cancellationToken)
    {
        byte[]? content = null;
        var limit = _baleOptions.MaxDownloadBytes;

        if (attachment.FileSize is long size && size > limit)
        {
            _logger.LogInformation("The {Kind} file is {Size} bytes, over the {Limit}-byte limit; it is forwarded but not stored", attachment.Kind, size, limit);
        }
        else
        {
            try
            {
                content = (await _bale.DownloadFileAsync(attachment.FileId, limit, cancellationToken))?.Content;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Downloading the {Kind} file failed; it is forwarded but not stored", attachment.Kind);
            }
        }

        return new MessageAttachment
        {
            Kind = attachment.Kind,
            FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? null : Truncate(attachment.FileName.Trim(), 260),
            MimeType = Truncate(AttachmentMime.Resolve(attachment.Kind, attachment.MimeType), 128),
            SizeBytes = content?.LongLength ?? attachment.FileSize,
            BaleFileId = Truncate(attachment.FileId, 512),
            BaleFileUniqueId = string.IsNullOrWhiteSpace(attachment.FileUniqueId) ? null : Truncate(attachment.FileUniqueId, 128),
            Content = content
        };
    }

    /// <summary>What the AI is told when the customer sent a file without any text.</summary>
    private static string DescribeFileForAi(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo => "[The customer sent a photo without any text.]",
        AttachmentKind.Video => "[The customer sent a video without any text.]",
        AttachmentKind.Voice => "[The customer sent a voice message without any text.]",
        AttachmentKind.Audio => "[The customer sent an audio file without any text.]",
        AttachmentKind.Animation => "[The customer sent an animation without any text.]",
        _ => "[The customer sent a file without any text.]"
    };

    private static string Excerpt(string text)
    {
        var oneLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Truncate(oneLine, 60);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max - 1) + "…";

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
