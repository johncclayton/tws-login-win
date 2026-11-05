using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// <c>login-windows</c>: one line per visible Login window, <c>&lt;pid&gt; &lt;screen&gt;</c>, whichever TWS it
/// belongs to. Nothing is sent.
/// </summary>
public class LoginWindowsTests
{
    readonly FakeTws _tws = new();

    IReadOnlyList<string> Lines() => LoginWindows.Lines(_tws.Tools);

    [Fact]
    public void No_TWS_lists_nothing()
    {
        Assert.Empty(Lines());
    }

    [Fact]
    public void Each_visible_Login_window_is_listed_with_its_process_and_screen()
    {
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: 7001);
        _tws.Add("Second Factor Authentication", "SunAwtFrame", TwsScreens.DeviceChoice, processId: 7002);
        _tws.Add("Login", "SunAwtFrame", TwsScreens.Authenticating, processId: 7003);
        _tws.Add("Login", "SunAwtFrame", content: null, processId: 7004);

        Assert.Equal(["7004 unknown", "7003 other", "7002 second-factor", "7001 form"], Lines());
        Assert.Empty(_tws.Sent);
        Assert.Equal(0, _tws.OpenTrees);
    }

    [Fact]
    public void Hidden_Login_windows_dialogs_and_other_frames_are_not_listed()
    {
        // TWS keeps an invisible second Login window; its main window and dialogs are not Login windows.
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build).Visible = false;
        _tws.Add("Interactive Brokers", "SunAwtFrame");
        _tws.AddDialog("Login", TwsScreens.LoginError());
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: 7002);

        Assert.Equal(["7002 form"], Lines());
    }

    [Fact]
    public void Bridge_off_still_lists_the_windows_by_process()
    {
        _tws.BridgeOff = true;
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build);
        _tws.Add("Second Factor Authentication", "SunAwtFrame", TwsScreens.DeviceChoice, processId: 7002);

        Assert.Equal(["7002 second-factor", "7001 unknown"], Lines());
    }
}
