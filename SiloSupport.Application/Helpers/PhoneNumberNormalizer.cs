namespace SiloSupport.Application.Helpers;

/// <summary>
/// Pure helpers to compare phone numbers written in different formats ("09121234567", "+98 912 123 4567",
/// "989121234567", "۰۹۱۲۱۲۳۴۵۶۷"). Bale reports a shared contact as international digits without '+'
/// ("989121234567"), administrators usually type the national form ("09121234567").
/// Two numbers are the same person's number when their last <see cref="KeyLength"/> digits are equal.
/// </summary>
public static class PhoneNumberNormalizer
{
    /// <summary>Number of trailing digits used as the comparison key (an Iranian mobile number without the 0 / 98 prefix).</summary>
    public const int KeyLength = 10;

    public const int MaxDigits = 15;

    /// <summary>The ASCII digits of the value; Persian and Arabic-Indic digits are converted, everything else is dropped.</summary>
    public static string DigitsOnly(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;

        var buffer = new System.Text.StringBuilder(phoneNumber.Length);
        foreach (var c in phoneNumber)
        {
            if (c is >= '0' and <= '9')
                buffer.Append(c);
            else if (c is >= '۰' and <= '۹')   // Persian digits
                buffer.Append((char)('0' + (c - '۰')));
            else if (c is >= '٠' and <= '٩')   // Arabic-Indic digits
                buffer.Append((char)('0' + (c - '٠')));
        }

        return buffer.ToString();
    }

    /// <summary>True when the value has between 10 and 15 digits, enough to be compared reliably.</summary>
    public static bool IsValid(string? phoneNumber)
    {
        var digits = DigitsOnly(phoneNumber);
        return digits.Length is >= KeyLength and <= MaxDigits;
    }

    /// <summary>The comparison key (last 10 digits), or an empty string when the value is not a usable phone number.</summary>
    public static string Key(string? phoneNumber)
    {
        var digits = DigitsOnly(phoneNumber);
        return digits.Length >= KeyLength ? digits[^KeyLength..] : string.Empty;
    }

    /// <summary>True when both values are usable phone numbers and have the same key.</summary>
    public static bool AreSame(string? first, string? second)
    {
        var key = Key(first);
        return key.Length > 0 && key == Key(second);
    }
}
