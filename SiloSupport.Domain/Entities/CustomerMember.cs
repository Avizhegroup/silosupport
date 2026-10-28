namespace SiloSupport.Domain.Entities;

/// <summary>A person belonging to a customer. Identified in incoming Bale messages by <see cref="BaleId"/>.</summary>
public class CustomerMember
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// The member's Bale user id, stored as text (trimmed). The numeric sender id of an incoming Bale message is
    /// compared with it as its invariant decimal string.
    /// </summary>
    public string BaleId { get; set; } = string.Empty;

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }
}
