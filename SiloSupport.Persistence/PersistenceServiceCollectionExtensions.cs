using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiloSupport.Application.Interfaces;
using SiloSupport.Persistence.Repositories;

namespace SiloSupport.Persistence;

/// <summary>
/// Registers the DbContext, the repositories and the unit-of-work helper. Used by every host that talks to the
/// support database (the Blazor admin Web project and the Bale customer-support Windows service).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddSupportPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerMemberRepository, CustomerMemberRepository>();
        services.AddScoped<ICustomerGroupRepository, CustomerGroupRepository>();
        services.AddScoped<ICustomerGroupedMessageRepository, CustomerGroupedMessageRepository>();

        return services;
    }
}
