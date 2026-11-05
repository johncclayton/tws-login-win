namespace tws_login_win;

/// <summary>
/// A file's Authenticode signature as Windows judges it. <see cref="Organization"/> is the signer certificate's
/// organisation (O=), null when the file is unsigned or the certificate names none; <see cref="Detail"/> says why a
/// signature is not valid, or notes a revocation check that could not be made.
/// </summary>
public sealed record Signature(bool Valid, string? Organization, string Detail)
{
    public override string ToString() =>
        Valid ? $"signed by '{Organization ?? "(no organisation)"}'{(Detail.Length > 0 ? $" ({Detail})" : "")}" : $"not validly signed: {Detail}";
}

/// <summary>Judges files' Authenticode signatures.</summary>
public interface ISignatures
{
    Signature Check(string path);
}

/// <summary>A running process's identity: its executable, and when it started (a process id can be reused).</summary>
public sealed record ProcessImage(string ExePath, DateTime StartTime);

/// <summary>Finds a running process's executable.</summary>
public interface IProcessImages
{
    /// <summary>The process's image; null when it is not running or cannot be opened.</summary>
    ProcessImage? Find(int processId);
}

/// <summary>
/// Only a <b>trusted TWS</b> gets credentials: a TWS process whose executable carries a valid Authenticode signature
/// from <see cref="Publisher"/>. Any program can name itself tws.exe and show a window titled "Login"; this keeps the
/// user name, password and one-time code from going to one. See docs/adr/0002-trust-only-ibkr-signed-tws.md.
/// </summary>
public static class TrustedTws
{
    /// <summary>The organisation in the certificate Interactive Brokers signs TWS with.</summary>
    public const string Publisher = "Interactive Brokers Group, Inc.";

    /// <summary>
    /// <paramref name="tools"/> seeing only trusted TWS processes' windows. Each process is judged once (per start
    /// time) and an untrusted one is logged once, then left out: it is never read, pressed or typed into.
    /// </summary>
    public static TwsTools Scope(TwsTools tools, IProcessImages images, ISignatures signatures) =>
        tools with { Desktop = new Desktop(tools.Desktop, images, signatures, tools.Log) };

    /// <summary>Whether <paramref name="signature"/> is one this tool trusts a TWS executable with.</summary>
    public static bool IsPublisher(Signature signature) =>
        signature.Valid && string.Equals(signature.Organization, Publisher, StringComparison.Ordinal);

    sealed class Desktop(IDesktop inner, IProcessImages images, ISignatures signatures, ILog log) : IDesktop
    {
        readonly Dictionary<int, (DateTime Started, bool Trusted)> _judged = [];

        public IReadOnlyList<TopLevelWindow> TwsWindows() =>
            inner.TwsWindows().Where(w => Trusted(w.ProcessId)).ToList();

        bool Trusted(int processId)
        {
            var image = images.Find(processId);
            if (image is null)
                return false; // gone, or not openable: nothing can be checked, so nothing is sent
            if (_judged.TryGetValue(processId, out var judged) && judged.Started == image.StartTime)
                return judged.Trusted;
            var signature = signatures.Check(image.ExePath);
            var trusted = IsPublisher(signature);
            _judged[processId] = (image.StartTime, trusted);
            if (trusted)
                log.Info($"TWS pid {processId} ({image.ExePath}) is {signature}");
            else
                log.Info($"ignoring pid {processId} ({image.ExePath}): {signature}, not by '{Publisher}'. "
                         + "Its windows are never read or typed into (--trust-unsigned overrides this).");
            return trusted;
        }
    }
}
