using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Repositories;

public class CustomerMemberRepository : ICustomerMemberRepository
{
    private readonly ApplicationDbContext _db;

    public CustomerMemberRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerMember>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _db.CustomerMembers
            .AsNoTracking()
            .Where(m => m.CustomerId == customerId)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<CustomerMember?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _db.CustomerMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public Task<CustomerMember?> FindByBaleIdAsync(string baleId, CancellationToken cancellationToken = default)
    {
        return _db.CustomerMembers
            .AsNoTracking()
            .Include(m => m.Customer)
            .FirstOrDefaultAsync(m => m.BaleId == baleId, cancellationToken);
    }

    public Task<bool> BaleIdExistsAsync(string baleId, int? excludeMemberId, CancellationToken cancellationToken = default)
    {
        return _db.CustomerMembers.AnyAsync(
            m => m.BaleId == baleId && (excludeMemberId == null || m.Id != excludeMemberId),
            cancellationToken);
    }

    public async Task AddAsync(CustomerMember member, CancellationToken cancellationToken = default)
    {
        _db.CustomerMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(member).State = EntityState.Detached;
    }

    public async Task<bool> UpdateAsync(CustomerMember member, CancellationToken cancellationToken = default)
    {
        var existing = await _db.CustomerMembers.FirstOrDefaultAsync(m => m.Id == member.Id, cancellationToken);
        if (existing is null)
            return false;

        existing.Name = member.Name;
        existing.Description = member.Description;
        existing.PhoneNumber = member.PhoneNumber;
        existing.BaleId = member.BaleId;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(existing).State = EntityState.Detached;
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _db.CustomerMembers.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }
}
