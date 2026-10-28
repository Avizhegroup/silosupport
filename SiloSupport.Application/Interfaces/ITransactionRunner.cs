namespace SiloSupport.Application.Interfaces;

/// <summary>
/// Unit-of-work helper. The EF implementation opens a transaction on the shared scoped context,
/// commits on success, rolls back on exception and joins an already open transaction.
/// </summary>
public interface ITransactionRunner
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> work);
}
