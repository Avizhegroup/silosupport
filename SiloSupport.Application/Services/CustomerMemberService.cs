using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Services;

/// <summary>Use cases for managing the members of a customer.</summary>
public class CustomerMemberService
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerMemberRepository _members;

    public CustomerMemberService(ICustomerRepository customers, ICustomerMemberRepository members)
    {
        _customers = customers;
        _members = members;
    }

    public async Task<IReadOnlyList<MemberDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var members = await _members.GetByCustomerAsync(customerId, cancellationToken);
        return members.Select(ToDto).ToList();
    }

    public async Task<MemberDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var member = await _members.GetByIdAsync(id, cancellationToken);
        return member is null ? null : ToDto(member);
    }

    public async Task<OperationResult<int>> CreateAsync(int customerId, MemberInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult<int>.Failure(errors);

        if (await _customers.GetByIdAsync(customerId, cancellationToken) is null)
            return OperationResult<int>.Failure("مشتری دیگر وجود ندارد.");

        if (await _members.BaleIdExistsAsync(input.BaleId.Trim(), null, cancellationToken))
            return OperationResult<int>.Failure("عضو دیگری با این شناسه بله ثبت شده است.");

        var member = new CustomerMember
        {
            CustomerId = customerId,
            Name = input.Name.Trim(),
            Description = input.Description?.Trim() ?? string.Empty,
            PhoneNumber = input.PhoneNumber?.Trim() ?? string.Empty,
            BaleId = input.BaleId.Trim()
        };

        await _members.AddAsync(member, cancellationToken);
        return OperationResult<int>.Success(member.Id);
    }

    public async Task<OperationResult> UpdateAsync(int id, MemberInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult.Failure(errors);

        if (await _members.GetByIdAsync(id, cancellationToken) is null)
            return OperationResult.Failure("عضو دیگر وجود ندارد.");

        if (await _members.BaleIdExistsAsync(input.BaleId.Trim(), id, cancellationToken))
            return OperationResult.Failure("عضو دیگری با این شناسه بله ثبت شده است.");

        var updated = await _members.UpdateAsync(
            new CustomerMember
            {
                Id = id,
                Name = input.Name.Trim(),
                Description = input.Description?.Trim() ?? string.Empty,
                PhoneNumber = input.PhoneNumber?.Trim() ?? string.Empty,
                BaleId = input.BaleId.Trim()
            },
            cancellationToken);

        return updated ? OperationResult.Success() : OperationResult.Failure("عضو دیگر وجود ندارد.");
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _members.DeleteAsync(id, cancellationToken);
        return deleted ? OperationResult.Success() : OperationResult.Failure("عضو دیگر وجود ندارد.");
    }

    private static MemberDto ToDto(CustomerMember m) =>
        new(m.Id, m.CustomerId, m.Name, m.Description, m.PhoneNumber, m.BaleId);
}
