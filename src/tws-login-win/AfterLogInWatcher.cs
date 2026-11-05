namespace tws_login_win;

/// <summary>
/// Follows one TWS after its Log In was pressed, until it is logged in: its main window shows and, for paper, its
/// paper warning has been accepted. TWS 1051 shows the paper warning after the main window, but either order is
/// accepted. Sees only windows of the Login window's process: live and paper TWS run side by side.
/// With a <paramref name="code"/> source it enters the code on the code screen, once per login attempt; without one
/// it waits for the user.
/// </summary>
public sealed class AfterLogInWatcher(TwsWindow login, TwsTools tools, TradingMode mode, IOneTimeCodeSource? code)
    : IAfterLogIn
{
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a new dialog may take to show its text before it is judged by it.</summary>
    static readonly TimeSpan PaintPatience = TimeSpan.FromSeconds(2);

    /// <summary>How soon after the press the Login form must change: tabs gone, or the window retitled.</summary>
    static readonly TimeSpan LogInTaken = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long after OK on the code screen an empty code box may still show (TWS clearing it as it works) before it
    /// counts as TWS asking for the code again.
    /// </summary>
    static readonly TimeSpan CodeTaken = TimeSpan.FromSeconds(10);

    /// <summary>How long the Login window may show the same controls before the login counts as stuck.</summary>
    static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(60);

    /// <summary>How long the paper warning may take to close once its accept button is pressed.</summary>
    static readonly TimeSpan WarningCloses = TimeSpan.FromSeconds(5);

    /// <summary>How many times "Re-login is required" is answered in one run; once more aborts, so it never fights another session.</summary>
    const int MaxRelogins = 1;

    readonly Dictionary<IntPtr, DateTimeOffset> _dialogsSeen = [];
    readonly HashSet<IntPtr> _dialogsDone = [];
    readonly HashSet<IntPtr> _framesSeen = [];
    int _relogins;
    string? _secondFactorScreen;
    DateTimeOffset? _codeEntered;
    bool _warningAccepted;
    TwsWindow? _main;
    bool _loginFormGone;
    string? _lastSignature;
    DateTimeOffset _lastChange = tools.Clock.Now;

    readonly DateTimeOffset _pressed = tools.Clock.Now;

    public void WaitUntilLoggedIn(TimeSpan limit)
    {
        while (true)
        {
            if (tools.Clock.Now - _pressed >= limit)
                throw new LoginAbortedException(_main is null
                    ? $"not logged in within {limit.TotalSeconds:0}s of Log In."
                    : $"main window up, but no paper warning to accept within {limit.TotalSeconds:0}s of Log In.");
            var mine = tools.Windows().Where(w => w.ProcessId == login.ProcessId && w.Visible).ToList();
            foreach (var dialog in mine.Where(w => w.IsDialog))
                AnswerDialog(dialog);
            if (_main is null && mine.FirstOrDefault(IsMainWindow) is { } main)
            {
                _main = main;
                tools.Log.Info($"main window {main}");
            }
            if (_main is not null && (mode == TradingMode.Live || _warningAccepted))
            {
                tools.Log.Info("logged in");
                return;
            }
            // Other frames ("Trades", ...) show account data: logged by title only, never read.
            foreach (var frame in mine.Where(w => w.Handle != login.Handle && !w.IsDialog && w.Handle != _main?.Handle))
                if (_framesSeen.Add(frame.Handle))
                    tools.Log.Info($"window {frame} class={frame.ClassName}; ignored");
            // Once the main window is up the Login window has done its part; only the paper warning is awaited.
            if (_main is null)
                FollowLoginWindow(mine.FirstOrDefault(w => w.Handle == login.Handle));
            tools.Clock.Sleep(PollInterval);
        }
    }

    /// <summary>Follows the Login window (null once hidden or closed) through the screens TWS shows in it.</summary>
    void FollowLoginWindow(TwsWindow? window)
    {
        using var tree = window?.TryControls();
        if (window is not null && tree is null)
            return; // closing or between screens; judged on the next poll
        bool formShows = window is { Title: LoginFormText.WindowTitle } && LoginForm.Shows(tree!);
        if (formShows && _loginFormGone)
            throw new LoginAbortedException("the Login form is back after Log In (login rejected or cancelled); not filling it again.");
        if (!formShows)
            _loginFormGone = true;
        if (!_loginFormGone && tools.Clock.Now - _pressed >= LogInTaken)
            throw new LoginAbortedException($"Log In was not taken: the Login form still shows {LogInTaken.TotalSeconds:0}s after the press.");
        if (window is null)
            return;

        var signature = window.Title + "|" + tree!.Signature();
        if (signature != _lastSignature)
        {
            _lastSignature = signature;
            _lastChange = tools.Clock.Now;
        }

        // The second-factor screens wait on a code or the user (a phone approval), so they are never a stall.
        if (window.Title == LoginFormText.SecondFactorTitle)
        {
            if (_codeEntered is { } entered)
            {
                // One attempt only: the code screen asking again (empty box) means the code was not accepted. A
                // rejected code normally shows as a "Login Error" dialog instead, answered in AnswerDialog.
                // The title alone proves nothing: TWS can keep "Second Factor Authentication" well after it accepted
                // the code, while it loads (seen 2026-11-02); that is waited on, up to the login limit.
                if (CodeScreen.AsksForCode(tree) && tools.Clock.Now - entered >= CodeTaken)
                    throw new LoginAbortedException(
                        $"code not taken: the code screen asks for a code again {CodeTaken.TotalSeconds:0}s or more after OK; not entering the code again.");
                if (_secondFactorScreen is null && tools.Clock.Now - entered >= CodeTaken)
                {
                    _secondFactorScreen = "after OK";
                    tools.Log.Info($"the Login window is still titled '{window.Title}' {CodeTaken.TotalSeconds:0}s after OK " +
                                   $"({(CodeScreen.Shows(tree) ? "code screen, code still in the box" : "not the code screen")}); waiting for TWS");
                }
                return;
            }
            if (CodeScreen.Shows(tree) && code is not null)
            {
                new CodeScreen(window, tools).Enter(code);
                _codeEntered = tools.Clock.Now;
                return;
            }
            WaitForUser(tree);
            return;
        }
        _secondFactorScreen = null;
        if (tools.Clock.Now - _lastChange >= StallLimit)
            throw new LoginAbortedException($"the Login window {window} has been unchanged for {StallLimit.TotalSeconds:0}s.");
    }

    /// <summary>Logs once per second-factor screen that it waits for the user. Sends nothing.</summary>
    void WaitForUser(ControlTree tree)
    {
        string screen;
        if (CodeScreen.Shows(tree))
            screen = "code screen: enter the Mobile Authenticator code in TWS";
        else if (tree.Showing.FirstOrDefault(c => c.Role == Roles.List) is { } devices)
        {
            var selected = devices.Children.FirstOrDefault(c => c.Selected)?.Name.Trim() ?? "none";
            screen = $"device choice: '{selected}' selected; choose in TWS";
        }
        else
            screen = "second-factor screen: approve or choose in TWS";
        if (screen == _secondFactorScreen)
            return;
        _secondFactorScreen = screen;
        tools.Log.Info($"{screen}; waiting for you");
    }

    /// <summary>
    /// Answers each known dialog once and then leaves it alone until it closes. A dialog that still says something
    /// unknown after a while aborts, unless the main window is already up: then it is logged once and ignored.
    /// </summary>
    void AnswerDialog(TwsWindow dialog)
    {
        if (_dialogsDone.Contains(dialog.Handle))
            return;
        var firstSeen = _dialogsSeen.TryGetValue(dialog.Handle, out var seen) ? seen : _dialogsSeen[dialog.Handle] = tools.Clock.Now;
        using var tree = dialog.TryControls();
        if (tree is null)
            return; // closing; or not readable yet
        var reading = Dialogs.Read(tree);
        switch (reading.Kind)
        {
            case DialogKind.PaperWarning:
                AcceptPaperWarning(dialog, reading);
                _warningAccepted = true;
                _dialogsDone.Add(dialog.Handle);
                return;
            case DialogKind.ExistingSession:
                tools.Log.Info($"existing-session dialog {dialog}: pressing '{Dialogs.ContinueLogin}', which disconnects the other session");
                dialog.Press(reading.Button!, $"'{Dialogs.ContinueLogin}'");
                _dialogsDone.Add(dialog.Handle);
                return;
            case DialogKind.Relogin:
                if (_relogins >= MaxRelogins)
                    throw new LoginAbortedException(
                        $"'{reading.MaskedMessage}' again after pressing '{Dialogs.Relogin}' {_relogins} time(s); not pressing it again. " +
                        "Another session (phone, browser) may be taking the login back.");
                _relogins++;
                tools.Log.Info($"re-login dialog {dialog}: '{reading.MaskedMessage}'; pressing '{Dialogs.Relogin}' ({_relogins} of {MaxRelogins})");
                dialog.Press(reading.Button!, $"'{Dialogs.Relogin}'");
                _dialogsDone.Add(dialog.Handle);
                // A new login attempt: its code screen may need the code again. What TWS shows after Re-login has not
                // been seen yet; should it be the Login form, the login aborts as "Login form is back".
                _codeEntered = null;
                _lastChange = tools.Clock.Now;
                return;
            case DialogKind.LoginError:
                throw new LoginAbortedException($"TWS refused the login: '{reading.MaskedMessage}' ({dialog}); nothing pressed.");
        }
        if (tools.Clock.Now - firstSeen < PaintPatience)
            return;
        // Once the main window is up TWS is logged in, and startup dialogs are its business; only the paper warning
        // is still awaited.
        if (_main is not null)
        {
            _dialogsDone.Add(dialog.Handle);
            tools.Log.Info($"dialog {dialog} after the main window: '{reading.MaskedMessage}'; ignored");
            return;
        }
        throw new LoginAbortedException($"unrecognised dialog {dialog} after Log In: '{reading.MaskedMessage}'; nothing pressed.");
    }

    /// <summary>Presses the paper warning's accept button and waits for the warning to close.</summary>
    void AcceptPaperWarning(TwsWindow dialog, DialogReading reading)
    {
        tools.Log.Info($"paper warning {dialog}: '{reading.MaskedMessage}'");
        dialog.Press(reading.Button!, $"'{reading.Button!.Name}'");
        if (!tools.Clock.WaitFor(() => !dialog.IsOpen, WarningCloses, TimeSpan.FromMilliseconds(250)))
            throw new LoginAbortedException("paper warning still open after pressing its accept button.");
        tools.Log.Info("paper warning dismissed");
    }

    static bool IsMainWindow(TwsWindow w) => w.IsFrame && w.Title.Contains("Interactive Brokers", StringComparison.Ordinal);
}
