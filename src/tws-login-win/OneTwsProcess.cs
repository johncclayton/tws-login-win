namespace tws_login_win;

/// <summary>
/// The one TWS process a login is tied to (<c>login --pid</c>): only its windows count, so another TWS's Login window
/// is never taken, and once the process has exited the login stops (<see cref="LoginAbortedException"/>) instead of
/// waiting out its timeouts.
/// </summary>
/// <param name="hasExited">Whether the process has exited; asked often, so it must be cheap.</param>
public sealed class OneTwsProcess(int processId, Func<bool> hasExited)
{
    /// <summary>The longest stretch slept without asking whether the process is still there.</summary>
    static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);

    public int ProcessId => processId;

    /// <summary>
    /// <paramref name="tools"/> seeing only this process's windows, and stopping within <see cref="CheckInterval"/>
    /// of any wait once the process has exited: every phase of the login waits through the clock.
    /// </summary>
    public TwsTools Scope(TwsTools tools) =>
        tools with { Desktop = new Desktop(this, tools.Desktop), Clock = new Clock(this, tools.Clock) };

    /// <summary>Stops the login once the process has exited.</summary>
    public void ThrowIfExited()
    {
        if (hasExited())
            throw new LoginAbortedException($"TWS pid {processId} exited");
    }

    /// <summary>Every look at the desktop asks first whether the process is still there.</summary>
    sealed class Desktop(OneTwsProcess process, IDesktop desktop) : IDesktop
    {
        public IReadOnlyList<TopLevelWindow> TwsWindows()
        {
            process.ThrowIfExited();
            return desktop.TwsWindows().Where(w => w.ProcessId == process.ProcessId).ToList();
        }
    }

    /// <summary>Sleeps in short stretches, asking between them whether the process is still there.</summary>
    sealed class Clock(OneTwsProcess process, IClock clock) : IClock
    {
        public DateTimeOffset Now => clock.Now;

        public void Sleep(TimeSpan duration)
        {
            var end = clock.Now + duration;
            for (var left = duration; left > TimeSpan.Zero; left = end - clock.Now)
            {
                clock.Sleep(left < CheckInterval ? left : CheckInterval);
                process.ThrowIfExited();
            }
        }
    }
}
