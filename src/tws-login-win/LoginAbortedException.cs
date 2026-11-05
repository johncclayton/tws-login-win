namespace tws_login_win;

/// <summary>
/// An expected reason to stop before (or instead of) sending more input to TWS. The message is shown to the
/// operator, so it must never contain a credential.
/// </summary>
public sealed class LoginAbortedException(string message) : Exception(message);
