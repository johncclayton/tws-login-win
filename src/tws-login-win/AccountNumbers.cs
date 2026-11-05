using System.Text.RegularExpressions;

namespace tws_login_win;

/// <summary>TWS puts the account number (U1234567, DU1234567) in window titles; it is never logged.</summary>
public static partial class AccountNumbers
{
    public static string Redact(string text) => Pattern().Replace(text, "[account]");

    [GeneratedRegex(@"\b[A-Z]{1,3}\d{5,9}\b")]
    private static partial Regex Pattern();
}
