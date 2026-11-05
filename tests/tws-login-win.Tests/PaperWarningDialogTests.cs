using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>Picking the paper warning out of TWS's many "Warning" dialogs and accepting it, once the main window is up.</summary>
public class PaperWarningDialogTests
{
    readonly FakeTws _tws = new();
    readonly FakeTws.Window _login;

    public PaperWarningDialogTests()
    {
        _login = _tws.Add("Login", "SunAwtFrame", TwsScreens.Authenticating);
        _tws.Add("Interactive Brokers", "SunAwtFrame");
    }

    void Accept() =>
        new AfterLogInWatcher(_tws.Tools.Windows().Single(w => w.Handle == _login.Handle), _tws.Tools, TradingMode.Paper, code: null)
            .WaitUntilLoggedIn(TimeSpan.FromSeconds(30));

    static FakeControl MarketDataWarning() =>
        TwsScreens.Dialog("Warning", FakeControl.Message("Market data farm is down"), FakeControl.Button("OK"));

    [Fact]
    public void Accepts_the_dialog_with_the_warning_text_not_another_Warning()
    {
        var other = _tws.AddDialog("Warning", MarketDataWarning());
        _tws.AddDialog("Warning", TwsScreens.PaperWarning());

        Accept();

        Assert.Equal(["press 'I understand and accept'"], _tws.Sent);
        Assert.True(other.Exists);
    }

    [Fact]
    public void Accept_button_without_the_warning_text_is_not_pressed_and_the_wait_times_out()
    {
        _tws.AddDialog("Warning", TwsScreens.Dialog("Warning", FakeControl.Button("I understand and accept")));

        var ex = Assert.Throws<LoginAbortedException>(Accept);

        Assert.Contains("no paper warning to accept", ex.Message);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Reports_a_dialog_that_stays_open_after_the_press()
    {
        var paper = _tws.AddDialog("Warning", TwsScreens.PaperWarning());
        paper.ClosesOnPress = false;

        var ex = Assert.Throws<LoginAbortedException>(Accept);

        Assert.Contains("still open", ex.Message);
    }

    [Fact]
    public void Disabled_accept_button_is_not_pressed()
    {
        var warning = TwsScreens.PaperWarning();
        warning.Children[0].Children[0].Children[1].Disabled();
        _tws.AddDialog("Warning", warning);

        var ex = Assert.Throws<LoginAbortedException>(Accept);

        Assert.Contains("not enabled", ex.Message);
        Assert.Empty(_tws.Sent);
    }
}
