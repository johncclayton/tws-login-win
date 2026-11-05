using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace tws_login_win;

/// <summary>
/// Authenticode through WinVerifyTrust: the file's signature, its certificate chain to a trusted root, and revocation
/// checked from the local cache only (no network fetch, so a slow or absent network never blocks a login). A
/// revocation status not in the cache is accepted and noted. The file is held open while it is judged, so it cannot
/// be changed, renamed or deleted meanwhile, and the signer is read from the same verification, not the file again.
/// </summary>
public sealed class Authenticode : ISignatures
{
    public Signature Check(string path)
    {
        FileStream file;
        try
        {
            file = Files.Hold(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Signature(false, null, "file not found");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Signature(false, null, $"cannot be opened to check: {ex.Message}");
        }
        using (file)
        {
            var (result, organization) = Verify(path, file.SafeFileHandle);
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
            return new Signature(valid, valid ? organization : null, detail);
        }
    }

    static (int Result, string? Organization) Verify(string path, SafeFileHandle handle)
    {
        var action = GenericVerifyV2;
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            var fileInfo = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), FilePath = pathPtr, FileHandle = handle.DangerousGetHandle() };
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
            try
            {
                return (result, result is 0 or CertERevocationFailure ? SignerOrganization(data.StateData) : null);
            }
            finally
            {
                data.StateAction = StateActionClose;
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            }
        }
        finally
        {
            if (added)
                handle.DangerousRelease();
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    /// <summary>The organisation (O=) of the signing certificate that WinVerifyTrust just verified.</summary>
    static string? SignerOrganization(IntPtr state)
    {
        var provider = WTHelperProvDataFromStateData(state);
        if (provider == IntPtr.Zero)
            return null;
        var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer == IntPtr.Zero)
            return null;
        var info = Marshal.PtrToStructure<ProviderSigner>(signer);
        if (info.CertCount == 0 || info.Certs == IntPtr.Zero)
            return null;
        var leaf = Marshal.PtrToStructure<ProviderCert>(info.Certs).Cert; // [0] is the signing certificate
        if (leaf == IntPtr.Zero)
            return null;
        using var certificate = new X509Certificate2(leaf);
        foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
            if (rdn.HasMultipleElements is false && rdn.GetSingleElementType().Value == "2.5.4.10")
                return rdn.GetSingleElementValue();
        return null;
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

    [DllImport("wintrust.dll")]
    static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);

    [DllImport("wintrust.dll")]
    static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, bool counterSigner, uint counterSignerIndex);

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

    /// <summary>The start of CRYPT_PROVIDER_SGNR.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct ProviderSigner
    {
        public uint Size;
        public uint VerifyAsOfLow, VerifyAsOfHigh; // FILETIME: two DWORDs, so 4-byte aligned
        public uint CertCount;
        public IntPtr Certs;
    }

    /// <summary>The start of CRYPT_PROVIDER_CERT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct ProviderCert
    {
        public uint Size;
        public IntPtr Cert;
    }
}

static class Files
{
    /// <summary>
    /// Opens <paramref name="path"/> for reading while letting others only read it: until the stream is disposed, the
    /// file cannot be written, renamed, replaced or deleted.
    /// </summary>
    public static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>The volume and file id of an open file: equal for two handles exactly when they are the same file.</summary>
    public static (uint Volume, ulong File)? Id(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info)
            ? (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow)
            : null;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);

    [StructLayout(LayoutKind.Sequential)]
    struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}

/// <summary>
/// Running processes' executables, as Windows itself records them. The path is the kernel's, not the process's own
/// memory (which the process can rewrite), and it follows a rename: a process that renamed its executable and put a
/// signed copy at the old path is judged by its real, renamed file. As a second check, the file mapped at the
/// process's image base must be that same file.
/// </summary>
sealed class Win32ProcessImages : IProcessImages
{
    const uint QueryLimitedInformation = 0x1000, QueryInformation = 0x0400, VmRead = 0x0010;

    public DateTime? StartTime(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public ProcessImage? Find(int processId)
    {
        DateTime started;
        IntPtr imageBase;
        try
        {
            using var process = Process.GetProcessById(processId);
            started = process.StartTime;
            imageBase = process.MainModule?.BaseAddress ?? IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        var handle = OpenProcess(QueryLimitedInformation | QueryInformation | VmRead, false, (uint)processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var path = KernelImagePath(handle);
            if (path is null)
                return null;
            FileStream held;
            try
            {
                held = Files.Hold(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ProcessImage(path, started, $"its executable cannot be opened: {ex.Message}");
            }
            var problem = RunsFrom(handle, imageBase, held) ? null : "it is not running from the file now at that path";
            return new ProcessImage(path, started, problem, held.Dispose);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The kernel's path of the process's executable (it follows a rename); null when it cannot be opened.</summary>
    public static string? ImagePath(int processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            return KernelImagePath(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static string? KernelImagePath(IntPtr process)
    {
        var buffer = new char[32768];
        var size = buffer.Length;
        return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    /// <summary>Whether the file mapped at the process's image base is the one <paramref name="held"/> has open.</summary>
    static bool RunsFrom(IntPtr process, IntPtr imageBase, FileStream held)
    {
        if (imageBase == IntPtr.Zero)
            return false;
        var buffer = new char[32768];
        var length = GetMappedFileName(process, imageBase, buffer, buffer.Length);
        if (length == 0)
            return false;
        var device = new string(buffer, 0, length); // \Device\HarddiskVolumeN\..., the file's current name
        try
        {
            using var mapped = new FileStream(@"\\?\GLOBALROOT" + device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Files.Id(mapped.SafeFileHandle) is { } a && Files.Id(held.SafeFileHandle) is { } b && a == b;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] name, ref int size);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetMappedFileNameW")]
    static extern int GetMappedFileName(IntPtr process, IntPtr address, [Out] char[] name, int size);
}
