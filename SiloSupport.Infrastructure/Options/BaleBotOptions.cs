namespace SiloSupport.Infrastructure.Options;

/// <summary>Settings of the Bale bot used by the support routing feature (section "Bale").</summary>
public class BaleBotOptions
{
    public const string SectionName = "Bale";

    /// <summary>When false the polling worker does not start (the administrative UI still works).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bot token from @botfather. Keep it in user secrets / environment variables, never in source control.</summary>
    public string BotToken { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://tapi.bale.ai";

    public int LongPollTimeoutSeconds { get; set; } = 30;

    /// <summary>True when a real token (not empty and not a "&lt;placeholder&gt;") is configured.</summary>
    public bool HasToken =>
        !string.IsNullOrWhiteSpace(BotToken)
        && !(BotToken.TrimStart().StartsWith('<') && BotToken.TrimEnd().EndsWith('>'));
}
