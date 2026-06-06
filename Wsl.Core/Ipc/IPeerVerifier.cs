namespace Wsl.Core.Ipc;

public interface IPeerVerifier
{
    /// <summary>True if the process at <paramref name="pid"/> is an acceptable peer:
    /// same user, image path matches the expected exe, and Authenticode signature is valid
    /// (or, for dev builds, the path matches and signature check is bypassed by policy).</summary>
    bool IsTrustedPeer(int pid, string expectedExeName);
}
