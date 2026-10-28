using System.Text.Json;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Helpers;

/// <summary>The priority and analysis extracted from the AI response.</summary>
public sealed record AiClassification(GroupPriority Priority, string? Analysis);

/// <summary>
/// Parses the AI answer to the classification prompt: a JSON object with a "priority" of exactly
/// Low, Medium or Critical and an optional "analysis". Anything else is rejected; nothing is guessed.
/// </summary>
public static class AiPriorityResultParser
{
    public static bool TryParse(string? responseText, out AiClassification? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(responseText))
        {
            error = "The AI response was empty.";
            return false;
        }

        // Tolerate markdown code fences or short prose around the JSON object.
        var start = responseText.IndexOf('{');
        var end = responseText.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            error = "The AI response did not contain a JSON object.";
            return false;
        }

        var json = responseText.Substring(start, end - start + 1);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The AI response JSON was not an object.";
                return false;
            }

            string? priorityText = null;
            string? analysis = null;
            var hasPriority = false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("priority") || string.Equals(property.Name, "priority", StringComparison.OrdinalIgnoreCase))
                {
                    hasPriority = true;
                    if (property.Value.ValueKind == JsonValueKind.String)
                        priorityText = property.Value.GetString();
                }
                else if (string.Equals(property.Name, "analysis", StringComparison.OrdinalIgnoreCase)
                         && property.Value.ValueKind == JsonValueKind.String)
                {
                    analysis = property.Value.GetString();
                }
            }

            if (!hasPriority)
            {
                error = "The AI response had no priority field.";
                return false;
            }

            if (!PriorityHelper.TryParse(priorityText, out var priority))
            {
                error = "The AI response contained an unsupported priority value.";
                return false;
            }

            analysis = string.IsNullOrWhiteSpace(analysis) ? null : analysis.Trim();
            result = new AiClassification(priority, analysis);
            return true;
        }
        catch (JsonException)
        {
            error = "The AI response JSON could not be parsed.";
            return false;
        }
    }
}
