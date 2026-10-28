namespace SiloSupport.Domain.Enums;

/// <summary>
/// Business priority assigned to a customer's destination group. Exactly these values are supported.
/// </summary>
public enum GroupPriority
{
    /// <summary>General questions, minor or cosmetic issues.</summary>
    Low = 0,

    /// <summary>Problems that affect the customer but are not blocking.</summary>
    Medium = 1,

    /// <summary>Blocking problems that need immediate attention.</summary>
    Critical = 2
}
