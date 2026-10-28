namespace SiloSupport.Web.Security;

/// <summary>Validates post-login return URLs so the login endpoint can never redirect to another site (open redirect).</summary>
public static class ReturnUrlHelper
{
    public const string DefaultUrl = "/admin/customers";

    /// <summary>True only for local, absolute-path URLs such as "/admin/customers".</summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048)
            return false;

        if (url[0] != '/')
            return false;

        // "//host" and "/\host" are interpreted by browsers as external URLs.
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
            return false;

        return !url.Any(char.IsControl);
    }

    public static string Resolve(string? url) => IsSafe(url) ? url! : DefaultUrl;
}
