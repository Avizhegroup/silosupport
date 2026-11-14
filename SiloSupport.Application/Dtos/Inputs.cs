using System.ComponentModel.DataAnnotations;
using SiloSupport.Application.Helpers;
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

    /// <summary>
    /// Optional numeric Bale chat id (usually a group) where the people of this customer write. Messages from senders
    /// who are neither support persons nor registered members are assigned to the customer of their chat.
    /// </summary>
    public long? BaleChatId { get; set; }
}

/// <summary>Form/command model for creating or editing a customer member.</summary>
public class MemberInput : IValidatableObject
{
    [Required(ErrorMessage = "نام الزامی است.")]
    [StringLength(200, ErrorMessage = "نام نباید بیشتر از ۲۰۰ کاراکتر باشد.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "توضیحات نباید بیشتر از ۱۰۰۰ کاراکتر باشد.")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The member's mobile phone number (required, at least 10 digits). Persian digits are accepted.
    /// </summary>
    [Required(ErrorMessage = "شماره تلفن الزامی است.")]
    [StringLength(32, ErrorMessage = "شماره تلفن نباید بیشتر از ۳۲ کاراکتر باشد.")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Optional Bale ID as text: the Bale user name (for example "@ali" or "ali") or the numeric Bale user id
    /// (for example "123456789"). Leave empty for people without a Bale user name; they are identified by phone number.
    /// </summary>
    [StringLength(64, ErrorMessage = "شناسه بله نباید بیشتر از ۶۴ کاراکتر باشد.")]
    public string? BaleId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(PhoneNumber) && !PhoneNumberNormalizer.IsValid(PhoneNumber))
            yield return new ValidationResult("شماره تلفن معتبر نیست؛ حداقل ۱۰ رقم لازم است.", new[] { nameof(PhoneNumber) });

        var id = BaleId?.Trim();
        if (string.IsNullOrEmpty(id))
            yield break; // optional

        if (id == "0")
            yield return new ValidationResult("شناسه بله الزامی است و نمی‌تواند ۰ باشد.", new[] { nameof(BaleId) });
        else if (id.Any(char.IsWhiteSpace))
            yield return new ValidationResult("شناسه بله نباید شامل فاصله باشد.", new[] { nameof(BaleId) });
        else if (id.TrimStart('@').Length == 0)
            yield return new ValidationResult("شناسه بله باید بعد از @ حداقل یک نویسه داشته باشد.", new[] { nameof(BaleId) });
        else if (id.TrimStart('@') != id.TrimStart('@').TrimEnd('@') || id.Count(c => c == '@') > 1)
            yield return new ValidationResult("شناسه بله فقط می‌تواند با یک @ شروع شود.", new[] { nameof(BaleId) });
    }
}

/// <summary>Form/command model for creating or editing a support person (shared by all customers).</summary>
public class SupportPersonInput : IValidatableObject
{
    [Required(ErrorMessage = "نام الزامی است.")]
    [StringLength(200, ErrorMessage = "نام نباید بیشتر از ۲۰۰ کاراکتر باشد.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "شماره تلفن الزامی است.")]
    [StringLength(32, ErrorMessage = "شماره تلفن نباید بیشتر از ۳۲ کاراکتر باشد.")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The Bale user name, with or without '@' (for example "@ali"). Required and unique.</summary>
    [Required(ErrorMessage = "نام کاربری بله الزامی است.")]
    [StringLength(64, ErrorMessage = "نام کاربری بله نباید بیشتر از ۶۴ کاراکتر باشد.")]
    public string BaleUsername { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(PhoneNumber) && !PhoneNumberNormalizer.IsValid(PhoneNumber))
            yield return new ValidationResult("شماره تلفن معتبر نیست؛ حداقل ۱۰ رقم لازم است.", new[] { nameof(PhoneNumber) });

        var username = BaleUsername?.Trim();
        if (string.IsNullOrEmpty(username))
            yield break; // reported by [Required]

        var bare = username.TrimStart('@');
        if (username.Any(char.IsWhiteSpace))
            yield return new ValidationResult("نام کاربری بله نباید شامل فاصله باشد.", new[] { nameof(BaleUsername) });
        else if (bare.Length == 0)
            yield return new ValidationResult("نام کاربری بله باید بعد از @ حداقل یک نویسه داشته باشد.", new[] { nameof(BaleUsername) });
        else if (username.Count(c => c == '@') > 1)
            yield return new ValidationResult("نام کاربری بله فقط می‌تواند با یک @ شروع شود.", new[] { nameof(BaleUsername) });
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
