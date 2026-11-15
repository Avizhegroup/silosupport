using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for <see cref="SupportPerson"/>.</summary>
public interface ISupportPersonRepository
{
    Task<IReadOnlyList<SupportPerson>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SupportPerson?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The support person with this Bale user name (ignoring case and a leading '@'), or null when the value is empty
    /// or nobody has it.
    /// </summary>
    Task<SupportPerson?> FindByUsernameAsync(string? username, CancellationToken cancellationToken = default);

    /// <summary>True when another support person (ignoring <paramref name="excludeId"/>) has the Bale user name.</summary>
    Task<bool> UsernameExistsAsync(string username, int? excludeId, CancellationToken cancellationToken = default);

    Task AddAsync(SupportPerson person, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(SupportPerson person, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
