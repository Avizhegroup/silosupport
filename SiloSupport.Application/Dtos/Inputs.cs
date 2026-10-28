using System.ComponentModel.DataAnnotations;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Dtos;

/// <summary>Form/command model for creating or editing a customer.</summary>
public class CustomerInput
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be at most 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description must be at most 1000 characters.")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>Form/command model for creating or editing a customer member.</summary>
public class MemberInput : IValidatableObject
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be at most 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description must be at most 1000 characters.")]
    public string Description { get; set; } = string.Empty;

    [StringLength(32, ErrorMessage = "Phone number must be at most 32 characters.")]
    public string PhoneNumber { get; set; } = string.Empty;

    public long BaleId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaleId == 0)
            yield return new ValidationResult("Bale ID is required and cannot be 0.", new[] { nameof(BaleId) });
    }
}

/// <summary>Form/command model for creating or editing a destination priority group.</summary>
public class GroupInput : IValidatableObject
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be at most 200 characters.")]
    public string Name { get; set; } = string.Empty;

    public long BaleId { get; set; }

    public GroupPriority Priority { get; set; } = GroupPriority.Low;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaleId == 0)
            yield return new ValidationResult("Bale group ID is required and cannot be 0.", new[] { nameof(BaleId) });

        if (!Enum.IsDefined(Priority))
            yield return new ValidationResult("Priority must be Low, Medium or Critical.", new[] { nameof(Priority) });
    }
}
