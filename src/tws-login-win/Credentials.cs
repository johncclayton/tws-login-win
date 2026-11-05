namespace tws_login_win;

/// <summary>Which Login tab an account belongs to.</summary>
public enum TradingMode
{
    Paper,
    Live,
}

/// <summary>What a login needs: the account's user name, password and trading mode.</summary>
public sealed record TwsCredentials(string User, string Password, TradingMode Mode)
{
    /// <summary>Where the second factor's one-time code comes from; null when the source holds none.</summary>
    public IOneTimeCodeSource? OneTimeCode { get; init; }

    /// <summary>Records print every member by default; this keeps the values out of every log.</summary>
    public override string ToString() => $"{Mode} credentials (user {User.Length} chars, password {Password.Length} chars)";

    /// <summary>For <c>check-creds</c>: like <see cref="ToString"/>, plus the length of a code fetched now.</summary>
    public string Check() =>
        $"{Mode} credentials (user {User.Length} chars, password {Password.Length} chars, "
        + (OneTimeCode is null ? "no code)" : $"code {OneTimeCode.Current().Length} digits)");

    public static TradingMode ParseMode(string value, string where) =>
        value.Trim().ToLowerInvariant() switch
        {
            "paper" => TradingMode.Paper,
            "live" => TradingMode.Live,
            _ => throw new LoginAbortedException($"{where} must be 'paper' or 'live' (value not shown)."),
        };
}

/// <summary>Gives the one-time code as it is now. Implementations never log or show the code.</summary>
public interface IOneTimeCodeSource
{
    /// <summary>The current code: six digits.</summary>
    string Current();
}

/// <summary>Where credentials come from. Implementations never log or show the values.</summary>
public interface ICredentialSource
{
    /// <summary>Where the credentials come from, for logs - never the values.</summary>
    string Description { get; }

    TwsCredentials Load();
}

public static class CredentialSources
{
    /// <summary>
    /// <c>live</c> or <c>paper</c> uses the 1Password item named by <c>TWS_LOGIN_LIVE_1PASSWORD_ITEM</c> or
    /// <c>TWS_LOGIN_PAPER_1PASSWORD_ITEM</c>; <c>op://vault/item</c> selects 1Password; anything else is an
    /// IBC-style ini path.
    /// </summary>
    public static ICredentialSource FromSpec(string spec, Func<string, string?> environment, IOnePasswordCli onePassword)
    {
        if (spec.Equals("live", StringComparison.OrdinalIgnoreCase) || spec.Equals("paper", StringComparison.OrdinalIgnoreCase))
        {
            var variable = $"TWS_LOGIN_{spec.ToUpperInvariant()}_1PASSWORD_ITEM";
            var item = environment(variable);
            if (string.IsNullOrWhiteSpace(item))
                throw new LoginAbortedException($"{variable} is not set; set it to the op://vault/item for {spec.ToLowerInvariant()} logins.");
            return ForMode(FromSpec(item, environment, onePassword), TwsCredentials.ParseMode(spec, "mode"));
        }
        return spec.StartsWith("op://", StringComparison.OrdinalIgnoreCase)
            ? new OnePasswordCredentialSource(spec, onePassword)
            : new IniCredentialSource(spec);
    }

    /// <summary><paramref name="source"/>, whose credentials must be for <paramref name="mode"/> or the load aborts.</summary>
    public static ICredentialSource ForMode(ICredentialSource source, TradingMode mode) => new ModeChecked(source, mode);

    /// <summary>Credentials asked for by trading mode must be for that mode.</summary>
    sealed class ModeChecked(ICredentialSource inner, TradingMode mode) : ICredentialSource
    {
        public string Description => inner.Description;

        public TwsCredentials Load()
        {
            var credentials = inner.Load();
            return credentials.Mode == mode
                ? credentials
                : throw new LoginAbortedException($"{mode} login asked for, but {inner.Description} is for {credentials.Mode} trading.");
        }
    }
}
