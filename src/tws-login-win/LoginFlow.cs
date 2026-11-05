namespace tws_login_win;

/// <summary>
/// The TWS Login form as the login flow drives it. Every step confirms its effect on the form before returning, and
/// anything that cannot be done safely throws <see cref="LoginAbortedException"/> instead.
/// </summary>
public interface ILoginForm
{
    /// <summary>Selects the tab for <paramref name="mode"/>; confirms it and the paper banner (shown for paper only).</summary>
    void SelectTab(TradingMode mode);

    /// <summary>Replaces the field's text with <paramref name="value"/> and confirms the field holds it.</summary>
    void Fill(LoginField field, string value);

    /// <summary>
    /// Presses Log In once it is enabled and the form has settled; what follows is watched from this moment. With a
    /// <paramref name="code"/> source the code screen is filled in; without one it is left to the user.
    /// </summary>
    IAfterLogIn LogIn(TradingMode mode, IOneTimeCodeSource? code);
}

/// <summary>What happens after Log In, followed for the Login window's own TWS.</summary>
public interface IAfterLogIn
{
    /// <summary>
    /// Returns once this TWS's main window shows (and, for paper, its warning has been accepted). Answers the known
    /// dialogs on the way, enters the code on the code screen when it has a code source, and otherwise waits for the
    /// user on the second-factor screens; anything else aborts with <see cref="LoginAbortedException"/>.
    /// </summary>
    void WaitUntilLoggedIn(TimeSpan limit);
}

public interface ILoginFormLocator
{
    /// <summary>Waits for a visible Login window whose form is ready.</summary>
    ILoginForm WaitForForm(TimeSpan timeout);

    /// <summary>
    /// The visible Login window's form, judged once without waiting; stops when the window shows anything but the
    /// Login form.
    /// </summary>
    ILoginForm ExistingForm();
}

/// <param name="LoginForm">How long to wait for the Login form.</param>
/// <param name="LoggedIn">How long from Log In until the main window shows.</param>
public sealed record LoginTimeouts(TimeSpan LoginForm, TimeSpan LoggedIn)
{
    public static readonly LoginTimeouts Default = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3));
}

/// <summary>
/// Logs TWS in: select the tab, fill both fields, press Log In only when every check passed, then follow what TWS
/// shows until it is logged in. Decides what to do; <see cref="ILoginForm"/> knows how.
/// </summary>
public sealed class LoginFlow(ILoginFormLocator locator, ILog log)
{
    /// <param name="existing">
    /// The Login window is already showing and is judged once, not waited for (<c>login --existing</c>).
    /// </param>
    public void Run(TwsCredentials credentials, LoginTimeouts timeouts, bool existing = false)
    {
        var form = existing ? locator.ExistingForm() : locator.WaitForForm(timeouts.LoginForm);
        form.SelectTab(credentials.Mode);
        form.Fill(LoginField.User, credentials.User);
        form.Fill(LoginField.Password, credentials.Password);
        var afterLogIn = form.LogIn(credentials.Mode, credentials.OneTimeCode);
        afterLogIn.WaitUntilLoggedIn(timeouts.LoggedIn);
        log.Info($"{credentials.Mode.ToString().ToLowerInvariant()} login complete");
    }
}
