using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly ApplicationDbContext _db;

    public CustomerRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerSummary>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                MemberCount = c.Members.Count,
                Priorities = c.Groups.Select(g => g.Priority).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CustomerSummary(r.Id, r.Name, r.Description, r.MemberCount, r.Priorities))
            .ToList();
    }

    public Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(customer).State = EntityState.Detached;
    }

    public async Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customer.Id, cancellationToken);
        if (existing is null)
            return false;

        existing.Name = customer.Name;
        existing.Description = customer.Description;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(existing).State = EntityState.Detached;
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Customers.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }

    public Task<bool> HasMessageHistoryAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _db.CustomerGroupedMessages.AnyAsync(m => m.Group!.CustomerId == customerId, cancellationToken);
    }
}
