using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace tws_login_win;

/// <summary>
/// Authenticode through WinVerifyTrust: the file's signature, its certificate chain to a trusted root, and revocation
/// checked from the local cache only (no network fetch, so a slow or absent network never blocks a login). A
/// revocation status not in the cache is accepted and noted.
/// </summary>
public sealed class Authenticode : ISignatures
{
    public Signature Check(string path)
    {
        if (!File.Exists(path))
            return new Signature(false, null, "file not found");
        var result = Verify(path);
        var detail = result switch
        {
            0 => "",
            CertERevocationFailure => "revocation not checked: not in the local cache",
            TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown => "no signature",
            TrustEBadDigest => "the file was changed after it was signed",
            CertEUntrustedRoot or CertEChaining => "the certificate does not chain to a trusted root",
            CertERevoked => "the certificate was revoked",
            CertEExpired => "the certificate had expired when it was signed",
            TrustEExplicitDistrust => "the certificate is explicitly distrusted on this machine",
            _ => $"WinVerifyTrust result 0x{result:X8}",
        };
        var valid = result is 0 or CertERevocationFailure;
        return new Signature(valid, valid ? SignerOrganization(path) : null, detail);
    }

    static string? SignerOrganization(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // reads the signer of a signed file, which X509CertificateLoader does not cover
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            foreach (var rdn in signer.SubjectName.EnumerateRelativeDistinguishedNames())
                if (rdn.GetSingleElementType().Value == "2.5.4.10")
                    return rdn.GetSingleElementValue();
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    static int Verify(string path)
    {
        var action = GenericVerifyV2;
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfo = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), FilePath = pathPtr };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = UiNone,
                RevocationChecks = RevokeWholeChain,
                UnionChoice = ChoiceFile,
                File = filePtr,
                StateAction = StateActionVerify,
                ProvFlags = CacheOnlyUrlRetrieval | RevocationCheckChainExcludeRoot,
            };
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.StateAction = StateActionClose;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    const uint UiNone = 2, RevokeWholeChain = 1, ChoiceFile = 1, StateActionVerify = 1, StateActionClose = 2;
    const uint CacheOnlyUrlRetrieval = 0x1000, RevocationCheckChainExcludeRoot = 0x80;
    const int TrustENoSignature = unchecked((int)0x800B0100), TrustESubjectFormUnknown = unchecked((int)0x800B0003),
        TrustEProviderUnknown = unchecked((int)0x800B0001), TrustEBadDigest = unchecked((int)0x80096010),
        CertEUntrustedRoot = unchecked((int)0x800B0109), CertEChaining = unchecked((int)0x800B010A),
        CertERevoked = unchecked((int)0x800B010C), CertERevocationFailure = unchecked((int)0x800B010E),
        CertEExpired = unchecked((int)0x800B0101), TrustEExplicitDistrust = unchecked((int)0x800B0111);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [StructLayout(LayoutKind.Sequential)]
    struct FileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}

/// <summary>Running processes' executables, read from Windows.</summary>
sealed class Win32ProcessImages : IProcessImages
{
    public ProcessImage? Find(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var exe = process.MainModule?.FileName;
            return exe is null ? null : new ProcessImage(exe, process.StartTime);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
