using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Helpers;

/// <summary>
/// Pure helpers for the media type of stored customer files: filling in a missing MIME type and deciding which types
/// the Web panel may show inline. Everything not on the allow-list is served as a download, so an uploaded HTML or SVG
/// file can never run in the panel's origin.
/// </summary>
public static class AttachmentMime
{
    private static readonly HashSet<string> InlineSafe = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "video/mp4", "video/webm", "video/quicktime",
        "audio/ogg", "audio/mpeg", "audio/mp4", "audio/aac", "audio/wav", "audio/x-wav", "audio/webm", "audio/opus"
    };

    /// <summary>The reported MIME type (without parameters), or a sensible default for the kind when Bale gave none.</summary>
    public static string Resolve(AttachmentKind kind, string? reportedMimeType)
    {
        var mime = Normalize(reportedMimeType);
        if (mime.Length > 0)
            return mime;

        return kind switch
        {
            AttachmentKind.Photo => "image/jpeg",
            AttachmentKind.Video => "video/mp4",
            AttachmentKind.Voice => "audio/ogg",
            AttachmentKind.Audio => "audio/mpeg",
            AttachmentKind.Animation => "video/mp4",
            _ => "application/octet-stream"
        };
    }

    /// <summary>True when the browser may render this type directly in the panel (image, video or audio player).</summary>
    public static bool IsInlineSafe(string? mimeType) => InlineSafe.Contains(Normalize(mimeType));

    /// <summary>"image" / "video" / "audio" for inline-safe types, otherwise "file".</summary>
    public static string PreviewKind(string? mimeType)
    {
        var mime = Normalize(mimeType);
        if (!InlineSafe.Contains(mime))
            return "file";

        return mime.StartsWith("image/", StringComparison.Ordinal) ? "image"
            : mime.StartsWith("video/", StringComparison.Ordinal) ? "video"
            : "audio";
    }

    private static string Normalize(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return string.Empty;

        var semicolon = mimeType.IndexOf(';');
        var bare = semicolon >= 0 ? mimeType[..semicolon] : mimeType;
        return bare.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// The file name offered for a download: the sender's file name reduced to a safe base name (no path, no control
    /// characters or characters that are special in a header), or "file-{id}" plus an extension that fits the MIME type.
    /// </summary>
    public static string DownloadName(int id, string? fileName, string? mimeType)
    {
        var name = string.Empty;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var lastSeparator = fileName.LastIndexOfAny(new[] { '/', '\\' });
            var baseName = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;
            name = new string(baseName.Where(c => !char.IsControl(c) && c is not ('"' or ';' or ':' or '*' or '?' or '<' or '>' or '|')).ToArray()).Trim().Trim('.');
            if (name.Length > 120)
                name = name[..120];
        }

        if (name.Length > 0)
            return name;

        var extension = Normalize(mimeType) switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "audio/ogg" => ".ogg",
            "audio/mpeg" => ".mp3",
            "application/pdf" => ".pdf",
            _ => string.Empty
        };
        return $"file-{id}{extension}";
    }

    /// <summary>A readable size like "1.4 MB"; empty when unknown.</summary>
    public static string FormatSize(long? bytes)
    {
        if (bytes is null or < 0)
            return string.Empty;

        var value = (double)bytes.Value;
        string[] units = { "B", "KB", "MB", "GB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.Value} {units[0]}"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }
}
