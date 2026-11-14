namespace SiloSupport.Domain.Entities;

/// <summary>
/// A person of our own support team. Support persons are shared by all customers. A Bale message sent by a support
/// person (recognised by <see cref="BaleUsername"/>) is never treated as a customer message; every other sender is
/// a customer.
/// </summary>
public class SupportPerson
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Bale user name, stored normalized: without '@' and in lower case ("ali" for "@Ali"). Unique. Every support
    /// person has one, because it is the only thing Bale tells us about a sender.
    /// </summary>
    public string BaleUsername { get; set; } = string.Empty;
}
