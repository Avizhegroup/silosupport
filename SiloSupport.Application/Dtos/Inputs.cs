using System.ComponentModel.DataAnnotations;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Dtos;

/// <summary>Form/command model for creating or editing a customer. Messages are Persian (the UI language).</summary>
public class CustomerInput
{
    [Required(ErrorMessage = "نام الزامی است.")]
    [StringLength(200, ErrorMessage = "نام نباید بیشتر از ۲۰۰ کاراکتر باشد.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "توضیحات نباید بیشتر از ۱۰۰۰ کاراکتر باشد.")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>Form/command model for creating or editing a customer member.</summary>
public class MemberInput : IValidatableObject
{
    [Required(ErrorMessage = "نام الزامی است.")]
    [StringLength(200, ErrorMessage = "نام نباید بیشتر از ۲۰۰ کاراکتر باشد.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "توضیحات نباید بیشتر از ۱۰۰۰ کاراکتر باشد.")]
    public string Description { get; set; } = string.Empty;

    [StringLength(32, ErrorMessage = "شماره تلفن نباید بیشتر از ۳۲ کاراکتر باشد.")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// The member's Bale ID as text: the Bale user name (for example "@ali" or "ali") or the numeric Bale user id
    /// (for example "123456789"). Incoming messages are matched against either one.
    /// </summary>
    [Required(ErrorMessage = "شناسه بله الزامی است.")]
    [StringLength(64, ErrorMessage = "شناسه بله نباید بیشتر از ۶۴ کاراکتر باشد.")]
    public string BaleId { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var id = BaleId?.Trim();
        if (string.IsNullOrEmpty(id))
            yield break; // reported by [Required]

        if (id == "0")
            yield return new ValidationResult("شناسه بله الزامی است و نمی‌تواند ۰ باشد.", new[] { nameof(BaleId) });
        else if (id.Any(char.IsWhiteSpace))
            yield return new ValidationResult("شناسه بله نباید شامل فاصله باشد.", new[] { nameof(BaleId) });
        else if (id.TrimStart('@').Length == 0)
            yield return new ValidationResult("نام کاربری بله باید بعد از @ حداقل یک نویسه داشته باشد.", new[] { nameof(BaleId) });
        else if (id.TrimStart('@') != id.TrimStart('@').TrimEnd('@') || id.Count(c => c == '@') > 1)
            yield return new ValidationResult("شناسه بله فقط می‌تواند با یک @ شروع شود.", new[] { nameof(BaleId) });
    }
}

/// <summary>Form/command model for creating or editing a destination priority group.</summary>
public class GroupInput : IValidatableObject
{
    [Required(ErrorMessage = "نام الزامی است.")]
    [StringLength(200, ErrorMessage = "نام نباید بیشتر از ۲۰۰ کاراکتر باشد.")]
    public string Name { get; set; } = string.Empty;

    public long BaleId { get; set; }

    public GroupPriority Priority { get; set; } = GroupPriority.Low;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaleId == 0)
            yield return new ValidationResult("شناسه گروه بله الزامی است و نمی‌تواند ۰ باشد.", new[] { nameof(BaleId) });

        if (!Enum.IsDefined(Priority))
            yield return new ValidationResult("اولویت باید کم، متوسط یا بحرانی باشد.", new[] { nameof(Priority) });
    }
}
