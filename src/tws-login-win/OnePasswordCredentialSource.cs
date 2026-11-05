using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace tws_login_win;

/// <summary>
/// One field of a 1Password item; <see cref="Section"/> is null for a field outside any section. <see cref="Type"/>
/// is 1Password's (STRING, CONCEALED, OTP, ...); an OTP field's <see cref="Totp"/> is its current code.
/// </summary>
public sealed record OnePasswordField(string Label, string? Section, string Value, string Type = "STRING", string? Totp = null)
{
    /// <summary>Records print every member by default; this keeps the value and the code out of every log.</summary>
    public override string ToString() => $"field '{Label}' ({Type}{(Section is null ? "" : $", section '{Section}'")}, {Value.Length} chars)";
}

/// <summary>
/// A 1Password item as <c>op item get --format json</c> gives it. Website entries are kept apart from fields by
/// 1Password, so only their labels are kept, to explain a website entry made where a field was meant.
/// </summary>
public sealed record OnePasswordItem(IReadOnlyList<OnePasswordField> Fields, IReadOnlyList<string> UrlLabels)
{
    /// <summary>Never the fields' values.</summary>
    public override string ToString() => $"1Password item ({Fields.Count} fields)";

    public static OnePasswordItem FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var fields = Array(root, "fields")
            .Select(f => new OnePasswordField(
                Text(f, "label") ?? "",
                f.TryGetProperty("section", out var section) && Text(section, "label") is { Length: > 0 } name ? name : null,
                Text(f, "value") ?? "",
                Text(f, "type") ?? "STRING",
                Text(f, "totp")))
            .ToList();
        var urlLabels = Array(root, "urls").Select(u => Text(u, "label")).OfType<string>().ToList();
        return new OnePasswordItem(fields, urlLabels);
    }

    static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>Reads one 1Password item, with its values.</summary>
public interface IOnePasswordCli
{
    OnePasswordItem GetItem(string vault, string item);
}

/// <summary>
/// A 1Password item (<c>op://vault/item</c>, each by name or ID) holding <c>username</c>, <c>password</c> and a text
/// field <c>trading mode</c> set to paper or live. A field is found by label, ignoring case: outside any section
/// first, otherwise in the one section that has it. Logins saved by a browser add sections repeating these labels.
/// </summary>
public sealed class OnePasswordCredentialSource(string itemReference, IOnePasswordCli cli) : ICredentialSource
{
    readonly string _item = itemReference.TrimEnd('/');

    public string Description => $"1Password item {_item}";

    public TwsCredentials Load()
    {
        var parts = _item["op://".Length..].Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0))
            throw new LoginAbortedException($"'{_item}' is not a 1Password item reference of the form op://vault/item.");
        var item = cli.GetItem(parts[0], parts[1]);
        var credentials = new TwsCredentials(
            Field(item, "username"),
            Field(item, "password"),
            TwsCredentials.ParseMode(Field(item, "trading mode"), $"field 'trading mode' of {_item}"));
        if (credentials.Mode == TradingMode.Paper)
            return credentials;
        OtpField(item);
        return credentials with { OneTimeCode = new ItemCode(() => OtpField(cli.GetItem(parts[0], parts[1])).Totp, _item) };
    }

    OnePasswordField OtpField(OnePasswordItem item)
    {
        var otp = item.Fields.Where(f => f.Type == "OTP").ToList();
        return otp.Count switch
        {
            1 => otp[0],
            0 => throw new LoginAbortedException($"{_item} is live but has no field of type OTP for the one-time code."),
            _ => throw new LoginAbortedException(
                $"{_item} has {otp.Count} fields of type OTP ({Places(otp)}); keep one."),
        };
    }

    static string Places(IEnumerable<OnePasswordField> fields) =>
        string.Join(", ", fields.Select(f => f.Section is null ? "outside any section" : $"'{f.Section}'"));

    /// <summary>Reads the item again on every call, so the code is the one 1Password shows at that moment.</summary>
    sealed class ItemCode(Func<string?> read, string itemReference) : IOneTimeCodeSource
    {
        public string Current()
        {
            var code = read() ?? "";
            return code.Length == 6 && code.All(char.IsAsciiDigit)
                ? code
                : throw new LoginAbortedException($"the OTP field of {itemReference} did not give a six-digit code (code not shown).");
        }
    }

    string Field(OnePasswordItem item, string label)
    {
        var matches = item.Fields.Where(f => string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase)).ToList();
        var outside = matches.Where(f => f.Section is null).ToList();
        var chosen = outside.Count > 0 ? outside : matches;
        if (chosen.Count > 1)
            throw new LoginAbortedException($"{_item} has {chosen.Count} fields '{label}' ({Places(chosen)}); rename all but one.");
        if (chosen.Count == 0)
            throw new LoginAbortedException(
                item.UrlLabels.Contains(label, StringComparer.OrdinalIgnoreCase)
                    ? $"{_item} has '{label}' as a website entry, not a field; add it as a text field instead."
                    : $"{_item} has no field '{label}'.");
        var value = chosen[0].Value;
        return value.Length > 0 ? value : throw new LoginAbortedException($"field '{label}' of {_item} is empty.");
    }
}

/// <summary>
/// The <c>op</c> CLI. Unattended use needs a service account (<c>OP_SERVICE_ACCOUNT_TOKEN</c> in the environment)
/// with access to the item's vault; <c>op</c> picks the token up itself.
/// </summary>
sealed class OpCli : IOnePasswordCli
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public OnePasswordItem GetItem(string vault, string item)
    {
        var start = new ProcessStartInfo("op")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "item", "get", item, "--vault", vault, "--format", "json" })
            start.ArgumentList.Add(arg);
        var what = $"op item get '{item}' --vault '{vault}'";

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            throw new LoginAbortedException("1Password CLI 'op' not found on PATH.");
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                throw new LoginAbortedException($"{what} timed out.");
            }
            // op's stderr describes the failure (no such item, not signed in); it never holds the secrets.
            if (process.ExitCode != 0)
                throw new LoginAbortedException($"{what} failed ({process.ExitCode}): {stderr.Result.Trim()}");
            try
            {
                return OnePasswordItem.FromJson(stdout.Result);
            }
            catch (JsonException)
            {
                // The output holds the secrets, so it is not shown.
                throw new LoginAbortedException($"{what} did not return item JSON.");
            }
        }
    }
}
