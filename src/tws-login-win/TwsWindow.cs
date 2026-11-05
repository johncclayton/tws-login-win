namespace tws_login_win;

/// <summary>The Java window classes of TWS's top-level windows.</summary>
public static class WindowClasses
{
    /// <summary>The Login window, the main window and TWS's other frames.</summary>
    public const string Frame = "SunAwtFrame";

    public const string Dialog = "SunAwtDialog";
}

/// <summary>
/// One TWS window that has been found: reads its controls and acts on them through the Java Access Bridge.
/// <list type="bullet">
/// <item>Controls are found by role, name and state, never by position: window size, display scaling and layout
/// shifts do not matter.</item>
/// <item>Input goes to the control itself, so TWS need not be in front and the user's mouse and keyboard are never
/// touched.</item>
/// <item>A control is only pressed while enabled; a refused action stops the login.</item>
/// <item>Field contents are never logged here: callers decide what to log, by length.</item>
/// </list>
/// </summary>
public sealed class TwsWindow(TopLevelWindow found, TwsTools tools)
{
    /// <summary>The fix when the bridge sees no controls, for abort messages.</summary>
    public const string BridgeHelp =
        "the Java Access Bridge is off: run jabswitch -enable (in TWS's jre\\bin) once for this Windows user, then restart TWS";

    public IntPtr Handle => found.Handle;

    public string Title => found.Title;

    public string ClassName => found.ClassName;

    public bool Visible => found.Visible;

    public bool IsFrame => ClassName == WindowClasses.Frame;

    public bool IsDialog => ClassName == WindowClasses.Dialog;

    /// <summary>The TWS process that owns the window; live and paper TWS run side by side as separate processes.</summary>
    public int ProcessId => found.ProcessId;

    /// <summary>Still on the desktop and visible.</summary>
    public bool IsOpen => tools.Desktop.TwsWindows().Any(w => w.Handle == Handle && w.Visible);

    /// <summary>For logs: the account number in a title is redacted.</summary>
    public override string ToString() => $"{Handle} '{AccountNumbers.Redact(Title)}' pid {ProcessId}";

    /// <summary>The window's controls now; null when it has no accessible context (closed, or the bridge is off).</summary>
    public ControlTree? TryControls() => tools.Bridge.Read(Handle);

    /// <summary>The window's controls now; stops the login when they cannot be read.</summary>
    public ControlTree Controls() =>
        TryControls() ?? throw new LoginAbortedException($"no controls readable in {this}: {(IsOpen ? BridgeHelp : "it closed")}.");

    /// <summary>
    /// Reads the controls every <paramref name="interval"/> until <paramref name="done"/> holds; false when it still
    /// does not after <paramref name="limit"/>.
    /// </summary>
    public bool WaitFor(Func<ControlTree, bool> done, TimeSpan limit, TimeSpan interval) =>
        tools.Clock.WaitFor(() =>
        {
            using var tree = Controls();
            return done(tree);
        }, limit, interval);

    /// <summary>Presses <paramref name="control"/>, which must be enabled; <paramref name="what"/> names it in logs.</summary>
    public void Press(Control control, string what)
    {
        if (!control.Enabled)
            throw new LoginAbortedException($"{what} is not enabled on {this}; not pressed.");
        if (!tools.Bridge.Press(control))
            throw new LoginAbortedException($"the bridge refused to press {what} on {this}.");
        tools.Log.Info($"pressed {what} {control} on {this}");
    }

    /// <summary>Replaces the text of <paramref name="field"/>; <paramref name="what"/> names it in logs. The text is never logged.</summary>
    public void SetText(Control field, string text, string what)
    {
        if (!field.Enabled || !field.Editable)
            throw new LoginAbortedException($"{what} is not an enabled, editable field on {this}; nothing set.");
        if (!tools.Bridge.SetText(field, text))
            throw new LoginAbortedException($"the bridge refused to set {what} on {this}.");
        tools.Log.Info($"set {what} ({text.Length} chars) on {this}");
    }
}
