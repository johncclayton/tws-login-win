namespace tws_login_win;

/// <summary>What a Login window shows, as <c>login-windows</c> and <c>login --existing</c> judge it.</summary>
public enum LoginScreen
{
    /// <summary>The Login form, ready to fill.</summary>
    Form,

    /// <summary>A second-factor screen: known by the window's title alone.</summary>
    SecondFactor,

    /// <summary>Anything else, e.g. "Authenticating..." while TWS works on a Log In.</summary>
    Other,

    /// <summary>The window's controls cannot be read: the Java Access Bridge is off or unavailable, or it is closing.</summary>
    Unknown,
}

/// <summary>
/// The visible Login windows of every TWS on the desktop, each with its process and what it shows. Only reads: a
/// form's fields are not read and nothing is sent.
/// </summary>
public static class LoginWindows
{
    /// <summary>The visible Login windows: frames titled for the Login form or a second-factor screen. TWS's invisible second Login window is left out.</summary>
    public static IReadOnlyList<TwsWindow> Showing(TwsTools tools) =>
        tools.Windows()
            .Where(w => w is { IsFrame: true, Visible: true, Title: LoginFormText.WindowTitle or LoginFormText.SecondFactorTitle })
            .ToList();

    public static LoginScreen Screen(TwsWindow window)
    {
        if (window.Title == LoginFormText.SecondFactorTitle)
            return LoginScreen.SecondFactor;
        ControlTree? tree;
        try
        {
            tree = window.TryControls();
        }
        catch (Exception ex) when (ex is LoginAbortedException or DllNotFoundException or EntryPointNotFoundException
                                       or BadImageFormatException)
        {
            return LoginScreen.Unknown; // no bridge to load from this TWS
        }
        using (tree)
            return tree is null ? LoginScreen.Unknown : LoginForm.Shows(tree) ? LoginScreen.Form : LoginScreen.Other;
    }

    /// <summary>One line per visible Login window: <c>&lt;pid&gt; &lt;form|second-factor|other|unknown&gt;</c>.</summary>
    public static IReadOnlyList<string> Lines(TwsTools tools) =>
        Showing(tools).Select(w => $"{w.ProcessId} {Name(Screen(w))}").ToList();

    public static string Name(LoginScreen screen) => screen switch
    {
        LoginScreen.Form => "form",
        LoginScreen.SecondFactor => "second-factor",
        LoginScreen.Other => "other",
        _ => "unknown",
    };
}
