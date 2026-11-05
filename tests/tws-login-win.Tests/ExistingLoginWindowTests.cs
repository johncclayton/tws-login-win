using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// <c>login --pid n --existing</c>: the Login window of an already running TWS is judged once, not waited for. On the
/// Login form it is logged in as usual; on anything else the login stops at once, sending nothing.
/// </summary>
public class ExistingLoginWindowTests
{
    const int Mine = FakeTws.DefaultProcessId;
    static readonly TwsCredentials Live = new("liveuser1234", "an0ther-secret", TradingMode.Live);
    static readonly LoginTimeouts Timeouts = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3));

    readonly FakeTws _tws = new();
    readonly FakeLoginForm _form = new();
    readonly DateTimeOffset _start;

    public ExistingLoginWindowTests() => _start = _tws.Now;

    void Run()
    {
        var tools = new OneTwsProcess(Mine, () => _tws.HasExited(Mine)).Scope(_tws.Tools);
        new LoginFlow(new LoginFormLocator(tools), _tws).Run(Live, Timeouts, existing: true);
    }

    void AssertStopsAtOnce(string shows)
    {
        var ex = Assert.Throws<LoginAbortedException>(Run);

        Assert.Equal(
            $"the Login window is not on the Login form (it shows {shows}); finish or cancel it in TWS, then try the login again.",
            ex.Message);
        Assert.Empty(_tws.Sent);
        Assert.Equal(_start, _tws.Now);
    }

    [Fact]
    public void Login_form_showing_is_logged_in_as_usual()
    {
        var login = _tws.Add("Login", "SunAwtFrame", _form.Build);
        _form.OnLogIn = () =>
        {
            login.Content = TwsScreens.Authenticating;
            _tws.Add("Interactive Brokers", "SunAwtFrame");
        };

        Run();

        Assert.Equal(["set 'Username' 12 chars", "set 'Password' 14 chars", "press 'Log In'"], _tws.Sent);
    }

    [Fact]
    public void Second_factor_screen_stops_at_once()
    {
        _tws.Add("Second Factor Authentication", "SunAwtFrame", TwsScreens.DeviceChoice);

        AssertStopsAtOnce("a second-factor screen");
    }

    [Fact]
    public void Other_screen_stops_at_once()
    {
        _tws.Add("Login", "SunAwtFrame", TwsScreens.Authenticating);

        AssertStopsAtOnce("another screen, not the Login form");
    }

    [Fact]
    public void Unreadable_window_stops_at_once()
    {
        _tws.BridgeOff = true;
        _tws.Add("Login", "SunAwtFrame", _form.Build);

        AssertStopsAtOnce("nothing the Java Access Bridge can read");
    }

    [Fact]
    public void No_Login_window_of_the_process_stops_at_once()
    {
        // Another TWS's Login form is not this process's.
        _tws.Add("Login", "SunAwtFrame", _form.Build, processId: 7002);

        var ex = Assert.Throws<LoginAbortedException>(Run);

        Assert.Equal("no visible TWS Login window.", ex.Message);
        Assert.Empty(_tws.Sent);
        Assert.Equal(_start, _tws.Now);
    }
}
