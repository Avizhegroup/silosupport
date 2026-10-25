using System.Text.Json.Serialization;

namespace Silo.Bot.Support.Models.Bale;

/// <summary>Wrapper returned by every Bale Bot API call.</summary>
public class BaleApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }
}
