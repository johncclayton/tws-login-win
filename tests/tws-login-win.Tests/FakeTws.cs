using System.Drawing;
using tws_login_win;

namespace tws_login_win.Tests;

/// <summary>
/// TWS as the automation sees it, in memory: top-level windows (first = top), each with controls as the Java Access
/// Bridge would report them, a clock that only moves when slept, and a record of every press and text set.
/// </summary>
sealed class FakeTws : IDesktop, IAccessBridge, IClock, ILog
{
    /// <summary>The TWS process a window belongs to unless a test names another.</summary>
    public const int DefaultProcessId = 7001;

    public sealed class Window(IntPtr handle, string title, string className, int processId)
    {
        public IntPtr Handle { get; } = handle;
        public int ProcessId { get; } = processId;
        public string Title { get; set; } = title;
        public string ClassName { get; } = className;
        public bool Visible { get; set; } = true;
        public bool Exists { get; set; } = true;

        /// <summary>The window's controls, built afresh on every read; null reads as no accessible context.</summary>
        public Func<FakeControl>? Content { get; set; }

        /// <summary>The window closes when any of its controls is pressed, as a dialog does when answered.</summary>
        public bool ClosesOnPress { get; set; }
    }

    readonly List<Window> _windows = [];
    readonly HashSet<int> _exited = [];
    int _open;

    /// <summary>No window has an accessible context, as when jabswitch was never run or TWS was not restarted.</summary>
    public bool BridgeOff { get; set; }

    public List<string> Sent { get; } = [];

    public List<string> Logged { get; } = [];

    /// <summary>Control trees read and not yet disposed.</summary>
    public int OpenTrees => _open;

    public Action? OnSleep { get; set; }

    public DateTimeOffset Now { get; private set; } = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    public TwsTools Tools => new(this, this, this, this);

    public Window Add(string title, string className, Func<FakeControl>? content = null, int processId = DefaultProcessId)
    {
        var window = new Window(1000 + _windows.Count, title, className, processId) { Content = content };
        _windows.Insert(0, window);
        return window;
    }

    /// <summary>The TWS process exits: its windows go with it.</summary>
    public void Exit(int processId)
    {
        _exited.Add(processId);
        foreach (var window in _windows.Where(w => w.ProcessId == processId))
            window.Exists = false;
    }

    public bool HasExited(int processId) => _exited.Contains(processId);

    /// <summary>A dialog of TWS that closes once one of its buttons is pressed.</summary>
    public Window AddDialog(string title, FakeControl content, int processId = DefaultProcessId)
    {
        var dialog = Add(title, "SunAwtDialog", () => content, processId);
        dialog.ClosesOnPress = true;
        return dialog;
    }

    // IDesktop
    public IReadOnlyList<TopLevelWindow> TwsWindows() =>
        _windows.Where(w => w.Exists).Select(w => new TopLevelWindow(w.Handle, w.Title, w.ClassName, w.Visible, w.ProcessId)).ToList();

    // IAccessBridge
    public ControlTree? Read(IntPtr handle)
    {
        var window = _windows.FirstOrDefault(w => w.Handle == handle && w.Exists);
        if (BridgeOff || window?.Content is not { } content)
            return null;
        _open++;
        return new ControlTree(content().ToControl(window), () => _open--);
    }

    public bool Press(Control control)
    {
        var (fake, window) = (FakeControl.Handle)control.BridgeHandle;
        Sent.Add($"press '{fake.Name}'");
        if (fake.Refuses)
            return false;
        if (window.ClosesOnPress)
            window.Exists = false;
        fake.OnPress?.Invoke();
        return true;
    }

    public bool SetText(Control control, string text)
    {
        var (fake, _) = (FakeControl.Handle)control.BridgeHandle;
        Sent.Add($"set '{fake.Name}' {text.Length} chars");
        if (fake.Refuses)
            return false;
        fake.OnSetText?.Invoke(text);
        return true;
    }

    // IClock, ILog
    public void Sleep(TimeSpan duration)
    {
        Now += duration;
        OnSleep?.Invoke();
    }

    public void Info(string message) => Logged.Add(message);
}

/// <summary>One control as a test builds it; <see cref="ToControl"/> turns it into what the bridge would report.</summary>
sealed class FakeControl(string role, string name = "", params string[] states)
{
    public sealed record Handle(FakeControl Control, FakeTws.Window Window);

    public string Role { get; } = role;
    public string Name { get; set; } = name;
    public HashSet<string> States { get; } = ["enabled", "visible", "showing", .. states];
    public Rectangle Bounds { get; set; } = new(0, 0, 100, 30);

    /// <summary>The text of a field or of read-only text; a password field's is counted, never reported.</summary>
    public string? Text { get; set; }

    public bool Password => Role == "password text";
    public bool Refuses { get; set; }
    public List<FakeControl> Children { get; } = [];
    public Action? OnPress { get; set; }
    public Action<string>? OnSetText { get; set; }

    public FakeControl With(params FakeControl[] children)
    {
        Children.AddRange(children);
        return this;
    }

    public FakeControl Hidden()
    {
        States.Remove("showing");
        Bounds = new Rectangle(-1, -1, -1, -1);
        return this;
    }

    public FakeControl Disabled()
    {
        States.Remove("enabled");
        return this;
    }

    public FakeControl At(int y)
    {
        Bounds = new Rectangle(0, y, 100, 30);
        return this;
    }

    public Control ToControl(FakeTws.Window window) => new(
        Role, Name, States.ToHashSet(), Bounds, Text?.Length ?? 0, Password ? null : Text,
        Children.Select(c => c.ToControl(window)).ToList(), new Handle(this, window));

    public static FakeControl Label(string name) => new("label", name);

    public static FakeControl Button(string name, Action? onPress = null) => new("push button", name) { OnPress = onPress };

    /// <summary>Read-only text, as TWS shows a dialog's message.</summary>
    public static FakeControl Message(string text) => new("text", "", "multiple line") { Text = text };
}

/// <summary>The TWS 1051 screens as the Java Access Bridge showed them on 2026-10-28.</summary>
static class TwsScreens
{
    public static FakeControl Frame(string title, params FakeControl[] children) =>
        new FakeControl("frame", title).With(new FakeControl("root pane").With(new FakeControl("panel").With(children)));

    public static FakeControl Dialog(string title, params FakeControl[] children) =>
        new FakeControl("dialog", title).With(new FakeControl("root pane").With(new FakeControl("panel").With(children)));

    public static FakeControl ExistingSession() => Dialog("Existing session detected",
        FakeControl.Message("Another session with the same user name already exists.\nWould you like to login and disconnect the other session?"),
        FakeControl.Button("Continue Login"),
        FakeControl.Button("Cancel"));

    public static FakeControl Relogin() => Dialog("Re-login is required",
        FakeControl.Label("RE-LOGIN IS REQUIRED"),
        FakeControl.Message("Your connection was lost. Would you like to re-login?"),
        FakeControl.Button("Re-login"),
        FakeControl.Button("Cancel"));

    public static FakeControl LoginError(string message = "Login failed - incorrect security code") => Dialog("Login Error",
        FakeControl.Label("LOGIN ERROR"),
        FakeControl.Message(message),
        FakeControl.Button("OK"));

    /// <summary>Its text as OCR read it on 2026-10-28; accepted through the bridge on a real paper login the same day.</summary>
    public static FakeControl PaperWarning() => Dialog("Warning",
        FakeControl.Message("This is not a brokerage account. This is a paper trading account."),
        FakeControl.Button("I understand and accept"));

    /// <summary>The code screen: label, code box, OK, Cancel, and the "Enter Read Only" button that is never pressed.</summary>
    public static FakeControl CodeScreen(FakeControl box, Action? onOk = null) => Frame("Second Factor Authentication",
        FakeControl.Label("SECOND FACTOR AUTHENTICATION"),
        FakeControl.Label("Enter Mobile Authenticator app code"),
        box,
        FakeControl.Message("Change security device"),
        FakeControl.Button("OK", onOk),
        FakeControl.Button("Cancel"),
        FakeControl.Button("Enter Read Only"));

    public static FakeControl CodeBox() => new("text", "", "focusable", "focused", "editable", "single line") { Text = "" };

    public static FakeControl DeviceChoice() => Frame("Second Factor Authentication",
        FakeControl.Message("Select second factor device"),
        new FakeControl("list").With(
            new FakeControl("label", " IB Key", "selectable"),
            new FakeControl("label", " Mobile Authenticator app", "selectable", "selected")),
        FakeControl.Button("OK"),
        FakeControl.Button("Cancel"),
        FakeControl.Button("Help"));

    public static FakeControl Authenticating() => Frame("Login", FakeControl.Label("Authenticating..."));
}

/// <summary>
/// The Login form of TWS 1051 with its behaviour: tabs switch the mode and the banner, fields hold what is set, and
/// Log In is enabled once both fields hold text. Knobs make it misbehave.
/// </summary>
sealed class FakeLoginForm
{
    public TradingMode Mode { get; set; } = TradingMode.Live;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    public bool TabIgnoresPress { get; set; }
    public bool NoBanner { get; set; }
    public int PasswordCharsDropped { get; set; }
    public bool UserReadsBackWrong { get; set; }
    public bool LogInStaysDisabled { get; set; }

    /// <summary>Runs when Log In is pressed: the test decides what TWS shows next.</summary>
    public Action? OnLogIn { get; set; }

    public int LogInPresses { get; private set; }

    public FakeControl Build()
    {
        bool paper = Mode == TradingMode.Paper;
        var live = new FakeControl("toggle button", "Live Trading", paper ? [] : ["checked"]) { OnPress = () => Select(TradingMode.Live) };
        var paperTab = new FakeControl("toggle button", "Paper Trading", paper ? ["checked"] : []) { OnPress = () => Select(TradingMode.Paper) };
        var user = new FakeControl("text", "Username", "focusable", "editable", "single line")
        {
            Text = UserReadsBackWrong && User.Length > 0 ? User[..^1] + "?" : User,
            OnSetText = t => User = t,
        }.At(400);
        var password = new FakeControl("password text", "Password", "focusable", "editable", "single line")
        {
            Text = Password,
            OnSetText = t => Password = t[..Math.Max(0, t.Length - PasswordCharsDropped)],
        }.At(460);
        var logIn = FakeControl.Button(LoginFormText.LogInLabel(Mode), () => { LogInPresses++; OnLogIn?.Invoke(); });
        if (LogInStaysDisabled || User.Length == 0 || Password.Length == 0)
            logIn.Disabled();
        var form = new List<FakeControl>
        {
            FakeControl.Label("Trader Workstation"),
            live,
            paperTab,
            user,
            password,
            new FakeControl("text", "", "multiple line") { Text = "Forgot password?" },
            logIn,
            new FakeControl("combo box", "").Hidden(),
        };
        if (paper && !NoBanner)
            form.Insert(3, FakeControl.Label(" SIMULATED TRADING   SIMULATED TRADING "));
        return TwsScreens.Frame("Login", [.. form]);
    }

    void Select(TradingMode mode)
    {
        if (!TabIgnoresPress)
            Mode = mode;
    }
}
