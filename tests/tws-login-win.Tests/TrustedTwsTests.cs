using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// Only a trusted TWS, a process whose executable Interactive Brokers validly signed, is seen at all: another
/// program's Login window is never read, pressed or typed into.
/// </summary>
public class TrustedTwsTests
{
    const int Tws = 7001, Impostor = 7002;
    static readonly DateTime Started = new(2026, 11, 5, 9, 0, 0);

    readonly FakeTws _tws = new();
    readonly FakeImages _images = new();
    readonly FakeSignatures _signatures = new();

    public TrustedTwsTests()
    {
        _images.Running[Tws] = new ProcessImage(@"C:\Jts\1051\tws1.exe", Started);
        _signatures.Of[@"C:\Jts\1051\tws1.exe"] = new Signature(true, TrustedTws.Publisher, "");
    }

    TwsTools Scoped() => TrustedTws.Scope(_tws.Tools, _images, _signatures);

    void AddImpostor(Signature signature, string exe = @"C:\Users\x\Downloads\tws.exe")
    {
        _images.Running[Impostor] = new ProcessImage(exe, Started);
        _signatures.Of[exe] = signature;
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: Impostor);
    }

    [Fact]
    public void A_TWS_signed_by_Interactive_Brokers_is_seen()
    {
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: Tws);

        Assert.Equal(["7001 form"], LoginWindows.Lines(Scoped()));
        Assert.Contains(_tws.Logged, line => line.Contains("pid 7001") && line.Contains(TrustedTws.Publisher));
    }

    [Theory]
    [InlineData(false, null, "no signature")]
    [InlineData(true, "Someone Else, Inc.", "")]
    [InlineData(false, TrustedTws.Publisher, "the file was changed after it was signed")]
    [InlineData(true, "Interactive Brokers Group, Inc", "")] // near enough is not enough
    public void Any_other_program_is_left_out_and_logged(bool valid, string? organization, string detail)
    {
        AddImpostor(new Signature(valid, organization, detail));

        Assert.Empty(LoginWindows.Lines(Scoped()));
        Assert.Contains(_tws.Logged, line => line.StartsWith("ignoring pid 7002") && line.Contains("--trust-unsigned"));
    }

    [Fact]
    public void An_impostor_beside_a_real_TWS_never_gets_the_credentials()
    {
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: Tws);
        AddImpostor(new Signature(false, null, "no signature"));

        // Two Login windows would make the login refuse; the impostor's is not even seen, so only TWS's is found.
        var form = new LoginFormLocator(Scoped()).FindNow();

        Assert.NotNull(form);
        Assert.Equal(Tws, form.Window.ProcessId);
    }

    [Fact]
    public void A_process_not_running_from_the_signed_file_at_its_path_is_left_out()
    {
        // E.g. it renamed its own executable and put a copy of the signed one at the old path.
        _images.Running[Tws] = _images.Running[Tws] with { Problem = "it is not running from the file now at that path" };
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: Tws);

        Assert.Empty(LoginWindows.Lines(Scoped()));
        Assert.Equal(0, _signatures.Checks);
        Assert.Contains(_tws.Logged, line => line.StartsWith("ignoring pid 7001") && line.Contains("not running from the file"));
    }

    [Fact]
    public void The_executable_is_held_only_while_it_is_judged()
    {
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: Tws);

        LoginWindows.Lines(Scoped());

        Assert.Equal(1, _images.Released);
    }

    [Fact]
    public void A_process_that_cannot_be_opened_is_left_out()
    {
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: 7003);

        Assert.Empty(LoginWindows.Lines(Scoped()));
    }

    [Fact]
    public void Each_process_is_judged_once_until_its_id_is_reused()
    {
        _tws.Add("Login", "SunAwtFrame", new FakeLoginForm().Build, processId: Tws);
        var tools = Scoped();

        LoginWindows.Lines(tools);
        LoginWindows.Lines(tools);
        Assert.Equal(1, _signatures.Checks);

        // The id now belongs to another program, started later.
        _images.Running[Tws] = new ProcessImage(@"C:\evil\tws.exe", Started.AddMinutes(5));
        _signatures.Of[@"C:\evil\tws.exe"] = new Signature(false, null, "no signature");

        Assert.Empty(LoginWindows.Lines(tools));
        Assert.Equal(2, _signatures.Checks);
    }

    sealed class FakeImages : IProcessImages
    {
        public Dictionary<int, ProcessImage> Running { get; } = [];

        public int Released { get; private set; }

        public DateTime? StartTime(int processId) => Running.GetValueOrDefault(processId)?.StartTime;

        public ProcessImage? Find(int processId) =>
            Running.GetValueOrDefault(processId) is { } image ? image with { Release = () => Released++ } : null;
    }

    sealed class FakeSignatures : ISignatures
    {
        public Dictionary<string, Signature> Of { get; } = [];

        public int Checks { get; private set; }

        public Signature Check(string path)
        {
            Checks++;
            return Of.GetValueOrDefault(path) ?? new Signature(false, null, "file not found");
        }
    }
}

/// <summary>Authenticode against real files: a Microsoft-signed executable, an unsigned file, a tampered copy.</summary>
public sealed class AuthenticodeTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("tws-login-win-sig").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>The .NET runtime's core library, signed by Microsoft with an embedded signature.</summary>
    static string SignedExe => typeof(object).Assembly.Location;

    [Fact]
    public void A_signed_file_is_valid_and_names_its_publisher()
    {
        var signature = new Authenticode().Check(SignedExe);

        Assert.True(signature.Valid, signature.ToString());
        Assert.Equal("Microsoft Corporation", signature.Organization);
        Assert.False(TrustedTws.IsPublisher(signature));
    }

    [Fact]
    public void An_unsigned_file_is_not_valid()
    {
        var path = Path.Combine(_folder, "tws.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 1, 2, 3]);

        var signature = new Authenticode().Check(path);

        Assert.False(signature.Valid);
        Assert.Null(signature.Organization);
    }

    [Fact]
    public void A_changed_copy_of_a_signed_file_is_not_valid()
    {
        var path = Path.Combine(_folder, "tws.exe");
        File.Copy(SignedExe, path);
        var bytes = File.ReadAllBytes(path);
        bytes[0x1000] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var signature = new Authenticode().Check(path);

        Assert.False(signature.Valid, signature.ToString());
    }

    [Fact]
    public void A_missing_file_is_not_valid()
    {
        Assert.False(new Authenticode().Check(Path.Combine(_folder, "none.exe")).Valid);
    }
}

/// <summary>Windows' record of real running processes, including one that swaps the file at its own path.</summary>
public sealed class ProcessImagesTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("tws-login-win-img").FullName;
    readonly List<System.Diagnostics.Process> _started = [];

    public void Dispose()
    {
        foreach (var p in _started)
        {
            try { p.Kill(); p.WaitForExit(5000); } catch (InvalidOperationException) { }
            p.Dispose();
        }
        Directory.Delete(_folder, recursive: true);
    }

    System.Diagnostics.Process StartCopyOfPing(string path)
    {
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), path);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, "-n 30 127.0.0.1")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        })!;
        _started.Add(process);
        // Its modules are listed only once it has started up.
        for (var i = 0; i < 100; i++)
        {
            using var image = new Win32ProcessImages().Find(process.Id);
            if (image is not null)
                break;
            Thread.Sleep(50);
        }
        return process;
    }

    [Fact]
    public void A_running_process_is_found_at_its_own_file()
    {
        var path = Path.Combine(_folder, "tws.exe");
        var process = StartCopyOfPing(path);

        using var image = new Win32ProcessImages().Find(process.Id);

        Assert.NotNull(image);
        Assert.Null(image.Problem);
        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(image.ExePath), ignoreCase: true);
        Assert.NotNull(new Win32ProcessImages().StartTime(process.Id));
    }

    [Fact]
    public void A_process_that_renamed_its_file_and_left_another_at_the_old_path_is_judged_by_its_real_file()
    {
        var path = Path.Combine(_folder, "tws.exe");
        var process = StartCopyOfPing(path);
        File.Move(path, Path.Combine(_folder, "tws_old.exe")); // Windows lets a running executable be renamed
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), path); // e.g. the signed TWS, copied

        using var image = new Win32ProcessImages().Find(process.Id);

        // Windows names the file the process really runs from, under its new name: that is the file judged, not the
        // copy now at the old path.
        Assert.NotNull(image);
        Assert.Equal("tws_old.exe", Path.GetFileName(image.ExePath), ignoreCase: true);
        Assert.Null(image.Problem);
    }

    [Fact]
    public void While_held_the_executable_cannot_be_renamed()
    {
        var path = Path.Combine(_folder, "tws.exe");
        var process = StartCopyOfPing(path);

        using (new Win32ProcessImages().Find(process.Id))
            Assert.ThrowsAny<IOException>(() => File.Move(path, Path.Combine(_folder, "moved.exe")));

        File.Move(path, Path.Combine(_folder, "moved.exe")); // released: allowed again
    }

    [Fact]
    public void A_process_that_has_exited_is_not_found()
    {
        var process = StartCopyOfPing(Path.Combine(_folder, "tws.exe"));
        process.Kill();
        process.WaitForExit();

        Assert.Null(new Win32ProcessImages().Find(process.Id));
    }
}
