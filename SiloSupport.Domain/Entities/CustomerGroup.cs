using SiloSupport.Domain.Enums;

namespace SiloSupport.Domain.Entities;

/// <summary>
/// A <b>destination</b> Bale group of a customer for one priority. Source support groups are never
/// configured here.
/// </summary>
public class CustomerGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Bale chat/group id (64-bit, see <see cref="CustomerMember.BaleId"/>).</summary>
    public long BaleId { get; set; }

    public int CustomerId { get; set; }

    public GroupPriority Priority { get; set; }

    public Customer? Customer { get; set; }

    public ICollection<CustomerGroupedMessage> Messages { get; set; } = new List<CustomerGroupedMessage>();
}
