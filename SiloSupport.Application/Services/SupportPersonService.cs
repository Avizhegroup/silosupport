using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Services;

/// <summary>Use cases for managing the support persons (our own team, shared by all customers).</summary>
public class SupportPersonService
{
    private readonly ISupportPersonRepository _persons;

    public SupportPersonService(ISupportPersonRepository persons)
    {
        _persons = persons;
    }

    public async Task<IReadOnlyList<SupportPersonDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var persons = await _persons.GetAllAsync(cancellationToken);
        return persons.Select(ToDto).ToList();
    }

    public async Task<SupportPersonDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var person = await _persons.GetByIdAsync(id, cancellationToken);
        return person is null ? null : ToDto(person);
    }

    public async Task<OperationResult<int>> CreateAsync(SupportPersonInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult<int>.Failure(errors);

        var username = BaleIdentity.Normalize(input.BaleUsername);
        if (await _persons.UsernameExistsAsync(username, null, cancellationToken))
            return OperationResult<int>.Failure("عضو دیگری از تیم پشتیبانی با این نام کاربری بله ثبت شده است.");

        var person = new SupportPerson
        {
            Name = input.Name.Trim(),
            PhoneNumber = input.PhoneNumber.Trim(),
            BaleUsername = username
        };

        await _persons.AddAsync(person, cancellationToken);
        return OperationResult<int>.Success(person.Id);
    }

    public async Task<OperationResult> UpdateAsync(int id, SupportPersonInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult.Failure(errors);

        if (await _persons.GetByIdAsync(id, cancellationToken) is null)
            return OperationResult.Failure("عضو تیم پشتیبانی دیگر وجود ندارد.");

        var username = BaleIdentity.Normalize(input.BaleUsername);
        if (await _persons.UsernameExistsAsync(username, id, cancellationToken))
            return OperationResult.Failure("عضو دیگری از تیم پشتیبانی با این نام کاربری بله ثبت شده است.");

        var updated = await _persons.UpdateAsync(
            new SupportPerson { Id = id, Name = input.Name.Trim(), PhoneNumber = input.PhoneNumber.Trim(), BaleUsername = username },
            cancellationToken);

        return updated ? OperationResult.Success() : OperationResult.Failure("عضو تیم پشتیبانی دیگر وجود ندارد.");
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _persons.DeleteAsync(id, cancellationToken);
        return deleted ? OperationResult.Success() : OperationResult.Failure("عضو تیم پشتیبانی دیگر وجود ندارد.");
    }

    private static SupportPersonDto ToDto(SupportPerson p) => new(p.Id, p.Name, p.PhoneNumber, p.BaleUsername);
}
