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
        int recentMessagesPerThread,
        CancellationToken cancellationToken = default)
    {
        // No age and no count limit: an issue is open until it is resolved.
        var threads = await _db.SupportThreads
            .AsNoTracking()
            .Where(t => t.CustomerId == customerId && t.Messages.Any(m => !m.IsResolved))
            .OrderByDescending(t => t.LastActivityUtc)
            .ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Title, t.Summary, t.LastActivityUtc })
            .ToListAsync(cancellationToken);

        if (threads.Count == 0)
            return Array.Empty<ThreadContextDto>();

        var ids = threads.Select(t => t.Id).ToList();

        // How many messages each issue has, and the priorities they received (counted in the database, no texts loaded).
        var stats = await _db.CustomerGroupedMessages
            .AsNoTracking()
            .Where(m => m.ThreadId != null && ids.Contains(m.ThreadId.Value))
            .GroupBy(m => new { m.ThreadId, m.Group!.Priority })
            .Select(g => new { ThreadId = g.Key.ThreadId!.Value, g.Key.Priority, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Only the newest few customer texts of every issue: a message is kept when fewer than N newer ones exist.
        var recent = recentMessagesPerThread <= 0
            ? new List<RecentRow>()
            : await _db.CustomerGroupedMessages
                .AsNoTracking()
                .Where(m => m.ThreadId != null && ids.Contains(m.ThreadId.Value) && m.OriginalText != null
                            && _db.CustomerGroupedMessages.Count(x => x.ThreadId == m.ThreadId && x.OriginalText != null
                                && (x.CreatedAtUtc > m.CreatedAtUtc || (x.CreatedAtUtc == m.CreatedAtUtc && x.Id > m.Id))) < recentMessagesPerThread)
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Id)
                .Select(m => new RecentRow(m.ThreadId!.Value, m.OriginalText!, m.Group!.Priority, m.CreatedAtUtc))
                .ToListAsync(cancellationToken);

        return threads
            .Select(t =>
            {
                var own = stats.Where(s => s.ThreadId == t.Id).ToList();
                return new ThreadContextDto(
                    t.Id,
                    t.Title,
                    t.Summary,
                    DateTime.SpecifyKind(t.LastActivityUtc, DateTimeKind.Utc),
                    own.Sum(s => s.Count),
                    own.Count == 0 ? Domain.Enums.GroupPriority.Low : own.Max(s => s.Priority),
                    recent
                        .Where(r => r.ThreadId == t.Id)
                        .Select(r => new ThreadContextMessageDto(r.Text, r.Priority, DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc)))
                        .ToList());
            })
            .ToList();
    }

    public async Task UpdateSummaryAsync(int threadId, string summary, CancellationToken cancellationToken = default)
    {
        await _db.SupportThreads
            .Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Summary, summary), cancellationToken);
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

    private sealed record RecentRow(int ThreadId, string Text, Domain.Enums.GroupPriority Priority, DateTime CreatedAtUtc);
}
