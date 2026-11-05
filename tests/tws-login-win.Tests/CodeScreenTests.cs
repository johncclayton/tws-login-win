using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// The code screen of a live login, as the watcher of one Login window sees it: it sets the Mobile Authenticator code
/// from the code source in the code box, reads it back, and presses OK. Once per login attempt.
/// </summary>
public class CodeScreenTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);
    const string SetCode = "set '' 6 chars";
    const string Ok = "press 'OK'";

    readonly FakeTws _tws = new();
    readonly FakeLoginForm _form = new() { User = "liveuser1234", Password = "pw" };
    readonly FakeTws.Window _login;
    readonly FakeCode _code = new("123456");

    /// <summary>What the code box holds, as TWS would show it.</summary>
    string _boxText = "";

    /// <summary>The code box shows something other than what was set: a dropped digit.</summary>
    bool _boxDropsADigit;

    /// <summary>The code OK was pressed with, or null.</summary>
    string? _submitted;

    public CodeScreenTests()
    {
        _login = _tws.Add("Login", "SunAwtFrame", _form.Build);
        _code.Clock = _tws;
    }

    sealed class FakeCode(string code) : IOneTimeCodeSource
    {
        public List<DateTimeOffset> Fetched { get; } = [];

        public Func<DateTimeOffset, string>? ByTime { get; set; }

        public IClock? Clock { get; set; }

        /// <summary>Runs during each fetch, before the code is chosen: e.g. sleeps the clock as a slow 1Password does.</summary>
        public Action? WhileFetching { get; set; }

        public string Current()
        {
            Fetched.Add(Clock!.Now);
            WhileFetching?.Invoke();
            return ByTime?.Invoke(Clock.Now) ?? code;
        }
    }

    void Wait(IOneTimeCodeSource? code)
    {
        new AfterLogInWatcher(_tws.Tools.Windows().Single(w => w.Handle == _login.Handle), _tws.Tools, TradingMode.Live, code)
            .WaitUntilLoggedIn(Limit);
    }

    void Wait() => Wait(_code);

    /// <summary>Runs <paramref name="action"/> once, at the first sleep at or after <paramref name="seconds"/> after Log In.</summary>
    void At(double seconds, Action action)
    {
        var when = _tws.Now + TimeSpan.FromSeconds(seconds);
        var previous = _tws.OnSleep;
        bool done = false;
        _tws.OnSleep = () =>
        {
            previous?.Invoke();
            if (done || _tws.Now < when)
                return;
            done = true;
            action();
        };
    }

    /// <summary>
    /// TWS works on the login. After the code is taken the window is titled "Login" again while TWS loads, as seen
    /// on live TWS 1051; no second-factor screen remains.
    /// </summary>
    void Authenticating(double seconds) => At(seconds, () =>
    {
        _login.Title = "Login";
        _login.Content = TwsScreens.Authenticating;
    });

    void CodeScreen(double seconds) => At(seconds, () =>
    {
        _boxText = "";
        _login.Title = "Second Factor Authentication";
        _login.Content = () =>
        {
            var box = TwsScreens.CodeBox();
            box.Text = _boxDropsADigit && _boxText.Length > 0 ? _boxText[..^1] : _boxText;
            box.OnSetText = t => _boxText = t;
            return TwsScreens.CodeScreen(box, () => _submitted = _boxText);
        };
    });

    /// <summary>A second-factor screen that is not the code screen.</summary>
    void OtherSecondFactorScreen(double seconds) => At(seconds, () =>
    {
        _login.Title = "Second Factor Authentication";
        _login.Content = () => TwsScreens.Frame("Second Factor Authentication", FakeControl.Message("Please wait"));
    });

    void MainWindow(double seconds) => At(seconds, () => _tws.Add("Interactive Brokers", "SunAwtFrame"));

    [Fact]
    public void Code_is_set_in_the_box_read_back_then_OK_is_pressed()
    {
        Authenticating(1);
        CodeScreen(3);
        Authenticating(8);
        MainWindow(20);

        Wait();

        Assert.Equal([SetCode, Ok], _tws.Sent);
        Assert.Equal("123456", _submitted);
        Assert.Single(_code.Fetched);
        Assert.Contains(_tws.Logged, l => l.Contains("logged in"));
        Assert.DoesNotContain(_tws.Logged, l => l.Contains("123456"));
    }

    [Fact]
    public void Box_not_showing_the_code_set_never_gets_OK()
    {
        _boxDropsADigit = true;
        Authenticating(1);
        CodeScreen(3);

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("does not show the code set", ex.Message);
        Assert.Equal([SetCode], _tws.Sent);
        Assert.Null(_submitted);
    }

    [Fact]
    public void Code_about_to_expire_is_not_used_the_next_one_is_awaited()
    {
        // Codes change every 30 s from 09:00:00; the screen shows at 09:00:26, 4 s before the change.
        var nextPeriod = _tws.Now + TimeSpan.FromSeconds(30);
        _code.ByTime = now => now < nextPeriod ? "111111" : "222222";
        Authenticating(1);
        CodeScreen(26);
        Authenticating(40);
        MainWindow(50);

        Wait();

        Assert.Equal("222222", _submitted);
        Assert.InRange(Assert.Single(_code.Fetched), nextPeriod, nextPeriod + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Slow_fetch_leaving_too_little_of_the_code_fetches_the_next_one()
    {
        // The screen shows at 09:00:23, with 7 s of the code left; 1Password takes 3 s, leaving under 5 s.
        var nextPeriod = _tws.Now + TimeSpan.FromSeconds(30);
        _code.ByTime = now => now < nextPeriod ? "111111" : "222222";
        _code.WhileFetching = () => _tws.Sleep(TimeSpan.FromSeconds(3));
        Authenticating(1);
        CodeScreen(23.5);
        Authenticating(40);
        MainWindow(50);

        Wait();

        Assert.Equal("222222", _submitted);
        Assert.Equal(2, _code.Fetched.Count);
        Assert.InRange(_code.Fetched[1], nextPeriod, nextPeriod + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Wrong_code_shows_TWS_s_login_error_and_stops_without_a_second_attempt()
    {
        // As seen on 2026-10-28: a "Login Error" dialog, and the Login window goes back to the Login form.
        Authenticating(1);
        CodeScreen(3);
        At(6, () =>
        {
            _login.Title = "Login";
            _login.Content = _form.Build;
            _tws.AddDialog("Login Error", TwsScreens.LoginError());
        });

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("incorrect security code", ex.Message);
        Assert.Equal([SetCode, Ok], _tws.Sent);
    }

    /// <summary>
    /// As seen on live TWS 1051 on 2026-11-02: TWS logged "Authentication completed" 0.2 s after OK but kept the window
    /// titled "Second Factor Authentication" for over 10 s while it loaded. The title alone is not a refused code.
    /// </summary>
    [Fact]
    public void Second_factor_title_kept_while_TWS_loads_is_not_a_refused_code()
    {
        Authenticating(1);
        CodeScreen(3);
        OtherSecondFactorScreen(6);
        MainWindow(25);

        Wait();

        Assert.Single(_tws.Sent, s => s == Ok);
        Assert.Contains(_tws.Logged, l => l.Contains("logged in"));
    }

    [Fact]
    public void Code_screen_still_holding_the_code_while_TWS_loads_is_waited_on()
    {
        Authenticating(1);
        CodeScreen(3);
        MainWindow(25);

        Wait();

        Assert.Equal([SetCode, Ok], _tws.Sent);
        Assert.Contains(_tws.Logged, l => l.Contains("logged in"));
    }

    [Fact]
    public void Second_factor_screen_that_never_goes_ends_at_the_login_limit_without_a_second_attempt()
    {
        Authenticating(1);
        CodeScreen(3);
        OtherSecondFactorScreen(6);

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("not logged in within 180s", ex.Message);
        Assert.Single(_tws.Sent, s => s == Ok);
    }

    [Fact]
    public void Code_screen_back_more_than_10s_after_OK_aborts_without_a_second_attempt()
    {
        var start = _tws.Now;
        Authenticating(1);
        CodeScreen(3);
        Authenticating(6);
        CodeScreen(20);

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("code not taken", ex.Message);
        Assert.Single(_tws.Sent, s => s == Ok);
        Assert.Single(_code.Fetched);
        Assert.InRange(_tws.Now - start, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(21));
    }

    [Fact]
    public void After_Re_login_the_code_is_entered_again_for_the_new_attempt()
    {
        Authenticating(1);
        CodeScreen(3);
        Authenticating(6);
        At(8, () => _tws.AddDialog("Re-login is required", TwsScreens.Relogin()));
        CodeScreen(12);
        Authenticating(16);
        MainWindow(25);

        Wait();

        Assert.Equal([SetCode, Ok, "press 'Re-login'", SetCode, Ok], _tws.Sent);
        Assert.Equal(2, _code.Fetched.Count);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345a")]
    [InlineData("")]
    public void Code_that_is_not_6_digits_is_never_set(string fetched)
    {
        Authenticating(1);
        CodeScreen(3);
        var code = new FakeCode(fetched) { Clock = _tws };

        var ex = Assert.Throws<LoginAbortedException>(() => Wait(code));

        Assert.Contains("not 6 digits", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Enter_Read_Only_and_Cancel_are_never_pressed()
    {
        Authenticating(1);
        CodeScreen(3);
        Authenticating(8);
        MainWindow(20);

        Wait();

        Assert.DoesNotContain(_tws.Sent, s => s.Contains("Read Only") || s.Contains("Cancel"));
    }
}
