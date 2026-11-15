using System.Text;
using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Enums;

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

        if (!string.IsNullOrWhiteSpace(context.ThreadTitle))
        {
            builder.AppendLine(context.IsFollowUp
                ? $"🧵 ادامه موضوع: {Truncate(context.ThreadTitle.Trim(), MaxNameCharacters)}"
                : $"🧵 موضوع جدید: {Truncate(context.ThreadTitle.Trim(), MaxNameCharacters)}");
        }

        if (context.AttachmentKind is { } kind)
        {
            var file = $"پیوست: {Label(kind)}";
            if (!string.IsNullOrWhiteSpace(context.AttachmentFileName))
                file += $" ({Truncate(context.AttachmentFileName.Trim(), MaxNameCharacters)})";
            builder.AppendLine(file);
        }

        if (!string.IsNullOrWhiteSpace(context.OriginalMessage))
        {
            builder.AppendLine();
            builder.AppendLine("متن پیام:");
            builder.AppendLine(Truncate(context.OriginalMessage.Trim(), MaxMessageCharacters));
        }

        if (!string.IsNullOrWhiteSpace(context.Analysis))
        {
            builder.AppendLine();
            builder.AppendLine("تحلیل هوش مصنوعی:");
            builder.AppendLine(Truncate(context.Analysis.Trim(), MaxAnalysisCharacters));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>The Persian name of a file kind, as shown to support.</summary>
    public static string Label(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo => "تصویر",
        AttachmentKind.Video => "ویدیو",
        AttachmentKind.Voice => "پیام صوتی",
        AttachmentKind.Audio => "فایل صوتی",
        AttachmentKind.Animation => "انیمیشن",
        _ => "فایل"
    };

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value.Substring(0, max - 1) + "…";
    }
}
