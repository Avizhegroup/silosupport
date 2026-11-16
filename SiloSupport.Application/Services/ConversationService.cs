using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;

namespace SiloSupport.Application.Services;

/// <summary>Use cases behind the customer conversations page.</summary>
public class ConversationService
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerGroupRepository _groups;
    private readonly ICustomerGroupedMessageRepository _messages;
    private readonly ISupportThreadRepository _threads;

    public ConversationService(
        ICustomerRepository customers,
        ICustomerGroupRepository groups,
        ICustomerGroupedMessageRepository messages,
        ISupportThreadRepository threads)
    {
        _customers = customers;
        _groups = groups;
        _messages = messages;
        _threads = threads;
    }

    /// <summary>
    /// Loads one customer's destination groups and only that customer's messages, grouped by group (most urgent
    /// group first, messages oldest first). Returns null when the customer does not exist.
    /// </summary>
    public async Task<CustomerConversationDto?> GetAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
            return null;

        var groups = await _groups.GetByCustomerAsync(customerId, cancellationToken);
        var messages = await _messages.GetByCustomerAsync(customerId, cancellationToken);

        var byGroup = messages
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GroupedMessageDto>)g.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).ToList());

        var groupDtos = groups
            .OrderByDescending(g => (int)g.Priority)
            .Select(g => new GroupConversationDto(
                g.Id,
                g.Name,
                g.Priority,
                byGroup.TryGetValue(g.Id, out var list) ? list : Array.Empty<GroupedMessageDto>()))
            .ToList();

        // The same messages again, but grouped by issue (thread) instead of by destination group.
        var groupsById = groups.ToDictionary(g => g.Id);
        ThreadMessageDto ToThreadMessage(GroupedMessageDto m) => new(
            m.Id,
            m.GroupId,
            groupsById.TryGetValue(m.GroupId, out var g) ? g.Name : string.Empty,
            groupsById.TryGetValue(m.GroupId, out var gp) ? gp.Priority : default,
            m.Message,
            m.CreatedAtUtc,
            m.IsResolved,
            m.Attachment,
            m.OriginalText);

        var threadRows = await _threads.GetByCustomerAsync(customerId, cancellationToken);
        var threadDtos = threadRows
            .Select(t => new ThreadDto(
                t.Id,
                t.Title,
                t.Summary,
                DateTime.SpecifyKind(t.CreatedAtUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(t.LastActivityUtc, DateTimeKind.Utc),
                messages.Where(m => m.ThreadId == t.Id)
                    .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
                    .Select(ToThreadMessage)
                    .ToList()))
            // Open issues first (they need attention), each block newest activity first.
            .OrderByDescending(t => t.IsOpen)
            .ThenByDescending(t => t.LastActivityUtc)
            .ThenByDescending(t => t.Id)
            .ToList();

        var legacy = messages
            .Where(m => m.ThreadId is null)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .Select(ToThreadMessage)
            .ToList();

        return new CustomerConversationDto(
            customer.Id,
            customer.Name,
            groupDtos,
            PriorityHelper.Missing(groups.Select(g => g.Priority)),
            threadDtos,
            legacy);
    }

    /// <summary>Resolves or re-opens every message of one issue of the given customer.</summary>
    public async Task<OperationResult> SetThreadResolvedAsync(int customerId, int threadId, bool isResolved, CancellationToken cancellationToken = default)
    {
        var updated = await _messages.SetThreadResolvedAsync(customerId, threadId, isResolved, cancellationToken);
        return updated ? OperationResult.Success() : OperationResult.Failure("موضوع برای این مشتری یافت نشد.");
    }

    /// <summary>The stored file of a message, or null when it does not exist or could not be downloaded.</summary>
    public async Task<AttachmentContentDto?> GetAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _messages.GetAttachmentAsync(attachmentId, cancellationToken);
        return attachment is { Content: not null } ? attachment : null;
    }

    /// <summary>Marks a message of the given customer as resolved or unresolved and persists it.</summary>
    public async Task<OperationResult> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default)
    {
        var updated = await _messages.SetResolvedAsync(customerId, messageId, isResolved, cancellationToken);
        return updated ? OperationResult.Success() : OperationResult.Failure("پیام برای این مشتری یافت نشد.");
    }
}
