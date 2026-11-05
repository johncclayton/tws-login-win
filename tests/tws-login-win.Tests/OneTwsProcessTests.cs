using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// A login tied to one TWS process (<c>--pid</c>): only that process's windows count, and it stops soon after the
/// process exits, whatever it is doing then.
/// </summary>
public class OneTwsProcessTests
{
    const int Mine = FakeTws.DefaultProcessId, Other = 7002;
    static readonly TwsCredentials Live = new("liveuser1234", "an0ther-secret", TradingMode.Live);
    static readonly LoginTimeouts Timeouts = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3));

    readonly FakeTws _tws = new();
    readonly FakeLoginForm _form = new();
    readonly FakeLoginForm _otherForm = new();

    void Run() => Run(Mine);

    void Run(int processId)
    {
        var tools = new OneTwsProcess(processId, () => _tws.HasExited(processId)).Scope(_tws.Tools);
        new LoginFlow(new LoginFormLocator(tools), _tws).Run(Live, Timeouts);
    }

    FakeTws.Window AddLogin(FakeLoginForm form, int processId)
    {
        var login = _tws.Add("Login", "SunAwtFrame", form.Build, processId);
        form.OnLogIn = () =>
        {
            login.Content = TwsScreens.Authenticating;
            _tws.Add("Interactive Brokers", "SunAwtFrame", processId: processId);
        };
        return login;
    }

    [Fact]
    public void Another_TWS_Login_window_is_left_alone()
    {
        AddLogin(_otherForm, Other);
        AddLogin(_form, Mine);

        Run();

        Assert.Equal(("liveuser1234", "an0ther-secret"), (_form.User, _form.Password));
        Assert.Equal(("", ""), (_otherForm.User, _otherForm.Password));
    }

    /// <summary>The process exits at the first sleep at or after <paramref name="seconds"/> from now; returns that moment.</summary>
    Func<DateTimeOffset> ExitAt(double seconds)
    {
        var when = _tws.Now + TimeSpan.FromSeconds(seconds);
        DateTimeOffset? exited = null;
        var previous = _tws.OnSleep;
        _tws.OnSleep = () =>
        {
            previous?.Invoke();
            if (exited is null && _tws.Now >= when)
            {
                exited = _tws.Now;
                _tws.Exit(Mine);
            }
        };
        return () => exited ?? throw new InvalidOperationException("the process never exited");
    }

    void AssertAbortsSoonAfterExit(Func<DateTimeOffset> exited)
    {
        var ex = Assert.Throws<LoginAbortedException>(Run);

        Assert.Equal($"TWS pid {Mine} exited", ex.Message);
        Assert.InRange(_tws.Now - exited(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Exit_while_waiting_for_the_Login_window_aborts_within_a_second()
    {
        var exited = ExitAt(30.3);

        AssertAbortsSoonAfterExit(exited);
        Assert.Empty(_tws.Sent);
    }

    [Fact]
    public void Exit_while_waiting_after_Log_In_aborts_within_a_second()
    {
        var login = AddLogin(_form, Mine);
        // Log In is taken, then TWS waits on the second factor until the process goes.
        _form.OnLogIn = () =>
        {
            login.Title = "Second Factor Authentication";
            login.Content = TwsScreens.DeviceChoice;
        };
        Func<DateTimeOffset>? exited = null;
        _form.OnLogIn += () => exited = ExitAt(40.2);

        var ex = Assert.Throws<LoginAbortedException>(Run);

        Assert.Equal($"TWS pid {Mine} exited", ex.Message);
        Assert.InRange(_tws.Now - exited!(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Exit_during_a_long_wait_ends_it_within_a_second()
    {
        // As the code screen waits up to 6 s for the next one-time code, without looking at any window.
        var tools = new OneTwsProcess(Mine, () => _tws.HasExited(Mine)).Scope(_tws.Tools);
        var exited = ExitAt(0.2);

        var ex = Assert.Throws<LoginAbortedException>(() => tools.Clock.Sleep(TimeSpan.FromSeconds(6)));

        Assert.Equal($"TWS pid {Mine} exited", ex.Message);
        Assert.InRange(_tws.Now - exited(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_process_already_gone_aborts_before_anything_is_read()
    {
        AddLogin(_form, Mine);
        _tws.Exit(Mine);

        var ex = Assert.Throws<LoginAbortedException>(Run);

        Assert.Equal($"TWS pid {Mine} exited", ex.Message);
        Assert.Empty(_tws.Sent);
    }
}
