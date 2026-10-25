namespace Silo.Bot.Support.Configuration;

/// <summary>Configuration options for the Bale Bot API.</summary>
public class BaleOptions
{
    public const string SectionName = "Bale";

    public string BotToken { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = string.Empty;

    public int LongPollTimeoutSeconds { get; set; } = 30;
}
