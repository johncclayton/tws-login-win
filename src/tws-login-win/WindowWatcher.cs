namespace tws_login_win;

/// <summary>
/// Diagnostics for learning what TWS shows: logs each TWS window as it appears or changes title, and prints the
/// controls of the Login window and of dialogs whenever they change.
/// <list type="bullet">
/// <item>Windows already open when watching starts (another TWS instance) are left alone.</item>
/// <item>Only the Login window and dialogs are read. Other frames, the main window among them, show account data, so
/// they are logged by title only.</item>
/// <item>Fields that may hold typed text are shown by length only, so the user name, password and code never are; all
/// digits are masked.</item>
/// </list>
/// </summary>
public sealed class WindowWatcher(TwsTools tools)
{
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public void Watch(TimeSpan duration)
    {
        var start = tools.Clock.Now;
        var existing = tools.Windows().Select(w => w.Handle).ToHashSet();
        tools.Log.Info($"watching for {duration.TotalSeconds:0}s; leaving alone {existing.Count} TWS windows already open");
        var titles = new Dictionary<IntPtr, string>();
        var loginWindows = new HashSet<IntPtr>();
        var signatures = new Dictionary<IntPtr, string>();
        while (tools.Clock.Now - start < duration)
        {
            foreach (var window in tools.Windows().Where(w => w.Visible))
            {
                if (existing.Contains(window.Handle))
                    continue;
                var at = $"+{(tools.Clock.Now - start).TotalSeconds:0}s";
                if (!titles.TryGetValue(window.Handle, out var title) || title != window.Title)
                {
                    titles[window.Handle] = window.Title;
                    tools.Log.Info($"{at} window {window} class={window.ClassName}");
                }
                // TWS retitles the Login window for later steps ("Second Factor Authentication"); it stays the Login window.
                if (window.Title == LoginFormText.WindowTitle)
                    loginWindows.Add(window.Handle);
                if (!loginWindows.Contains(window.Handle) && !window.IsDialog)
                    continue;

                using var tree = window.TryControls();
                if (tree is null)
                {
                    if (signatures.TryAdd(window.Handle, ""))
                        tools.Log.Info($"{at} no controls readable in {window}: {TwsWindow.BridgeHelp}");
                    continue;
                }
                var signature = tree.Signature();
                if (signatures.TryGetValue(window.Handle, out var last) && last == signature)
                    continue;
                signatures[window.Handle] = signature;
                tools.Log.Info($"{at} controls of {window}:");
                foreach (var line in ControlDump.Lines(tree))
                    tools.Log.Info("  " + line);
            }
            tools.Clock.Sleep(PollInterval);
        }
        tools.Log.Info("observation finished");
    }
}

/// <summary>A window's showing controls as text, safe to log: fields that may hold typed text by length only, digits masked.</summary>
public static class ControlDump
{
    public static IEnumerable<string> Lines(ControlTree tree) => Lines(tree.Root, 0);

    static IEnumerable<string> Lines(Control control, int depth)
    {
        if (!control.Showing)
            yield break;
        bool field = control.MayHoldTypedText;
        var name = field ? $"<{control.Name.Length} chars>" : ControlTree.MaskDigits(control.Name);
        // TWS's link-like texts start with a line break; shown on one line.
        var text = field || control.Text is null ? "" : $" text='{ControlTree.MaskDigits(control.Text.ReplaceLineEndings(" ").Trim())}'";
        yield return $"{new string(' ', depth * 2)}[{control.Role}] '{name}' {{{string.Join(",", control.States.Order())}}} " +
                     $"at {control.Bounds.X},{control.Bounds.Y} {control.Bounds.Width}x{control.Bounds.Height} len={control.TextLength}{text}";
        foreach (var child in control.Children)
            foreach (var line in Lines(child, depth + 1))
                yield return line;
    }
}
