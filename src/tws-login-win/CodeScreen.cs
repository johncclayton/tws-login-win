namespace tws_login_win;

/// <summary>
/// The code screen: the second-factor screen of the Login window that asks for the Mobile Authenticator code.
/// Sets the code in the code box, reads it back, and presses OK, once. Never presses Cancel, never changes the
/// security device, never presses "Enter Read Only". The code is never logged.
/// </summary>
public sealed class CodeScreen(TwsWindow window, TwsTools tools)
{
    /// <summary>The label above the code box.</summary>
    public const string Label = "Enter Mobile Authenticator app code";

    /// <summary>A Mobile Authenticator (TOTP) code is valid for one 30 s period, counted from the Unix epoch.</summary>
    static readonly TimeSpan CodePeriod = TimeSpan.FromSeconds(30);

    /// <summary>A code with less time left than this may change before TWS checks it, so the next one is used.</summary>
    static readonly TimeSpan MinCodeLeft = TimeSpan.FromSeconds(5);

    /// <summary>Waited past the change of period, so a clock slightly behind 1Password's still gets the new code.</summary>
    static readonly TimeSpan ClockMargin = TimeSpan.FromSeconds(1);

    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan ReactionLimit = TimeSpan.FromSeconds(3);

    /// <summary>The Login window shows the code screen: its label and code box.</summary>
    public static bool Shows(ControlTree tree) => tree.Find(Roles.Label, Label).Count == 1 && CodeBox(tree) is not null;

    /// <summary>The code screen with an empty code box: TWS asks for a code, not still working on one entered.</summary>
    public static bool AsksForCode(ControlTree tree) => Shows(tree) && CodeBox(tree)!.TextLength == 0;

    public void Enter(IOneTimeCodeSource source)
    {
        var code = Fetch(source);
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit))
            throw new LoginAbortedException($"the one-time code from the code source is not 6 digits ({code?.Length ?? 0} chars); nothing set.");

        using (var tree = window.Controls())
        {
            var box = CodeBox(tree) ?? throw new LoginAbortedException($"no single code box below '{Label}' on {window}.");
            tools.Log.Info($"code box {box} holds {box.TextLength} chars");
            window.SetText(box, code, "the code box");
        }

        // The code box is a plain text field, so the digits are compared in full before OK.
        string shown = "no box";
        if (!window.WaitFor(tree =>
            {
                var box = CodeBox(tree);
                shown = box?.TextLength.ToString() ?? "no box";
                return box?.Text == code;
            }, ReactionLimit, PollInterval))
            throw new LoginAbortedException($"the code box does not show the code set ({shown} chars); OK not pressed.");

        using (var tree = window.Controls())
        {
            var ok = tree.Button("OK") ?? throw new LoginAbortedException($"no single OK button on the code screen of {window}.");
            window.Press(ok, "'OK'");
        }
        tools.Log.Info($"code entered ({code.Length} digits, read back) and OK pressed");
    }

    /// <summary>The one showing, editable, single-line field: the code box.</summary>
    static Control? CodeBox(ControlTree tree) => tree.One(c => c.IsTextField);

    /// <summary>
    /// A code with at least <see cref="MinCodeLeft"/> to go once fetched. 1Password takes 1-3 s, so the time left is
    /// checked again after the fetch; a code fetched too late is dropped and the next one fetched, once.
    /// </summary>
    string? Fetch(IOneTimeCodeSource source)
    {
        WaitIfCodeAboutToChange();
        var code = source.Current();
        if (CodeLeft() >= MinCodeLeft)
            return code;
        tools.Log.Info($"code fetched with {CodeLeft().TotalSeconds:0.0}s left; fetching the next one");
        WaitIfCodeAboutToChange();
        return source.Current();
    }

    void WaitIfCodeAboutToChange()
    {
        var left = CodeLeft();
        if (left >= MinCodeLeft)
            return;
        tools.Log.Info($"code changes in {left.TotalSeconds:0.0}s; waiting for the next one");
        tools.Clock.Sleep(left + ClockMargin);
    }

    TimeSpan CodeLeft() =>
        CodePeriod - TimeSpan.FromMilliseconds(tools.Clock.Now.ToUnixTimeMilliseconds() % (long)CodePeriod.TotalMilliseconds);
}
