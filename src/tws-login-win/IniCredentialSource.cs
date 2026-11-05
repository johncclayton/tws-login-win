using System.Text.RegularExpressions;

namespace tws_login_win;

/// <summary>
/// IBC-style config file: <c>IbLoginId=</c>, <c>IbPassword=</c>, <c>TradingMode=paper|live</c>.
/// Only this class reads the file, and it never prints a value. <c>IbPassword</c> is taken exactly as written after
/// <c>=</c>, spaces included (a password may start or end with one); the other two are trimmed.
/// </summary>
public sealed partial class IniCredentialSource(string path) : ICredentialSource
{
    public string Description => $"ini file {path}";

    public TwsCredentials Load()
    {
        if (!File.Exists(path))
            throw new LoginAbortedException($"credential file not found: {path}");

        string? user = null, password = null, mode = null;
        foreach (var line in File.ReadLines(path))
        {
            var match = Setting().Match(line);
            if (!match.Success)
                continue;
            var value = match.Groups[2].Value;
            switch (match.Groups[1].Value)
            {
                case "IbLoginId": user = value.Trim(); break;
                case "IbPassword": password = value; break;
                default: mode = value.Trim(); break;
            }
        }

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
            throw new LoginAbortedException($"IbLoginId / IbPassword missing from {path} (values not shown).");
        if (string.IsNullOrEmpty(mode))
            throw new LoginAbortedException($"TradingMode missing from {path}.");
        return new TwsCredentials(user, password, TwsCredentials.ParseMode(mode, $"TradingMode in {path}"));
    }

    [GeneratedRegex(@"^\s*(IbLoginId|IbPassword|TradingMode)\s*=(.*)$")]
    private static partial Regex Setting();
}
