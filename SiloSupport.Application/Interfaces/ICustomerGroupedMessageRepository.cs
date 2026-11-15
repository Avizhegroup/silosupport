using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for forwarded-message history.</summary>
public interface ICustomerGroupedMessageRepository
{
    /// <summary>
    /// Stores the message. When <see cref="CustomerGroupedMessage.Thread"/> is set the new thread is stored with it;
    /// when only <see cref="CustomerGroupedMessage.ThreadId"/> is set the thread's last activity is moved forward.
    /// A <see cref="CustomerGroupedMessage.Attachment"/> is stored in the same save.
    /// </summary>
    Task AddAsync(CustomerGroupedMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads only the messages of the given customer (filtered in the database), oldest first.
    /// </summary>
    Task<IReadOnlyList<GroupedMessageDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves or re-opens every message of a thread that belongs to the given customer. False when no such thread exists.
    /// </summary>
    Task<bool> SetThreadResolvedAsync(int customerId, int threadId, bool isResolved, CancellationToken cancellationToken = default);

    /// <summary>The stored bytes of an attachment, or null when it does not exist.</summary>
    Task<AttachmentContentDto?> GetAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default);

    /// <summary>All-time message counts per customer, priority and resolution state (aggregated in the database).</summary>
    Task<IReadOnlyList<MessageCountRow>> GetCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creation time and priority of every message created at or after <paramref name="sinceUtc"/>.</summary>
    Task<IReadOnlyList<MessageTimeRow>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the resolution flag of a message that belongs to the given customer.
    /// Returns false when no such message exists for that customer.
    /// </summary>
    Task<bool> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default);
}
