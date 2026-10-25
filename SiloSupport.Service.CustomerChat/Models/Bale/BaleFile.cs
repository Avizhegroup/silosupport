using System.Text.Json.Serialization;

namespace Silo.Bot.Support.Models.Bale;

/// <summary>Result of the Bale getFile method.</summary>
public class BaleFile
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }
}
