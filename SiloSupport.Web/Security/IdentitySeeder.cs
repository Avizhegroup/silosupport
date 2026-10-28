using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SiloSupport.Domain.Enums;
using SiloSupport.Persistence;

namespace SiloSupport.Web.Security;

/// <summary>Idempotent startup seeding: migrations, roles and the administrator (credentials come from appsettings.json).</summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AppRoles.Administrator))
            await roles.CreateAsync(new IdentityRole(AppRoles.Administrator));

        var userName = configuration["AdminSeed:UserName"];
        var password = configuration["AdminSeed:Password"];

        if (IsMissing(userName) || IsMissing(password))
        {
            logger.LogWarning(
                "AdminSeed:UserName / AdminSeed:Password are not configured; no administrator was created. " +
                "Set them in appsettings.json to create the first administrator.");
            return;
        }

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByNameAsync(userName!);
        if (existing is null)
        {
            var admin = new ApplicationUser { UserName = userName, FullName = "Administrator", EmailConfirmed = true };
            var created = await users.CreateAsync(admin, password!);
            if (!created.Succeeded)
            {
                logger.LogError(
                    "Could not create the seed administrator: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Code)));
                return;
            }

            existing = admin;
            logger.LogInformation("Created the seed administrator {UserName}", userName);
        }

        if (!await users.IsInRoleAsync(existing, AppRoles.Administrator))
            await users.AddToRoleAsync(existing, AppRoles.Administrator);
    }

    private static bool IsMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) || (value.StartsWith('<') && value.EndsWith('>'));
}
