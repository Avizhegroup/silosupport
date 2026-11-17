using Microsoft.Extensions.Hosting;
using Serilog;
using SiloSupport.Service.CustomerSupport;

namespace SiloSupport.Service.CustomerSupport;

/// <summary>
/// Windows service that receives customer messages from Bale support groups, classifies them with the Silo AI API
/// and forwards them to the customer's priority group. The Blazor admin site (SiloSupport.Web) only manages and shows
/// the data; it does not talk to Bale.
/// </summary>
public static partial class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseWindowsService(config => config.ServiceName = "Silo Customer Support Service")
            .ConfigureServices((context, services) => services.AddCustomerSupportBotServices(context.Configuration))
            .UseSerilog()
            .Build();

        await host.RunAsync();
    }
}
