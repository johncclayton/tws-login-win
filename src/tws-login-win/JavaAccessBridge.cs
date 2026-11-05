using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace tws_login_win;

/// <summary>
/// The Java Access Bridge that ships with TWS's own Java runtime (<c>jre\bin\windowsaccessbridge-64.dll</c>), loaded
/// from the folder of the TWS process whose window is read first. TWS loads its side of the bridge only when it was
/// enabled with <c>jabswitch -enable</c> before TWS started; otherwise no window has an accessible context.
/// The bridge talks to Java through window messages, so this thread must pump them: see <see cref="Win32.PumpMessages"/>.
/// The DLL must carry a valid Authenticode signature unless <c>requireSigned</c> is false (<c>--trust-unsigned</c>).
/// </summary>
sealed class JavaAccessBridge(ISignatures signatures, bool requireSigned) : IAccessBridge
{
    const string Dll = "windowsaccessbridge-64.dll";
    const int MaxDepth = 50, MaxChildren = 300, MaxText = 2000;
    static readonly TimeSpan StartUp = TimeSpan.FromSeconds(1);

    bool _started;

    public ControlTree? Read(IntPtr window)
    {
        Start(window);
        if (Native.isJavaWindow(window) == 0 || Native.getAccessibleContextFromHWND(window, out var vm, out var root) == 0)
            return null;
        var refs = new List<long> { root };
        Control? tree;
        try
        {
            tree = Walk(vm, root, 0, refs);
        }
        catch
        {
            Release(vm, refs);
            throw;
        }
        if (tree is null)
        {
            Release(vm, refs);
            return null;
        }
        return new ControlTree(tree, () => Release(vm, refs));
    }

    public bool Press(Control control)
    {
        var (vm, ac) = (Handle)control.BridgeHandle;
        const int NameBytes = 256 * 2;
        IntPtr todo = Marshal.AllocHGlobal(4 + 32 * NameBytes);
        try
        {
            for (int i = 0; i < 4 + 32 * NameBytes; i++)
                Marshal.WriteByte(todo, i, 0);
            Marshal.WriteInt32(todo, 1);
            var name = "click".ToCharArray();
            Marshal.Copy(name, 0, todo + 4, name.Length);
            return Native.doAccessibleActions(vm, ac, todo, out _) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(todo);
        }
    }

    public bool SetText(Control control, string text)
    {
        var (vm, ac) = (Handle)control.BridgeHandle;
        return Native.setTextContents(vm, ac, text) != 0;
    }

    /// <summary>A control's Java object in the bridge: valid until its tree is released.</summary>
    sealed record Handle(int Vm, long Context);

    /// <summary>Loads the DLL from the TWS install of the window's process and starts the bridge, once.</summary>
    void Start(IntPtr window)
    {
        if (_started)
            return;
        Win32.GetWindowThreadProcessId(window, out var processId);
        string folder;
        try
        {
            var exe = Process.GetProcessById((int)processId).MainModule?.FileName
                ?? throw new LoginAbortedException("cannot tell where TWS is installed.");
            folder = Path.Combine(Path.GetDirectoryName(exe)!, "jre", "bin");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new LoginAbortedException($"cannot tell where TWS is installed: {ex.Message}");
        }
        var path = Path.Combine(folder, Dll);
        if (!File.Exists(path))
            throw new LoginAbortedException($"no {Dll} in {folder}: this TWS has no Java Access Bridge.");
        if (requireSigned && signatures.Check(path) is { Valid: false } signature)
            throw new LoginAbortedException($"{path} is {signature}; not loading it (--trust-unsigned overrides this).");
        Win32.SetDllDirectory(folder);
        NativeLibrary.Load(path);
        Native.Windows_run();
        Win32.PumpMessages(StartUp); // the bridge finds the Java VMs through window messages
        _started = true;
    }

    static Control? Walk(int vm, long ac, int depth, List<long> refs)
    {
        if (Native.getAccessibleContextInfo(vm, ac, out var info) == 0)
            return null;
        int length = 0;
        string? text = null;
        if (info.accessibleText != 0 && Native.getAccessibleTextInfo(vm, ac, out var textInfo, 0, 0) != 0)
        {
            length = textInfo.charCount;
            // A password field's text is never read, only counted.
            if (length > 0 && info.role_en_US != Roles.PasswordText)
            {
                var buffer = new StringBuilder(Math.Min(length, MaxText) + 1);
                if (Native.getAccessibleTextRange(vm, ac, 0, Math.Min(length, MaxText) - 1, buffer, (short)buffer.Capacity) != 0)
                    text = buffer.ToString();
            }
            else if (length == 0)
                text = "";
        }
        var children = new List<Control>();
        if (depth < MaxDepth)
            for (int i = 0; i < Math.Min(info.childrenCount, MaxChildren); i++)
            {
                long child = Native.getAccessibleChildFromContext(vm, ac, i);
                if (child == 0)
                    continue;
                refs.Add(child);
                if (Walk(vm, child, depth + 1, refs) is { } control)
                    children.Add(control);
            }
        var states = info.states_en_US.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        return new Control(info.role_en_US, info.name, states, new Rectangle(info.x, info.y, info.width, info.height),
            length, text, children, new Handle(vm, ac));
    }

    static void Release(int vm, List<long> refs)
    {
        foreach (var r in refs)
            Native.releaseJavaObject(vm, r);
        refs.Clear();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ContextInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string role;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string role_en_US;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string states;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string states_en_US;
        public int indexInParent, childrenCount, x, y, width, height;
        public int accessibleComponent, accessibleAction, accessibleSelection, accessibleText, accessibleInterfaces;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TextInfo
    {
        public int charCount, caretIndex, indexAtPoint;
    }

    /// <summary>The 64-bit bridge API: VM ids are 32-bit, Java object references 64-bit (JOBJECT64), cdecl.</summary>
    static class Native
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void Windows_run();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int isJavaWindow(IntPtr window);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int getAccessibleContextFromHWND(IntPtr window, out int vm, out long context);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int getAccessibleContextInfo(int vm, long context, out ContextInfo info);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern long getAccessibleChildFromContext(int vm, long context, int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int getAccessibleTextInfo(int vm, long context, out TextInfo info, int x, int y);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] public static extern int getAccessibleTextRange(int vm, long context, int start, int end, StringBuilder text, short length);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] public static extern int setTextContents(int vm, long context, string text);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int doAccessibleActions(int vm, long context, IntPtr actions, out int failure);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void releaseJavaObject(int vm, long obj);
    }
}
