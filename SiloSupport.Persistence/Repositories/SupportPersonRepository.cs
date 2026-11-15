using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Repositories;

public class SupportPersonRepository : ISupportPersonRepository
{
    private readonly ApplicationDbContext _db;

    public SupportPersonRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SupportPerson>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.SupportPersons.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public Task<SupportPerson?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _db.SupportPersons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task<SupportPerson?> FindByUsernameAsync(string? username, CancellationToken cancellationToken = default)
    {
        var normalized = BaleIdentity.Normalize(username);
        if (normalized.Length == 0)
            return Task.FromResult<SupportPerson?>(null);

        // Usernames are stored normalized, so an exact comparison is enough.
        return _db.SupportPersons.AsNoTracking().FirstOrDefaultAsync(s => s.BaleUsername == normalized, cancellationToken);
    }

    public Task<bool> UsernameExistsAsync(string username, int? excludeId, CancellationToken cancellationToken = default)
    {
        var normalized = BaleIdentity.Normalize(username);
        return _db.SupportPersons.AnyAsync(
            s => s.BaleUsername == normalized && (excludeId == null || s.Id != excludeId),
            cancellationToken);
    }

    public async Task AddAsync(SupportPerson person, CancellationToken cancellationToken = default)
    {
        _db.SupportPersons.Add(person);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(person).State = EntityState.Detached;
    }

    public async Task<bool> UpdateAsync(SupportPerson person, CancellationToken cancellationToken = default)
    {
        var existing = await _db.SupportPersons.FirstOrDefaultAsync(s => s.Id == person.Id, cancellationToken);
        if (existing is null)
            return false;

        existing.Name = person.Name;
        existing.PhoneNumber = person.PhoneNumber;
        existing.BaleUsername = person.BaleUsername;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(existing).State = EntityState.Detached;
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _db.SupportPersons.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }
}
