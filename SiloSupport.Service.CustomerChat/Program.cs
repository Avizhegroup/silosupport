using Serilog;

namespace Silo.Bot.Support;
public static partial class Program
{
    static void Main(string[] args)
    {
        var webApplicationOptions = new WebApplicationOptions()
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = args,
            ApplicationName = System.Diagnostics.Process.GetCurrentProcess().ProcessName
        };

        var builder = WebApplication.CreateBuilder(webApplicationOptions);

        builder.Host
               .UseWindowsService(config =>
               {
                   config.ServiceName = "Silo Support Service";
               })
               .ConfigureServices((context, services) =>
               {
                   services.AddSiloSupportBotServices(context.Configuration);
               })
               .UseSerilog();


        var app = builder.Build();

        app.Run();
    }
}
