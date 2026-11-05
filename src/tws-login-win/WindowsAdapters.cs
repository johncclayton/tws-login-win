using System.Diagnostics;

namespace tws_login_win;

/// <summary>Top-level windows of the TWS processes (tws.exe / tws1.exe). TWS's Swing controls have no HWNDs of their own.</summary>
sealed class Win32Desktop : IDesktop
{
    public IReadOnlyList<TopLevelWindow> TwsWindows()
    {
        var processIds = Process.GetProcesses()
            .Where(p => p.ProcessName.StartsWith("tws", StringComparison.OrdinalIgnoreCase))
            .Select(p => (uint)p.Id)
            .ToHashSet();
        var windows = new List<TopLevelWindow>();
        Win32.EnumWindows((handle, _) =>
        {
            Win32.GetWindowThreadProcessId(handle, out var processId);
            if (processIds.Contains(processId))
                windows.Add(new TopLevelWindow(handle, Win32.Text(handle), Win32.ClassName(handle), Win32.IsWindowVisible(handle), (int)processId));
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}

/// <summary>Whether one TWS process has exited, for <see cref="OneTwsProcess"/>.</summary>
static class Win32Process
{
    /// <summary>
    /// Opens the process now and keeps its handle, so its id cannot pass to another process while it is asked about.
    /// A process that is not running counts as exited.
    /// </summary>
    public static Func<bool> HasExited(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
            _ = process.Handle;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return () => true;
        }
        return () => process.HasExited;
    }
}

/// <summary>Real time. Waiting handles window messages, which the Java Access Bridge needs to talk to TWS.</summary>
sealed class MessagePumpingClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public void Sleep(TimeSpan duration) => Win32.PumpMessages(duration);
}

/// <summary>Time-stamped lines on <paramref name="output"/>.</summary>
sealed class ConsoleLog(TextWriter output) : ILog
{
    public void Info(string message) => output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
}
