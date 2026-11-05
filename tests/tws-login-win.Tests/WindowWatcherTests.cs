using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>The watch command: what it logs of TWS's windows, and what it never shows.</summary>
public class WindowWatcherTests
{
    readonly FakeTws _tws = new();

    void Watch(double seconds) => new WindowWatcher(_tws.Tools).Watch(TimeSpan.FromSeconds(seconds));

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

    [Fact]
    public void Windows_open_before_watching_are_neither_logged_nor_read()
    {
        _tws.Add("U1234567 Interactive Brokers", "SunAwtFrame", () => throw new InvalidOperationException("read"));

        Watch(5);

        Assert.DoesNotContain(_tws.Logged, l => l.Contains("Interactive Brokers"));
    }

    [Fact]
    public void A_dialog_s_controls_are_logged_when_it_appears_and_again_only_when_they_change()
    {
        FakeTws.Window? dialog = null;
        At(1, () => dialog = _tws.AddDialog("Existing session detected", TwsScreens.ExistingSession()));
        At(3, () => dialog!.Content = TwsScreens.Relogin);

        Watch(6);

        Assert.Equal(2, _tws.Logged.Count(l => l.Contains("controls of")));
        Assert.Contains(_tws.Logged, l => l.Contains("[push button] 'Continue Login'"));
        Assert.Contains(_tws.Logged, l => l.Contains("connection was lost"));
    }

    [Fact]
    public void Login_window_fields_are_shown_by_length_only_under_any_title()
    {
        var form = new FakeLoginForm { User = "liveuser1234", Password = "s3cret" };
        FakeTws.Window? login = null;
        At(1, () => login = _tws.Add("Login", "SunAwtFrame", form.Build));
        var box = TwsScreens.CodeBox();
        box.Text = "123456";
        At(3, () =>
        {
            login!.Title = "Second Factor Authentication";
            login.Content = () => TwsScreens.CodeScreen(box);
        });

        Watch(6);

        Assert.Contains(_tws.Logged, l => l.Contains("[text] '<8 chars>'") && l.Contains("len=12"));
        Assert.Contains(_tws.Logged, l => l.Contains("[password text]") && l.Contains("len=6"));
        Assert.Contains(_tws.Logged, l => l.Contains("'Enter Mobile Authenticator app code'"));
        Assert.DoesNotContain(_tws.Logged, l => l.Contains("liveuser1234") || l.Contains("s3cret") || l.Contains("123456"));
    }

    [Fact]
    public void Read_only_single_line_text_is_shown_by_length_only()
    {
        // E.g. the user name field, should TWS make it read-only while it logs in.
        At(1, () => _tws.Add("Login", "SunAwtFrame", () => TwsScreens.Frame("Login",
            new FakeControl("text", "Username", "single line") { Text = "liveuser1234" })));

        Watch(3);

        Assert.Contains(_tws.Logged, l => l.Contains("[text] '<8 chars>'") && l.Contains("len=12"));
        Assert.DoesNotContain(_tws.Logged, l => l.Contains("liveuser1234"));
    }

    [Fact]
    public void Frames_other_than_Login_are_logged_by_title_redacted_but_never_read()
    {
        At(1, () => _tws.Add("U1234567 Trades", "SunAwtFrame", () => throw new InvalidOperationException("read")));

        Watch(3);

        Assert.Contains(_tws.Logged, l => l.Contains("'[account] Trades'"));
        Assert.DoesNotContain(_tws.Logged, l => l.Contains("U1234567"));
    }

    [Fact]
    public void Digits_in_dialog_text_are_masked()
    {
        At(1, () => _tws.AddDialog("Warning", TwsScreens.Dialog("Warning", FakeControl.Message("Connected from 203.0.113.7"))));

        Watch(3);

        Assert.Contains(_tws.Logged, l => l.Contains("Connected from ###.#.###.#"));
        Assert.DoesNotContain(_tws.Logged, l => l.Contains("203.0.113.7"));
    }

    [Fact]
    public void Bridge_off_is_reported_once_with_the_fix()
    {
        _tws.BridgeOff = true;
        At(1, () => _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build));

        Watch(5);

        Assert.Single(_tws.Logged, l => l.Contains("jabswitch -enable"));
    }
}
