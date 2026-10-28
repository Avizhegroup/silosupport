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
        _db.Entry(message).State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<GroupedMessageDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        // Filtered in the database by the owning customer; other customers' history is never loaded.
        var rows = await _db.CustomerGroupedMessages
            .AsNoTracking()
            .Where(m => m.Group!.CustomerId == customerId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.GroupId, m.Message, m.CreatedAtUtc, m.IsResolved })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new GroupedMessageDto(
                r.Id,
                r.GroupId,
                r.Message,
                DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc),
                r.IsResolved))
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
}
