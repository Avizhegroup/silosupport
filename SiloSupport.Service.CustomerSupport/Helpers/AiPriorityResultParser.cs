using SiloSupport.Application.Helpers;
using System.Text.Json;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Helpers;

/// <summary>The priority, analysis, continued issue, issue title and issue summary extracted from the AI response.</summary>
/// <param name="ThreadId">The open issue the message continues; null for a new issue (or when the AI gave no usable id).</param>
/// <param name="ThreadTitle">Short title of the issue; null when the AI gave none.</param>
/// <param name="ThreadSummary">Summary of the whole issue including this message; null when the AI gave none.</param>
public sealed record AiClassification(GroupPriority Priority, string? Analysis, int? ThreadId = null, string? ThreadTitle = null, string? ThreadSummary = null);

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
            int? threadId = null;
            string? title = null;
            string? summary = null;
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
                else if (string.Equals(property.Name, "threadId", StringComparison.OrdinalIgnoreCase))
                {
                    // A number (or a numeric string) is an issue number; null, "new" or anything else means a new issue.
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
                        threadId = number;
                    else if (property.Value.ValueKind == JsonValueKind.String
                             && int.TryParse(property.Value.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedNumber))
                        threadId = parsedNumber;
                }
                else if (string.Equals(property.Name, "title", StringComparison.OrdinalIgnoreCase)
                         && property.Value.ValueKind == JsonValueKind.String)
                {
                    title = property.Value.GetString();
                }
                else if (string.Equals(property.Name, "summary", StringComparison.OrdinalIgnoreCase)
                         && property.Value.ValueKind == JsonValueKind.String)
                {
                    summary = property.Value.GetString();
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
            title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
            result = new AiClassification(priority, analysis, threadId, title, summary);
            return true;
        }
        catch (JsonException)
        {
            error = "The AI response JSON could not be parsed.";
            return false;
        }
    }
}
