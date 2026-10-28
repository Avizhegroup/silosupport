using System.Globalization;

namespace SiloSupport.Application.Helpers;

/// <summary>
/// Pure helpers that show UTC timestamps the way Iranian administrators expect: Iran standard time (UTC+03:30,
/// daylight saving was abolished in 2022) and the Persian (Jalali) calendar, e.g. <c>1405/08/06 15:30</c>.
/// </summary>
public static class PersianDateFormatter
{
    /// <summary>Fixed offset of Iran standard time.</summary>
    public static readonly TimeSpan IranOffset = TimeSpan.FromMinutes(210);

    private static readonly PersianCalendar Calendar = new();

    /// <summary>Converts a UTC (or unspecified, treated as UTC) timestamp to Iran local time.</summary>
    public static DateTime ToIranTime(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) + IranOffset;

    /// <summary>Jalali date of a local date/time as <c>yyyy/MM/dd</c> (Latin digits).</summary>
    public static string FormatLocalDate(DateTime local) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Calendar.GetYear(local):0000}/{Calendar.GetMonth(local):00}/{Calendar.GetDayOfMonth(local):00}");

    /// <summary>Jalali date and time of a UTC timestamp, in Iran time: <c>yyyy/MM/dd HH:mm</c>.</summary>
    public static string FormatUtc(DateTime utc)
    {
        var local = ToIranTime(utc);
        return string.Create(CultureInfo.InvariantCulture, $"{FormatLocalDate(local)} {local:HH:mm}");
    }
}
