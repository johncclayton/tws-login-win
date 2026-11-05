using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>What happens after Log In, as the watcher of one Login window sees it and decides.</summary>
public class AfterLogInWatcherTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    readonly FakeTws _tws = new();
    readonly FakeLoginForm _form = new() { User = "liveuser1234", Password = "pw" };
    readonly FakeTws.Window _login;

    public AfterLogInWatcherTests() => _login = _tws.Add("Login", "SunAwtFrame", _form.Build);

    /// <summary>A live login: logged in once the main window shows.</summary>
    void Wait() => Wait(TradingMode.Live);

    /// <summary>A paper login: also needs its paper warning accepted.</summary>
    void WaitPaper() => Wait(TradingMode.Paper);

    void Wait(TradingMode mode)
    {
        new AfterLogInWatcher(_tws.Tools.Windows().Single(w => w.Handle == _login.Handle), _tws.Tools, mode, code: null).WaitUntilLoggedIn(Limit);
    }

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

    /// <summary>TWS takes the Log In: the tabs go and "Authenticating…" shows.</summary>
    void Authenticating(double seconds) => At(seconds, () =>
    {
        _login.Title = "Login";
        _login.Content = TwsScreens.Authenticating;
    });

    /// <summary>A different "Authenticating…" screen, so the Login window counts as changing.</summary>
    void StillWorking(double seconds, string text) => At(seconds, () => _login.Content = () => TwsScreens.Frame("Login", FakeControl.Label(text)));

    void SecondFactor(double seconds, Func<FakeControl> screen) => At(seconds, () =>
    {
        _login.Title = "Second Factor Authentication";
        _login.Content = screen;
    });

    void MainWindow(double seconds, int processId = FakeTws.DefaultProcessId) =>
        At(seconds, () => _tws.Add("Interactive Brokers", "SunAwtFrame", processId: processId));

    [Fact]
    public void Main_window_of_this_TWS_means_logged_in()
    {
        Authenticating(1);
        MainWindow(20);

        Wait();

        Assert.Empty(_tws.Sent);
        Assert.Contains(_tws.Logged, l => l.Contains("logged in"));
    }

    [Fact]
    public void Code_screen_without_a_code_source_waits_for_the_user_without_input_or_a_stall_limit()
    {
        Authenticating(1);
        SecondFactor(3, () => TwsScreens.CodeScreen(TwsScreens.CodeBox()));
        MainWindow(100);

        Wait();

        Assert.Empty(_tws.Sent);
        Assert.Single(_tws.Logged, l => l.Contains("code screen") && l.Contains("waiting for you"));
    }

    [Fact]
    public void Device_choice_waits_for_the_user_naming_the_selected_device()
    {
        Authenticating(1);
        SecondFactor(3, TwsScreens.DeviceChoice);
        MainWindow(100);

        Wait();

        Assert.Empty(_tws.Sent);
        Assert.Single(_tws.Logged, l => l.Contains("'Mobile Authenticator app' selected") && l.Contains("waiting for you"));
    }

    [Fact]
    public void Other_frames_of_this_TWS_are_logged_once_by_title_and_left_alone()
    {
        Authenticating(1);
        At(10, () => _tws.Add("Trades", "SunAwtFrame", () => throw new InvalidOperationException("a frame with account data was read")));
        MainWindow(20);

        Wait();

        Assert.Single(_tws.Logged, l => l.Contains("'Trades'"));
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Paper_warning_is_accepted_on_the_way()
    {
        Authenticating(1);
        At(10, () => _tws.AddDialog("Warning", TwsScreens.PaperWarning()));
        MainWindow(15);

        WaitPaper();

        Assert.Equal(["press 'I understand and accept'"], _tws.Sent);
        Assert.Contains(_tws.Logged, l => l.Contains("logged in"));
    }

    [Fact]
    public void Existing_session_dialog_is_answered_with_Continue_Login_once()
    {
        Authenticating(1);
        At(5, () => _tws.AddDialog("Existing session detected", TwsScreens.ExistingSession()));
        MainWindow(15);

        Wait();

        Assert.Equal(["press 'Continue Login'"], _tws.Sent);
        Assert.Single(_tws.Logged, l => l.Contains("existing-session dialog"));
    }

    [Fact]
    public void Lost_connection_is_answered_with_Re_login_once()
    {
        // As seen on 2026-10-28: after Continue Login, the phone app took the session back.
        Authenticating(1);
        At(5, () => _tws.AddDialog("Existing session detected", TwsScreens.ExistingSession()));
        At(8, () => _tws.AddDialog("Re-login is required", TwsScreens.Relogin()));
        StillWorking(12, "Connecting...");
        MainWindow(25);

        Wait();

        Assert.Equal(["press 'Continue Login'", "press 'Re-login'"], _tws.Sent);
        Assert.Contains(_tws.Logged, l => l.Contains("connection was lost") && l.Contains("1 of 1"));
    }

    [Fact]
    public void Lost_connection_a_second_time_aborts_without_pressing_Re_login_again()
    {
        Authenticating(1);
        At(5, () => _tws.AddDialog("Re-login is required", TwsScreens.Relogin()));
        StillWorking(8, "Connecting...");
        At(12, () => _tws.AddDialog("Re-login is required", TwsScreens.Relogin()));

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("not pressing it again", ex.Message);
        Assert.Equal(["press 'Re-login'"], _tws.Sent);
    }

    [Fact]
    public void Login_error_aborts_with_TWS_s_own_message()
    {
        Authenticating(1);
        At(8, () => _tws.AddDialog("Login Error", TwsScreens.LoginError()));

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("TWS refused the login", ex.Message);
        Assert.Contains("Login failed - incorrect security code", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Login_failed_without_its_OK_button_is_not_taken_for_a_login_error()
    {
        Authenticating(1);
        At(8, () => _tws.AddDialog("Login Error", TwsScreens.Dialog("Login Error",
            FakeControl.Message("Login failed - incorrect security code"), FakeControl.Button("Retry"))));

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("unrecognised dialog", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Login_form_back_after_Re_login_aborts_without_refilling()
    {
        // What follows Re-login has not been seen on TWS yet; if it is the Login form, it is not filled again.
        Authenticating(1);
        At(5, () => _tws.AddDialog("Re-login is required", TwsScreens.Relogin()));
        At(8, () => _login.Content = _form.Build);

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("Login form is back", ex.Message);
        Assert.Equal(["press 'Re-login'"], _tws.Sent);
    }

    [Fact]
    public void Login_form_unchanged_10s_after_Log_In_aborts()
    {
        var start = _tws.Now;

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("Log In was not taken", ex.Message);
        Assert.InRange(_tws.Now - start, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11));
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Login_form_back_after_Log_In_aborts_without_refilling()
    {
        var start = _tws.Now;
        Authenticating(1);
        At(5, () => _login.Content = _form.Build);

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("Login form is back", ex.Message);
        Assert.InRange(_tws.Now - start, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Login_window_unchanged_for_60s_aborts()
    {
        var start = _tws.Now;
        Authenticating(1);
        StillWorking(30, "Loading...");

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("unchanged for 60s", ex.Message);
        Assert.InRange(_tws.Now - start, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(91));
    }

    [Fact]
    public void Unrecognised_dialog_stops_the_login_untouched_naming_its_text_with_digits_masked()
    {
        Authenticating(1);
        At(10, () => _tws.AddDialog("Warning", TwsScreens.Dialog("Warning",
            FakeControl.Message("Market data farm 12 is down"), FakeControl.Button("OK"))));

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("unrecognised dialog", ex.Message);
        Assert.Contains("Market data farm ## is down", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Dialog_still_without_text_is_given_time_to_show_the_paper_warning()
    {
        Authenticating(1);
        FakeTws.Window? warning = null;
        At(10, () => warning = _tws.AddDialog("Warning", TwsScreens.Dialog("Warning")));
        At(11.5, () => warning!.Content = TwsScreens.PaperWarning);
        MainWindow(15);

        WaitPaper();

        Assert.Equal(["press 'I understand and accept'"], _tws.Sent);
    }

    [Fact]
    public void Paper_login_is_not_done_until_its_warning_is_accepted_even_after_the_main_window()
    {
        // As seen on TWS 1051 on 2026-10-28: the main window ~12 s after Log In, the paper warning after it.
        Authenticating(1);
        MainWindow(12);
        At(14, () => _tws.AddDialog("Warning", TwsScreens.PaperWarning()));

        WaitPaper();

        Assert.Equal(["press 'I understand and accept'"], _tws.Sent);
        Assert.Contains(_tws.Logged, l => l.Contains("logged in"));
    }

    [Fact]
    public void Other_dialog_after_the_main_window_is_logged_and_ignored_while_the_paper_warning_is_awaited()
    {
        Authenticating(1);
        MainWindow(12);
        At(13, () => _tws.AddDialog("Tip of the day", TwsScreens.Dialog("Tip", FakeControl.Message("Did you know"), FakeControl.Button("Close"))));
        At(20, () => _tws.AddDialog("Warning", TwsScreens.PaperWarning()));

        WaitPaper();

        Assert.Equal(["press 'I understand and accept'"], _tws.Sent);
        Assert.Single(_tws.Logged, l => l.Contains("'Tip of the day'") && l.Contains("ignored"));
    }

    [Fact]
    public void Paper_login_whose_warning_never_comes_stops_at_the_limit_naming_it()
    {
        Authenticating(1);
        MainWindow(12);

        var ex = Assert.Throws<LoginAbortedException>(WaitPaper);

        Assert.Contains("paper warning", ex.Message);
    }

    [Fact]
    public void Main_window_of_the_other_TWS_is_not_this_login_and_the_limit_stops_the_wait()
    {
        var start = _tws.Now;
        Authenticating(1);
        MainWindow(5, processId: 8002);
        StillWorking(10, "a");   // keeps changing, so it is not stalled
        StillWorking(70, "b");
        StillWorking(130, "c");

        var ex = Assert.Throws<LoginAbortedException>(Wait);

        Assert.Contains("not logged in within 180s", ex.Message);
        Assert.InRange(_tws.Now - start, Limit, Limit + TimeSpan.FromSeconds(1));
    }
}
