using System.Runtime.InteropServices;
using System.Text;

namespace tws_login_win;

static class Win32
{
    const uint PmRemove = 1;

    public delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Msg message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Msg message);

    public static string Text(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    public static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    /// <summary>Waits <paramref name="duration"/> while handling this thread's window messages, as the Java Access Bridge needs.</summary>
    public static void PumpMessages(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        do
        {
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            var left = end - DateTime.UtcNow;
            if (left > TimeSpan.Zero)
                Thread.Sleep(left < TimeSpan.FromMilliseconds(15) ? left : TimeSpan.FromMilliseconds(15));
        }
        while (DateTime.UtcNow < end);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }
}
