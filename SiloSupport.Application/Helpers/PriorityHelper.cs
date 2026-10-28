using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Helpers;

/// <summary>Pure helpers around <see cref="GroupPriority"/>.</summary>
public static class PriorityHelper
{
    /// <summary>All supported priorities, most urgent first (display order).</summary>
    public static IReadOnlyList<GroupPriority> Ordered { get; } =
        new[] { GroupPriority.Critical, GroupPriority.Medium, GroupPriority.Low };

    /// <summary>
    /// Strictly parses a priority name (case-insensitive, trimmed). Numbers, synonyms such as "High"
    /// and anything else are rejected so an unexpected AI answer can never be routed.
    /// </summary>
    public static bool TryParse(string? value, out GroupPriority priority)
    {
        priority = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        foreach (var candidate in Ordered)
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                priority = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Persian display name used by the admin UI (the stored value stays the English enum name).</summary>
    public static string DisplayName(GroupPriority priority) => priority switch
    {
        GroupPriority.Critical => "بحرانی",
        GroupPriority.Medium => "متوسط",
        GroupPriority.Low => "کم",
        _ => priority.ToString()
    };

    /// <summary>The supported priorities for which the given set contains no group.</summary>
    public static IReadOnlyList<GroupPriority> Missing(IEnumerable<GroupPriority> configured)
    {
        var set = configured.ToHashSet();
        return Ordered.Where(p => !set.Contains(p)).ToList();
    }
}
