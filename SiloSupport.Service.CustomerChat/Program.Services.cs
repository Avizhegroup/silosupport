using SiloSupport.Application;
using SiloSupport.Service.CustomerChat.Configuration;
using SiloSupport.Service.CustomerChat.Services;
using SiloSupport.Service.CustomerChat.Workers;

namespace SiloSupport.Service.CustomerChat;
public static partial class Program
{
    public static void AddSiloSupportBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BaleOptions>()
            .Bind(configuration.GetSection(BaleOptions.SectionName));

        services.AddOptions<RagAiOptions>()
            .Bind(configuration.GetSection(RagAiOptions.SectionName));

        var baleOptions = configuration.GetSection(BaleOptions.SectionName).Get<BaleOptions>();

        var siloAiOptions = configuration.GetSection(RagAiOptions.SectionName).Get<RagAiOptions>();

        services.AddHttpClient(BaleBotClient.HttpClientName, client =>
        {
            var baseUrl = $"{baleOptions.ApiBaseUrl}/bot{baleOptions.BotToken}/";

            client.BaseAddress = new Uri(baseUrl);

            client.Timeout = TimeSpan.FromSeconds(baleOptions.LongPollTimeoutSeconds + 30);
        });

        services.AddHttpClient(SiloAiClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(siloAiOptions.BaseUrl);

            client.Timeout = TimeSpan.FromSeconds(120);

            client.DefaultRequestHeaders.Add("X-Api-Key", siloAiOptions.ApiKey);
        });

        services.AddSingleton<BaleBotClient>();

        services.AddSingleton<ISiloAiClient, SiloAiClient>();

        services.AddHostedService<BaleBotWorker>();
    }
}
