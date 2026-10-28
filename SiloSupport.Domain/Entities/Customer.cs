namespace SiloSupport.Domain.Entities;

/// <summary>A customer of the support team. Owns members and destination priority groups.</summary>
public class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ICollection<CustomerMember> Members { get; set; } = new List<CustomerMember>();

    public ICollection<CustomerGroup> Groups { get; set; } = new List<CustomerGroup>();
}
