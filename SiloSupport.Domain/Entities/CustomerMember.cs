namespace SiloSupport.Domain.Entities;

/// <summary>A person belonging to a customer. May be identified in incoming Bale messages by <see cref="BaleId"/>.</summary>
public class CustomerMember
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Optional Bale user name ("@ali") or numeric id entered by an administrator, stored as text (trimmed); null for
    /// people without a Bale user name. Messages are matched ignoring case and a leading '@'.
    /// </summary>
    public string? BaleId { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }
}
