namespace SiloSupport.Application.Helpers;

/// <summary>
/// Pure helpers for the "Bale ID" of a customer member. Administrators register a member by the Bale user name
/// (for example <c>@ali</c>) or by the numeric Bale user id; Bale reports both for the sender of a message, so a
/// message is matched against either one. Comparison ignores case and a leading <c>@</c>.
/// </summary>
public static class BaleIdentity
{
    /// <summary>Trimmed, without a leading '@', lower-cased. Empty when nothing usable is left.</summary>
    public static string Normalize(string? baleId)
    {
        var value = baleId?.Trim() ?? string.Empty;
        value = value.TrimStart('@').Trim();
        return value.ToLowerInvariant();
    }

    /// <summary>
    /// The lower-cased spellings under which a stored Bale ID can equal this value: <c>ali</c> and <c>@ali</c>.
    /// Empty when the value has no usable content.
    /// </summary>
    public static IReadOnlyList<string> Variants(string? baleId)
    {
        var normalized = Normalize(baleId);
        return normalized.Length == 0
            ? Array.Empty<string>()
            : new[] { normalized, "@" + normalized };
    }

    /// <summary>
    /// All spellings to look up for one incoming message: the numeric sender id and, when Bale sent one, the
    /// user name (with and without '@').
    /// </summary>
    public static IReadOnlyList<string> CandidatesFor(long? senderId, string? username)
    {
        var candidates = new List<string>();

        if (senderId is not null)
            candidates.AddRange(Variants(senderId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        candidates.AddRange(Variants(username));
        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>True when the stored Bale ID of a member designates this numeric sender id.</summary>
    public static bool IsNumericIdMatch(string storedBaleId, long senderId) =>
        Normalize(storedBaleId) == senderId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True when the stored Bale ID of a member designates this user name.</summary>
    public static bool IsUsernameMatch(string storedBaleId, string? username)
    {
        var normalizedUsername = Normalize(username);
        return normalizedUsername.Length > 0 && Normalize(storedBaleId) == normalizedUsername;
    }
}
