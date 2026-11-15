using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Repositories;

public class SupportThreadRepository : ISupportThreadRepository
{
    private readonly ApplicationDbContext _db;

    public SupportThreadRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ThreadContextDto>> GetOpenContextAsync(
        int customerId,
        DateTime sinceUtc,
        int maxThreads,
        int messagesPerThread,
        CancellationToken cancellationToken = default)
    {
        if (maxThreads <= 0 || messagesPerThread <= 0)
            return Array.Empty<ThreadContextDto>();

        var threads = await _db.SupportThreads
            .AsNoTracking()
            .Where(t => t.CustomerId == customerId
                        && t.LastActivityUtc >= sinceUtc
                        && t.Messages.Any(m => !m.IsResolved))
            .OrderByDescending(t => t.LastActivityUtc)
            .ThenByDescending(t => t.Id)
            .Take(maxThreads)
            .Select(t => new { t.Id, t.Title, t.LastActivityUtc })
            .ToListAsync(cancellationToken);

        if (threads.Count == 0)
            return Array.Empty<ThreadContextDto>();

        var ids = threads.Select(t => t.Id).ToList();

        var messages = await _db.CustomerGroupedMessages
            .AsNoTracking()
            .Where(m => m.ThreadId != null && ids.Contains(m.ThreadId.Value) && m.OriginalText != null)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => new { ThreadId = m.ThreadId!.Value, m.OriginalText, m.Group!.Priority, m.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return threads
            .Select(t => new ThreadContextDto(
                t.Id,
                t.Title,
                DateTime.SpecifyKind(t.LastActivityUtc, DateTimeKind.Utc),
                messages
                    .Where(m => m.ThreadId == t.Id)
                    .TakeLast(messagesPerThread)
                    .Select(m => new ThreadContextMessageDto(m.OriginalText!, m.Priority, DateTime.SpecifyKind(m.CreatedAtUtc, DateTimeKind.Utc)))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<SupportThread>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _db.SupportThreads
            .AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.LastActivityUtc)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);
    }
}
