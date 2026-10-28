using System.ComponentModel.DataAnnotations;

namespace SiloSupport.Application.Services;

/// <summary>Runs DataAnnotations/IValidatableObject validation so the UI and services share one rule set.</summary>
internal static class InputValidation
{
    public static IReadOnlyList<string> Validate(object input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);

        return results
            .Select(r => r.ErrorMessage ?? "Invalid value.")
            .Distinct()
            .ToList();
    }
}
