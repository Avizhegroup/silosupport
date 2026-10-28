using System.Text;
using SiloSupport.Application.Dtos;

namespace SiloSupport.Service.CustomerSupport.Helpers;

/// <summary>
/// Pure formatting of the text forwarded to a customer's priority group. Only data that is actually
/// available is included; the analysis section is omitted when the AI returned none.
/// </summary>
public static class ForwardedMessageFormatter
{
    /// <summary>Bale text messages are limited to 4096 characters; stay safely below it.</summary>
    public const int MaxMessageCharacters = 2500;
    public const int MaxAnalysisCharacters = 800;
    private const int MaxNameCharacters = 100;

    public static string Format(ForwardedMessageContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine("📩 پیام جدید مشتری");
        builder.AppendLine($"مشتری: {Truncate(context.CustomerName, MaxNameCharacters)}");

        var sender = Truncate(context.MemberName, MaxNameCharacters);
        if (!string.IsNullOrWhiteSpace(context.MemberPhoneNumber))
            sender += $" ({context.MemberPhoneNumber.Trim()})";
        builder.AppendLine($"فرستنده: {sender}");

        builder.AppendLine($"اولویت: {context.Priority}");
        builder.AppendLine();
        builder.AppendLine("متن پیام:");
        builder.AppendLine(Truncate(context.OriginalMessage.Trim(), MaxMessageCharacters));

        if (!string.IsNullOrWhiteSpace(context.Analysis))
        {
            builder.AppendLine();
            builder.AppendLine("تحلیل هوش مصنوعی:");
            builder.AppendLine(Truncate(context.Analysis.Trim(), MaxAnalysisCharacters));
        }

        return builder.ToString().TrimEnd();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value.Substring(0, max - 1) + "…";
    }
}
