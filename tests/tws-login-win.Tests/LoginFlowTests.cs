using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// The login up to Log In, through the Java Access Bridge: which tab it selects, what it sets, what it checks, and when
/// it is allowed to press Log In.
/// </summary>
public class LoginFlowTests
{
    static readonly TwsCredentials Paper = new("paperusr", "s3cret-pw", TradingMode.Paper);
    static readonly TwsCredentials Live = new("liveuser1234", "an0ther-secret", TradingMode.Live);
    static readonly LoginTimeouts Timeouts = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));

    readonly FakeTws _tws = new();
    readonly FakeLoginForm _form = new();
    readonly FakeTws.Window _login;

    public LoginFlowTests()
    {
        _login = _tws.Add("Login", "SunAwtFrame", _form.Build);
        // After Log In, TWS shows its main window and, for paper, the paper warning.
        _form.OnLogIn = () =>
        {
            _login.Content = TwsScreens.Authenticating;
            _tws.Add("Interactive Brokers", "SunAwtFrame");
            if (_form.Mode == TradingMode.Paper)
                _tws.AddDialog("Warning", TwsScreens.PaperWarning());
        };
    }

    void Run(TwsCredentials credentials)
    {
        new LoginFlow(new LoginFormLocator(_tws.Tools), _tws).Run(credentials, Timeouts);
    }

    [Fact]
    public void Paper_login_selects_the_tab_sets_both_fields_and_presses_Log_In()
    {
        Run(Paper);

        Assert.Equal(
            ["press 'Paper Trading'", "set 'Username' 8 chars", "set 'Password' 9 chars", "press 'Paper Log In'", "press 'I understand and accept'"],
            _tws.Sent);
        Assert.Equal(("paperusr", "s3cret-pw"), (_form.User, _form.Password));
        Assert.Contains(_tws.Logged, l => l.Contains("paper login complete"));
    }

    [Fact]
    public void Live_tab_already_selected_is_not_pressed_again()
    {
        Run(Live);

        Assert.Equal(["set 'Username' 12 chars", "set 'Password' 14 chars", "press 'Log In'"], _tws.Sent);
    }

    [Fact]
    public void Leftover_text_is_replaced_not_appended_to()
    {
        _form.User = "someone-else";
        _form.Password = "old";

        Run(Live);

        Assert.Equal(("liveuser1234", "an0ther-secret"), (_form.User, _form.Password));
    }

    [Fact]
    public void Tab_that_does_not_select_stops_before_any_field_is_set()
    {
        _form.TabIgnoresPress = true;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Paper));

        Assert.Contains("'Paper Trading' selected", ex.Message);
        Assert.Equal(["press 'Paper Trading'"], _tws.Sent);
    }

    [Fact]
    public void Missing_paper_banner_stops_before_any_field_is_set()
    {
        _form.NoBanner = true;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Paper));

        Assert.Contains("SIMULATED TRADING", ex.Message);
        Assert.Equal(["press 'Paper Trading'"], _tws.Sent);
    }

    [Fact]
    public void Password_field_holding_too_few_characters_never_reaches_Log_In()
    {
        _form.PasswordCharsDropped = 1;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Live));

        Assert.Contains("password field holds the 14 characters", ex.Message);
        Assert.Equal(0, _form.LogInPresses);
    }

    [Fact]
    public void User_name_read_back_different_never_reaches_Log_In()
    {
        _form.UserReadsBackWrong = true;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Live));

        Assert.Contains("user name field holds", ex.Message);
        Assert.Equal(0, _form.LogInPresses);
    }

    [Fact]
    public void Log_In_that_stays_disabled_is_not_pressed()
    {
        _form.LogInStaysDisabled = true;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Live));

        Assert.Contains("'Log In' is not enabled", ex.Message);
        Assert.Equal(0, _form.LogInPresses);
    }

    [Fact]
    public void Log_In_waits_until_the_form_has_stopped_changing_for_a_second()
    {
        // Someone types into the password field after it was set: Log In stays enabled, but the length keeps changing.
        int typed = 0;
        _tws.OnSleep = () =>
        {
            if (_form.Password.Length >= 14 && typed < 3)
            {
                _form.Password += "x";
                typed++;
            }
        };
        var start = _tws.Now;
        DateTimeOffset? pressedAt = null;
        var onLogIn = _form.OnLogIn;
        _form.OnLogIn = () =>
        {
            pressedAt = _tws.Now;
            onLogIn!();
        };

        Run(Live);

        Assert.Equal(3, typed);
        Assert.NotNull(pressedAt);
        Assert.InRange(pressedAt!.Value - start, TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Bridge_off_stops_naming_jabswitch_without_sending_anything()
    {
        _tws.BridgeOff = true;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Live));

        Assert.Contains("jabswitch -enable", ex.Message);
        Assert.Contains("restart TWS", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Two_Login_windows_stop_before_anything_is_sent()
    {
        // Live and paper TWS both at their Login form: which one to log in would be a guess.
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: 7002);

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Live));

        Assert.Contains("2 TWS Login windows", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Abort_messages_and_logs_never_contain_credentials()
    {
        _form.PasswordCharsDropped = 1;

        var ex = Assert.Throws<LoginAbortedException>(() => Run(Live));

        foreach (var text in _tws.Logged.Append(ex.Message))
        {
            Assert.DoesNotContain(Live.User, text);
            Assert.DoesNotContain(Live.Password, text);
        }
    }

    [Fact]
    public void Every_control_tree_read_is_released()
    {
        Run(Paper);

        Assert.Equal(0, _tws.OpenTrees);
    }
}
