using System.Drawing;
using System.Text.RegularExpressions;

namespace tws_login_win;

/// <summary>The roles of TWS's controls, as the Java Access Bridge names them in English.</summary>
public static class Roles
{
    public const string PushButton = "push button";
    public const string ToggleButton = "toggle button";
    public const string Label = "label";
    public const string Text = "text";
    public const string PasswordText = "password text";
    public const string List = "list";
}

/// <summary>
/// One control of a TWS window as the Java Access Bridge describes it. <see cref="Text"/> is null where it is not
/// read: password fields, and controls without text. <see cref="BridgeHandle"/> identifies it to the bridge for
/// actions, and only until its <see cref="ControlTree"/> is disposed.
/// </summary>
public sealed record Control(
    string Role,
    string Name,
    IReadOnlySet<string> States,
    Rectangle Bounds,
    int TextLength,
    string? Text,
    IReadOnlyList<Control> Children,
    object BridgeHandle)
{
    /// <summary>On screen now. Controls TWS keeps but does not show report no bounds.</summary>
    public bool Showing => States.Contains("showing") && Bounds.Width >= 0 && Bounds.Height >= 0;

    public bool Enabled => States.Contains("enabled");

    public bool Checked => States.Contains("checked");

    public bool Selected => States.Contains("selected");

    public bool Editable => States.Contains("editable");

    public bool SingleLine => States.Contains("single line");

    /// <summary>A field the user types into: the user name field, the code box.</summary>
    public bool IsTextField => Role == Roles.Text && Editable && SingleLine;

    /// <summary>
    /// May hold what someone typed (user name, password, code), even once TWS makes it read-only: a password field, or
    /// a text field that is editable or single-line. Logged by length only.
    /// </summary>
    public bool MayHoldTypedText => Role == Roles.PasswordText || Role == Roles.Text && (Editable || SingleLine);

    public IEnumerable<Control> SelfAndDescendants() => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    /// <summary>For logs: role, name and position. Never the text: a field may hold the user name or the code.</summary>
    public override string ToString() => $"[{Role}] '{Name}' at {Bounds.X},{Bounds.Y}";
}

/// <summary>
/// The controls of one TWS window, read at one moment. Disposing it lets the bridge release them; read again to see
/// changes.
/// </summary>
public sealed partial class ControlTree(Control root, Action release) : IDisposable
{
    public Control Root => root;

    /// <summary>Every control on screen now.</summary>
    public IEnumerable<Control> Showing => root.SelfAndDescendants().Where(c => c.Showing);

    /// <summary>The showing controls with this role and exact name.</summary>
    public IReadOnlyList<Control> Find(string role, string name) =>
        Showing.Where(c => c.Role == role && c.Name == name).ToList();

    /// <summary>The one showing control that matches; null when none or more than one does.</summary>
    public Control? One(Func<Control, bool> match) => Showing.Where(match).ToList() is [var only] ? only : null;

    /// <summary>The one showing push button named <paramref name="name"/>; null when there is none or more than one.</summary>
    public Control? Button(string name) => One(c => c.Role == Roles.PushButton && c.Name == name);

    /// <summary>
    /// What the window says: the names of showing labels and the text of showing read-only text. Never the contents of
    /// a field the user can type into, so the user name and the code are never part of it.
    /// </summary>
    public string Message() => string.Join(" / ", Showing
        .Select(c => c.Role switch
        {
            Roles.Label => c.Name,
            Roles.Text when !c.Editable => c.Text ?? "",
            _ => "",
        })
        .Select(t => t.Trim())
        .Where(t => t.Length > 0));

    /// <summary>
    /// Changes whenever a control shows, hides, or changes name, state or text length; not with the text itself.
    /// Used to tell a window that is stuck from one that is working.
    /// </summary>
    public string Signature() => string.Join("|", Showing.Select(c =>
        $"{c.Role}:{c.Name}:{string.Join(",", c.States.Order())}:{c.TextLength}:{c.Bounds}"));

    public void Dispose() => release();

    /// <summary>For logs: digits masked, as TWS text may show an account number or an IP address.</summary>
    public static string MaskDigits(string text) => Digit().Replace(text, "#");

    [GeneratedRegex(@"\d")]
    private static partial Regex Digit();
}
