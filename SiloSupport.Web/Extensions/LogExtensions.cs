using Serilog;
using SiloSupport.Shared;

namespace SiloSupport.Web.Extensions;
public static class LogExtensions
{
    public static void AddSiloSerilog(this IServiceCollection services
        , IConfiguration appConfig)
    {
        services.AddLogging(config =>
        {
            var customLogger = new LoggerConfiguration()
                .Enrich.FromLogContext().MinimumLevel.Information();

            customLogger.WriteTo.Debug();

#if DEBUG
            customLogger.WriteTo.Console();
#else
            if (appConfig["Seq:Url"].HasValue())
            {
                customLogger
                .WriteTo.Seq(appConfig["Seq:Url"].ToString());
            }
            else
            {
                customLogger
                    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                    .WriteTo.Logger(lc => lc
                        .Filter.ByIncludingOnly(evt => evt.Level == Serilog.Events.LogEventLevel.Warning)
                        .WriteTo.File($"Logs/Exceptions/Log-{PersianCalendarTools.GregorianToPersianWithManualSeprator(DateTime.Now, "")}.log"
                        , outputTemplate: @"-------------------Exception Begin----------------------
                                {NewLine}Exception Occure Time:{Timestamp:o}
                                {NewLine}Exception Message:{Message}
                                {NewLine}Exception Base:{Exception}
                                {NewLine}-------------------Exception End----------------------{NewLine}"))
                    .WriteTo.Logger(lc => lc
                        .Filter.ByIncludingOnly(evt => evt.Level <= Serilog.Events.LogEventLevel.Information)
                        .WriteTo.File($"Logs/InfoLogs/Log-{PersianCalendarTools.GregorianToPersianWithManualSeprator(DateTime.Now, "")}.log"
                        , outputTemplate: @"-------------------Log Begin----------------------
                                {NewLine}Occure Time:{Timestamp:o}
                                {NewLine}Message:{Message}
                                {NewLine}-------------------Log End----------------------{NewLine}"));
            }
#endif
            Log.Logger =
                   customLogger.CreateLogger();

            config.AddSerilog(logger: Log.Logger, dispose: true);
        });
    }
}
