using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SiloSupport.Application;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Infrastructure.Shared;
using SiloSupport.Persistence;
using SiloSupport.Service.CustomerSupport.Workers;

namespace SiloSupport.Service.CustomerSupport;

public static partial class Program
{
    /// <summary>Composition root of the customer-support Windows service.</summary>
    public static IServiceCollection AddCustomerSupportBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Typed options (same section names as the existing SiloSupport.Service.CustomerChat project).
        services.AddOptions<BaleBotOptions>().Bind(configuration.GetSection(BaleBotOptions.SectionName));
        services.AddOptions<RagAiOptions>().Bind(configuration.GetSection(RagAiOptions.SectionName));

        // Persistence (the Web project owns and applies the migrations; this service never migrates).
        services.AddSupportPersistence(configuration["Database:ConnectionString"]);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CustomerMessageProcessor>();

        // Named HttpClients; credentials are read lazily from options when a client is first created.
        services.AddHttpClient(BaleBotApiClient.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<BaleBotOptions>>().Value;
                client.BaseAddress = new Uri($"{options.ApiBaseUrl.TrimEnd('/')}/bot{options.BotToken}/");
                client.Timeout = TimeSpan.FromSeconds(options.LongPollTimeoutSeconds + 30);
            });

        services.AddHttpClient(SiloAiClient.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<RagAiOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
                client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
            });

        // External adapters.
        services.AddSingleton<IBaleBotClient, BaleBotApiClient>();
        services.AddScoped<ISiloAiClient, SiloAiClient>();
        services.AddScoped<IMessagePriorityClassifier, SiloAiPriorityClassifier>();

        // Receiving mechanism: long polling (getUpdates), as in SiloSupport.Service.CustomerChat.
        services.AddHostedService<BaleUpdatesWorker>();

        return services;
    }
}
