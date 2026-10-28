namespace SiloSupport.Domain.Entities;

/// <summary>A person belonging to a customer. Identified in incoming Bale messages by <see cref="BaleId"/>.</summary>
public class CustomerMember
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// The member's Bale user id. Bale ids can exceed 32 bits (up to 52 significant bits per the
    /// Bale documentation), so a 64-bit integer is used.
    /// </summary>
    public long BaleId { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }
}
