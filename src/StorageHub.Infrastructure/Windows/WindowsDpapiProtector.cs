using System.Runtime.InteropServices;
using System.Security.Cryptography;
using StorageHub.Security;

namespace StorageHub.Infrastructure.Windows;

/// <summary>
/// Which key DPAPI derives from, and therefore who can read the result back.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public enum DpapiProtectionScope
{
    /// <summary>
    /// The signed-in user. Nothing outside that account can decrypt the payload, which is why it
    /// is the default, and why a payload written this way is unreadable to a service.
    /// </summary>
    CurrentUser = 0,

    /// <summary>
    /// The machine. Required when a Windows service must read secrets with no user signed in.
    /// Any administrator on the machine can decrypt a machine-scoped payload, so the vault's
    /// per-secret entropy carries the real separation rather than the DPAPI key alone.
    /// </summary>
    LocalMachine = 1,
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed partial class WindowsDpapiProtector : ISecretProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const uint CryptProtectLocalMachine = 0x4;

    private readonly DpapiProtectionScope _scope;

    public WindowsDpapiProtector(DpapiProtectionScope scope = DpapiProtectionScope.CurrentUser)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        _scope = scope;
    }

    /// <summary>
    /// The scheme is part of every stored envelope and the vault refuses an entry whose scheme is
    /// not its own. That is deliberate: it makes a user-scoped vault fail loudly under a service
    /// rather than decrypt to nonsense, and it forces migration to be an explicit re-write.
    /// </summary>
    public string Scheme => _scope == DpapiProtectionScope.LocalMachine
        ? "windows-dpapi-local-machine-v1"
        : "windows-dpapi-current-user-v1";

    public unsafe byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
    {
        EnsureWindows();

        var flags = CryptProtectUiForbidden;
        if (_scope == DpapiProtectionScope.LocalMachine)
        {
            flags |= CryptProtectLocalMachine;
        }

        fixed (byte* plaintextPointer = plaintext)
        fixed (byte* entropyPointer = entropy)
        {
            var input = new DataBlob(plaintext.Length, plaintextPointer);
            var optionalEntropy = new DataBlob(entropy.Length, entropyPointer);
            var output = default(DataBlob);
            try
            {
                var succeeded = CryptProtectData(
                    &input,
                    0,
                    entropy.IsEmpty ? null : &optionalEntropy,
                    0,
                    0,
                    flags,
                    &output);
                if (succeeded == 0)
                {
                    throw CreateDpapiException("protect");
                }

                return CopyOutput(output);
            }
            finally
            {
                ReleaseOutput(output);
            }
        }
    }

    public unsafe byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy)
    {
        EnsureWindows();

        fixed (byte* protectedDataPointer = protectedData)
        fixed (byte* entropyPointer = entropy)
        {
            var input = new DataBlob(protectedData.Length, protectedDataPointer);
            var optionalEntropy = new DataBlob(entropy.Length, entropyPointer);
            var output = default(DataBlob);
            nint description = 0;
            try
            {
                var succeeded = CryptUnprotectData(
                    &input,
                    &description,
                    entropy.IsEmpty ? null : &optionalEntropy,
                    0,
                    0,
                    CryptProtectUiForbidden,
                    &output);
                if (succeeded == 0)
                {
                    throw CreateDpapiException("unprotect");
                }

                return CopyOutput(output);
            }
            finally
            {
                ReleaseOutput(output);
                if (description != 0)
                {
                    _ = LocalFree(description);
                }
            }
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is only available on Windows.");
        }
    }

    private static CryptographicException CreateDpapiException(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return new CryptographicException($"Windows DPAPI could not {operation} the payload (Win32 error {error}).");
    }

    private static unsafe byte[] CopyOutput(DataBlob output)
    {
        if (output.Size < 0 || (output.Size > 0 && output.Data is null))
        {
            throw new CryptographicException("Windows DPAPI returned an invalid payload.");
        }

        var result = new byte[output.Size];
        if (output.Size > 0)
        {
            new ReadOnlySpan<byte>(output.Data, output.Size).CopyTo(result);
        }

        return result;
    }

    private static unsafe void ReleaseOutput(DataBlob output)
    {
        if (output.Data is null)
        {
            return;
        }

        if (output.Size > 0)
        {
            CryptographicOperations.ZeroMemory(new Span<byte>(output.Data, output.Size));
        }

        _ = LocalFree((nint)output.Data);
    }

    [LibraryImport("Crypt32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int CryptProtectData(
        DataBlob* dataIn,
        nint dataDescription,
        DataBlob* optionalEntropy,
        nint reserved,
        nint prompt,
        uint flags,
        DataBlob* dataOut);

    [LibraryImport("Crypt32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int CryptUnprotectData(
        DataBlob* dataIn,
        nint* dataDescription,
        DataBlob* optionalEntropy,
        nint reserved,
        nint prompt,
        uint flags,
        DataBlob* dataOut);

    [LibraryImport("Kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LocalFree(nint memory);

    [StructLayout(LayoutKind.Sequential)]
    private readonly unsafe struct DataBlob(int size, byte* data)
    {
        public readonly int Size = size;
        public readonly byte* Data = data;
    }
}
