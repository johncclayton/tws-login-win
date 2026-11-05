namespace tws_login_win;

public enum DialogKind
{
    /// <summary>After a paper login: "not a brokerage account", accepted with "I understand and accept".</summary>
    PaperWarning,

    /// <summary>The same user name is logged in elsewhere: "Continue Login" disconnects that session.</summary>
    ExistingSession,

    /// <summary>"Re-login is required": the connection was lost, e.g. to another session; "Re-login" logs in again.</summary>
    Relogin,

    /// <summary>"Login Error": TWS refused the login, e.g. "Login failed - incorrect security code". Only OK to answer.</summary>
    LoginError,

    Unknown,
}

/// <param name="Button">The button that answers the dialog the way the login wants; null when none should be pressed.</param>
public sealed record DialogReading(DialogKind Kind, string Message, Control? Button)
{
    /// <summary>For logs and abort messages: TWS's own words, digits masked.</summary>
    public string MaskedMessage => ControlTree.MaskDigits(Message);
}

/// <summary>
/// Recognises TWS dialogs by their text and button, never by title: TWS gives many unrelated dialogs the same title.
/// </summary>
public static class Dialogs
{
    public const string ContinueLogin = "Continue Login";
    public const string Relogin = "Re-login";
    const string PaperWarningBody = "not a brokerage account";
    const string PaperWarningAccept = "accept";
    const string ExistingSessionBody = "same user name already exists";
    const string ReloginBody = "connection was lost";
    const string LoginErrorBody = "Login failed";
    const string LoginErrorOk = "OK";

    public static DialogReading Read(ControlTree tree)
    {
        var message = tree.Message();
        bool Says(string body) => message.Contains(body, StringComparison.OrdinalIgnoreCase);
        if (Says(PaperWarningBody) && PaperWarningButton(tree) is { } accept)
            return new(DialogKind.PaperWarning, message, accept);
        if (Says(ExistingSessionBody) && tree.Button(ContinueLogin) is { } continueLogin)
            return new(DialogKind.ExistingSession, message, continueLogin);
        if (Says(ReloginBody) && tree.Button(Relogin) is { } relogin)
            return new(DialogKind.Relogin, message, relogin);
        if (Says(LoginErrorBody) && tree.Button(LoginErrorOk) is not null)
            return new(DialogKind.LoginError, message, null);
        return new(DialogKind.Unknown, message, null);
    }

    /// <summary>The paper warning's accept button, "I understand and accept": the one showing push button whose name contains "accept".</summary>
    static Control? PaperWarningButton(ControlTree tree) =>
        tree.One(c => c.Role == Roles.PushButton && c.Name.Contains(PaperWarningAccept, StringComparison.OrdinalIgnoreCase));
}
