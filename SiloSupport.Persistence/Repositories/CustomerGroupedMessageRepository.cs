using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Repositories;

public class CustomerGroupedMessageRepository : ICustomerGroupedMessageRepository
{
    private readonly ApplicationDbContext _db;

    public CustomerGroupedMessageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(CustomerGroupedMessage message, CancellationToken cancellationToken = default)
    {
        _db.CustomerGroupedMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);

        // The message joined an existing thread: move the thread's last activity forward. (A new thread is stored
        // with its message and already has the right time.)
        if (message.Thread is null && message.ThreadId is int threadId)
        {
            var at = message.CreatedAtUtc;
            await _db.SupportThreads
                .Where(t => t.Id == threadId && t.LastActivityUtc < at)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.LastActivityUtc, at), cancellationToken);
        }

        _db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<GroupedMessageDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        // Filtered in the database by the owning customer; other customers' history is never loaded.
        var rows = await _db.CustomerGroupedMessages
            .AsNoTracking()
            .Where(m => m.Group!.CustomerId == customerId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.GroupId, m.Message, m.OriginalText, m.CreatedAtUtc, m.IsResolved, m.ThreadId })
            .ToListAsync(cancellationToken);

        // Attachment metadata only: the file bytes are never loaded for the list (HasContent is computed by the database).
        var attachments = (await _db.MessageAttachments
                .AsNoTracking()
                .Where(a => a.GroupedMessage!.Group!.CustomerId == customerId)
                .Select(a => new
                {
                    a.Id,
                    a.GroupedMessageId,
                    a.Kind,
                    a.FileName,
                    a.MimeType,
                    a.SizeBytes,
                    HasContent = a.Content != null
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.GroupedMessageId);

        return rows
            .Select(r => new GroupedMessageDto(
                r.Id,
                r.GroupId,
                r.Message,
                DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc),
                r.IsResolved,
                r.ThreadId,
                attachments.TryGetValue(r.Id, out var a)
                    ? new AttachmentInfoDto(a.Id, a.Kind, a.FileName, a.MimeType, a.SizeBytes, a.HasContent)
                    : null,
                r.OriginalText))
            .ToList();
    }

    public async Task<IReadOnlyList<MessageCountRow>> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.CustomerGroupedMessages
            .AsNoTracking()
            .GroupBy(m => new { m.Group!.CustomerId, m.Group.Priority, m.IsResolved })
            .Select(g => new { g.Key.CustomerId, g.Key.Priority, g.Key.IsResolved, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new MessageCountRow(r.CustomerId, r.Priority, r.IsResolved, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<MessageTimeRow>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _db.CustomerGroupedMessages
            .AsNoTracking()
            .Where(m => m.CreatedAtUtc >= sinceUtc)
            .Select(m => new { m.CreatedAtUtc, m.Group!.Priority })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new MessageTimeRow(DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc), r.Priority))
            .ToList();
    }

    public async Task<bool> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default)
    {
        var message = await _db.CustomerGroupedMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.Group!.CustomerId == customerId, cancellationToken);

        if (message is null)
            return false;

        message.IsResolved = isResolved;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(message).State = EntityState.Detached;
        return true;
    }

    public async Task<bool> SetThreadResolvedAsync(int customerId, int threadId, bool isResolved, CancellationToken cancellationToken = default)
    {
        var exists = await _db.SupportThreads.AnyAsync(t => t.Id == threadId && t.CustomerId == customerId, cancellationToken);
        if (!exists)
            return false;

        await _db.CustomerGroupedMessages
            .Where(m => m.ThreadId == threadId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.IsResolved, isResolved), cancellationToken);

        return true;
    }

    public Task<AttachmentContentDto?> GetAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        return _db.MessageAttachments
            .AsNoTracking()
            .Where(a => a.Id == attachmentId)
            .Select(a => new AttachmentContentDto(a.Id, a.Kind, a.FileName, a.MimeType, a.Content))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
