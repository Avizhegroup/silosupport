namespace SiloSupport.Service.CustomerSupport.Options;

/// <summary>Settings of the Bale bot used by the support routing feature (section "Bale").</summary>
public class BaleBotOptions
{
    public const string SectionName = "Bale";

    /// <summary>When false the polling worker does not start (the administrative UI still works).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bot token from @botfather (set it in appsettings.json).</summary>
    public string BotToken { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://tapi.bale.ai";

    public int LongPollTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Largest customer file (in megabytes) that is downloaded and stored for the Web panel. Bale lets bots download
    /// files up to 20 MB; bigger files are still forwarded to the priority group by their file id but cannot be shown in the panel.
    /// </summary>
    public int MaxDownloadMegabytes { get; set; } = 20;

    public long MaxDownloadBytes => Math.Clamp(MaxDownloadMegabytes, 1, 20) * 1024L * 1024L;

    /// <summary>True when a real token (not empty and not a "&lt;placeholder&gt;") is configured.</summary>
    public bool HasToken =>
        !string.IsNullOrWhiteSpace(BotToken)
        && !(BotToken.TrimStart().StartsWith('<') && BotToken.TrimEnd().EndsWith('>'));
}
