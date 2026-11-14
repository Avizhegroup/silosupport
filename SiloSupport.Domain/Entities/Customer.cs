namespace SiloSupport.Domain.Entities;

/// <summary>A customer of the support team. Owns members and destination priority groups.</summary>
public class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional numeric id of the Bale chat (usually a group) where this customer's people write to the bot.
    /// A message from a sender who is not a support person and not a registered member is assigned to the customer
    /// whose chat it came from. Unique among customers.
    /// </summary>
    public long? BaleChatId { get; set; }

    public ICollection<CustomerMember> Members { get; set; } = new List<CustomerMember>();

    public ICollection<CustomerGroup> Groups { get; set; } = new List<CustomerGroup>();
}
