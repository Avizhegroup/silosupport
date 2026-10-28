using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Interfaces;

namespace SiloSupport.Persistence;

/// <summary>
/// Unit of work on the shared scoped <see cref="ApplicationDbContext"/>: commits on success, rolls back on
/// exception and joins a transaction that is already open (re-entrant).
/// </summary>
public class EfTransactionRunner : ITransactionRunner
{
    private readonly ApplicationDbContext _db;

    public EfTransactionRunner(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work)
    {
        if (_db.Database.CurrentTransaction is not null)
            return await work();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var result = await work();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
