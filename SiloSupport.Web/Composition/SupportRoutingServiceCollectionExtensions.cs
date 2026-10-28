using SiloSupport.Application;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Persistence;
using SiloSupport.Persistence.Repositories;

namespace SiloSupport.Web.Composition;

/// <summary>Registers everything the customer support routing feature needs (repositories, services, adapters, worker).</summary>
public static class SupportRoutingServiceCollectionExtensions
{
    public static IServiceCollection AddSupportRouting(this IServiceCollection services, IConfiguration configuration)
    {
        // Options (typed; same section names as the existing SiloSupport.Service.CustomerChat project).
        services.AddOptions<RagAiOptions>().Bind(configuration.GetSection(RagAiOptions.SectionName));

        // Persistence ports.
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerMemberRepository, CustomerMemberRepository>();
        services.AddScoped<ICustomerGroupRepository, CustomerGroupRepository>();
        services.AddScoped<ICustomerGroupedMessageRepository, CustomerGroupedMessageRepository>();

        // Application services.
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CustomerService>();
        services.AddScoped<CustomerMemberService>();
        services.AddScoped<CustomerGroupService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<CustomerMessageProcessor>();

        return services;
    }
}
