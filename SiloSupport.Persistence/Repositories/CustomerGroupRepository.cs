using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Persistence.Repositories;

public class CustomerGroupRepository : ICustomerGroupRepository
{
    private readonly ApplicationDbContext _db;

    public CustomerGroupRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerGroup>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _db.CustomerGroups
            .AsNoTracking()
            .Where(g => g.CustomerId == customerId)
            .OrderBy(g => g.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<CustomerGroup?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _db.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
    }

    public Task<CustomerGroup?> GetByCustomerAndPriorityAsync(int customerId, GroupPriority priority, CancellationToken cancellationToken = default)
    {
        return _db.CustomerGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.Priority == priority, cancellationToken);
    }

    public Task<bool> BaleIdExistsAsync(long baleId, int? excludeGroupId, CancellationToken cancellationToken = default)
    {
        return _db.CustomerGroups.AnyAsync(
            g => g.BaleId == baleId && (excludeGroupId == null || g.Id != excludeGroupId),
            cancellationToken);
    }

    public Task<bool> PriorityTakenAsync(int customerId, GroupPriority priority, int? excludeGroupId, CancellationToken cancellationToken = default)
    {
        return _db.CustomerGroups.AnyAsync(
            g => g.CustomerId == customerId && g.Priority == priority && (excludeGroupId == null || g.Id != excludeGroupId),
            cancellationToken);
    }

    public Task<bool> HasMessagesAsync(int groupId, CancellationToken cancellationToken = default)
    {
        return _db.CustomerGroupedMessages.AnyAsync(m => m.GroupId == groupId, cancellationToken);
    }

    public async Task AddAsync(CustomerGroup group, CancellationToken cancellationToken = default)
    {
        _db.CustomerGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(group).State = EntityState.Detached;
    }

    public async Task<bool> UpdateAsync(CustomerGroup group, CancellationToken cancellationToken = default)
    {
        var existing = await _db.CustomerGroups.FirstOrDefaultAsync(g => g.Id == group.Id, cancellationToken);
        if (existing is null)
            return false;

        existing.Name = group.Name;
        existing.BaleId = group.BaleId;
        existing.Priority = group.Priority;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(existing).State = EntityState.Detached;
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _db.CustomerGroups.Where(g => g.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }
}
