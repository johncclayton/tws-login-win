namespace tws_login_win;

public enum LoginField
{
    User,
    Password,
}

/// <summary>What the TWS 1051 Login form calls its parts, as the Java Access Bridge reports them.</summary>
public static class LoginFormText
{
    /// <summary>The Login window's title while it shows the Login form; TWS retitles it for the second-factor screens.</summary>
    public const string WindowTitle = "Login";

    /// <summary>The Login window's title for every second-factor screen.</summary>
    public const string SecondFactorTitle = "Second Factor Authentication";

    public static string TabLabel(TradingMode mode) => mode == TradingMode.Paper ? "Paper Trading" : "Live Trading";

    public static string LogInLabel(TradingMode mode) => mode == TradingMode.Paper ? "Paper Log In" : "Log In";

    /// <summary>The banner label shown while the Paper Trading tab is selected.</summary>
    public const string PaperBanner = "SIMULATED TRADING";
}

/// <summary>
/// The Login form of one TWS Login window, read and driven through the Java Access Bridge.
/// The user name field's text is read back only to compare it; neither field's text is ever logged.
/// </summary>
public sealed class LoginForm(TwsWindow window, TwsTools tools) : ILoginForm
{
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>How long TWS may take to show the effect of a press or a set text.</summary>
    static readonly TimeSpan ReactionLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long the form must stay unchanged, Log In enabled, before Log In is pressed. Log In enables as soon as both
    /// fields hold any text, e.g. after the first typed character, so enabled alone does not mean the form is done.
    /// </summary>
    static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    public TwsWindow Window => window;

    /// <summary>The Login form shows: both tabs are there. Its fields are not read.</summary>
    public static bool Shows(ControlTree tree) =>
        tree.Find(Roles.ToggleButton, LoginFormText.TabLabel(TradingMode.Live)).Count == 1
        && tree.Find(Roles.ToggleButton, LoginFormText.TabLabel(TradingMode.Paper)).Count == 1;

    public void SelectTab(TradingMode mode)
    {
        var label = LoginFormText.TabLabel(mode);
        using (var tree = window.Controls())
        {
            var tab = Single(tree, Roles.ToggleButton, label);
            if (tab.Checked)
                tools.Log.Info($"'{label}' already selected");
            else
                window.Press(tab, $"'{label}'");
        }
        bool paper = mode == TradingMode.Paper;
        Await($"'{label}' selected, with the {LoginFormText.PaperBanner} banner {(paper ? "showing" : "hidden")}", tree =>
            Single(tree, Roles.ToggleButton, label).Checked && ShowsPaperBanner(tree) == paper);
    }

    public void Fill(LoginField field, string value)
    {
        var name = field == LoginField.User ? "user name" : "password";
        using (var tree = window.Controls())
        {
            var control = Field(tree, field);
            tools.Log.Info($"{name} field {control} holds {control.TextLength} chars");
            // Setting replaces the whole text: nothing to clear first, so no Delete on an empty field and no beep.
            window.SetText(control, value, $"the {name} field");
        }
        // The password cannot be read back, only counted; the user name is compared in full.
        Await($"the {name} field holds the {value.Length} characters set", tree =>
        {
            var control = Field(tree, field);
            return field == LoginField.Password ? control.TextLength == value.Length : control.Text == value;
        });
    }

    public IAfterLogIn LogIn(TradingMode mode, IOneTimeCodeSource? code)
    {
        var label = LoginFormText.LogInLabel(mode);
        string? last = null;
        var steadySince = tools.Clock.Now;
        bool enabled = false;
        bool pressed = tools.Clock.WaitFor(() =>
        {
            using var tree = window.Controls();
            var button = Single(tree, Roles.PushButton, label);
            enabled = button.Enabled;
            var signature = tree.Signature();
            if (signature != last)
            {
                last = signature;
                steadySince = tools.Clock.Now;
                return false;
            }
            if (!enabled || tools.Clock.Now - steadySince < Settle)
                return false;
            window.Press(button, $"'{label}'");
            return true;
        }, ReactionLimit + Settle, PollInterval);
        if (!pressed)
            throw new LoginAbortedException(enabled
                ? $"the Login form kept changing; '{label}' not pressed."
                : $"'{label}' is not enabled; not pressed.");
        return new AfterLogInWatcher(window, tools, mode, code);
    }

    /// <summary>The user name field (the one editable single-line text field) or the password field below it.</summary>
    static Control Field(ControlTree tree, LoginField field)
    {
        var users = tree.Showing.Where(c => c.IsTextField).ToList();
        var passwords = tree.Showing.Where(c => c.Role == Roles.PasswordText).ToList();
        if (users is not [var user] || passwords is not [var password])
            throw new LoginAbortedException(
                $"expected one user name and one password field on the Login form, found {users.Count} and {passwords.Count}.");
        if (user.Bounds.Top >= password.Bounds.Top)
            throw new LoginAbortedException("the editable text field is not above the password field; not the Login form as known.");
        return field == LoginField.User ? user : password;
    }

    static bool ShowsPaperBanner(ControlTree tree) =>
        tree.Showing.Any(c => c.Role == Roles.Label && c.Name.Contains(LoginFormText.PaperBanner, StringComparison.Ordinal));

    static Control Single(ControlTree tree, string role, string name) =>
        tree.One(c => c.Role == role && c.Name == name)
            ?? throw new LoginAbortedException($"expected one showing {role} '{name}' on the Login form, found {tree.Find(role, name).Count}.");

    /// <summary>Polls the form until <paramref name="done"/> holds; stops the login after <see cref="ReactionLimit"/>.</summary>
    void Await(string what, Func<ControlTree, bool> done)
    {
        if (!window.WaitFor(done, ReactionLimit, PollInterval))
            throw new LoginAbortedException($"not confirmed within {ReactionLimit.TotalSeconds:0}s: {what}.");
        tools.Log.Info($"confirmed: {what}");
    }
}

/// <summary>
/// Finds the TWS Login window: the one visible frame titled "Login" (TWS also keeps an invisible second one). With
/// more than one showing, which TWS to log in would be a guess, so it stops.
/// </summary>
public sealed class LoginFormLocator(TwsTools tools) : ILoginFormLocator
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a Login window may show without readable controls before the bridge is judged off.</summary>
    static readonly TimeSpan BridgePatience = TimeSpan.FromSeconds(10);

    /// <summary>The Login window's form; null when no Login window shows.</summary>
    public LoginForm? FindNow()
    {
        var logins = tools.Windows().Where(w => w is { Title: LoginFormText.WindowTitle, IsFrame: true, Visible: true }).ToList();
        return logins switch
        {
            [] => null,
            [var window] => new LoginForm(window, tools),
            _ => throw new LoginAbortedException(
                $"{logins.Count} TWS Login windows show ({string.Join(", ", logins)}); log in one TWS at a time."),
        };
    }

    public ILoginForm ExistingForm()
    {
        var window = LoginWindows.Showing(tools) switch
        {
            [] => throw new LoginAbortedException("no visible TWS Login window."),
            [var one] => one,
            var logins => throw new LoginAbortedException(
                $"{logins.Count} TWS Login windows show ({string.Join(", ", logins)}); log in one TWS at a time."),
        };
        tools.Log.Info($"login window {window}");
        var shows = LoginWindows.Screen(window) switch
        {
            LoginScreen.Form => null,
            LoginScreen.SecondFactor => "a second-factor screen",
            LoginScreen.Other => "another screen, not the Login form",
            _ => "nothing the Java Access Bridge can read",
        };
        if (shows is not null)
            throw new LoginAbortedException(
                $"the Login window is not on the Login form (it shows {shows}); finish or cancel it in TWS, then try the login again.");
        tools.Log.Info("login form ready");
        return new LoginForm(window, tools);
    }

    public ILoginForm WaitForForm(TimeSpan timeout)
    {
        IntPtr reported = IntPtr.Zero;
        DateTimeOffset? unreadableSince = null;
        LoginForm? ready = null;
        bool found = tools.Clock.WaitFor(() =>
        {
            if (FindNow() is not { } form)
                return false;
            if (form.Window.Handle != reported)
            {
                tools.Log.Info($"login window {form.Window}");
                reported = form.Window.Handle;
            }
            using var tree = form.Window.TryControls();
            if (tree is null)
            {
                unreadableSince ??= tools.Clock.Now;
                if (tools.Clock.Now - unreadableSince >= BridgePatience)
                    throw new LoginAbortedException($"no controls readable in {form.Window}: {TwsWindow.BridgeHelp}.");
                return false;
            }
            if (!LoginForm.Shows(tree))
                return false;
            ready = form;
            return true;
        }, timeout, PollInterval);
        if (!found)
            throw new LoginAbortedException($"no ready TWS Login form within {timeout.TotalSeconds:0}s.");
        tools.Log.Info("login form ready");
        return ready!;
    }
}
