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

        public ProcessImage? Find(int processId) => Running.GetValueOrDefault(processId);
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
