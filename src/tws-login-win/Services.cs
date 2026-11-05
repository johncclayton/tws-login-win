namespace tws_login_win;

public interface ILog
{
    void Info(string message);
}

public interface IClock
{
    DateTimeOffset Now { get; }

    void Sleep(TimeSpan duration);
}

public static class ClockExtensions
{
    /// <summary>
    /// Checks <paramref name="done"/> every <paramref name="interval"/> until it holds; false when it still does not
    /// after <paramref name="limit"/>.
    /// </summary>
    public static bool WaitFor(this IClock clock, Func<bool> done, TimeSpan limit, TimeSpan interval)
    {
        var deadline = clock.Now + limit;
        while (true)
        {
            if (done())
                return true;
            if (clock.Now >= deadline)
                return false;
            clock.Sleep(interval);
        }
    }
}

/// <summary>A top-level window owned by the TWS process <see cref="ProcessId"/>.</summary>
public sealed record TopLevelWindow(IntPtr Handle, string Title, string ClassName, bool Visible, int ProcessId);

/// <summary>The Windows desktop as TWS automation sees it: only the top-level windows of TWS processes.</summary>
public interface IDesktop
{
    IReadOnlyList<TopLevelWindow> TwsWindows();
}

/// <summary>
/// TWS's controls as the Java Access Bridge exposes them: read a window's controls, press a button, set a field's text.
/// No mouse, no keys, no foreground: the bridge acts on the control itself.
/// </summary>
public interface IAccessBridge
{
    /// <summary>
    /// The window's controls as they are now; null when the window has no accessible context: it has closed, or the
    /// bridge is off (not enabled with jabswitch, or TWS not restarted since).
    /// </summary>
    ControlTree? Read(IntPtr window);

    /// <summary>Runs the control's "click" action, as a mouse click on it would. False when the bridge refused.</summary>
    bool Press(Control control);

    /// <summary>Replaces the field's whole text. False when the bridge refused.</summary>
    bool SetText(Control control, string text);
}

/// <summary>What a form or dialog needs: the TWS windows, their controls, time, and the log.</summary>
public sealed record TwsTools(IDesktop Desktop, IAccessBridge Bridge, IClock Clock, ILog Log)
{
    /// <summary>The TWS windows on the desktop now.</summary>
    public IReadOnlyList<TwsWindow> Windows() => Desktop.TwsWindows().Select(w => new TwsWindow(w, this)).ToList();
}
