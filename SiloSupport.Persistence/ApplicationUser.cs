using Microsoft.AspNetCore.Identity;

namespace SiloSupport.Persistence;

/// <summary>Identity user of the administrative UI. Lives in Persistence because it derives from <see cref="IdentityUser"/>.</summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
}
