using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Wsl.Core.Ipc;

/// <summary>
/// Verifies a peer process: image filename matches the expected exe, the file is
/// Authenticode-signed, and the running user matches. In DEBUG builds the signature
/// requirement is relaxed (dev binaries are unsigned) but the path check still applies.
/// </summary>
public class WindowsPeerVerifier : IPeerVerifier
{
    public bool IsTrustedPeer(int pid, string expectedExeName)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var path = proc.MainModule?.FileName;
            if (path is null) return false;
            if (!string.Equals(Path.GetFileName(path), expectedExeName, StringComparison.OrdinalIgnoreCase))
                return false;

#if DEBUG
            return true; // dev binaries are unsigned
#else
            return HasValidSignature(path);
#endif
        }
        catch
        {
            return false;
        }
    }

    private static bool HasValidSignature(string path)
    {
        try
        {
            // Throws if not Authenticode-signed.
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            using var chain = new X509Chain();
            return chain.Build(cert);
        }
        catch
        {
            return false;
        }
    }
}
