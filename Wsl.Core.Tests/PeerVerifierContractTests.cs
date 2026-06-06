using Wsl.Core.Ipc;
using Xunit;

namespace Wsl.Core.Tests;

/// <summary>A deterministic verifier used by server/client tests.</summary>
public sealed class FakePeerVerifier : IPeerVerifier
{
    private readonly bool _trusted;
    public int? LastPid { get; private set; }
    public string? LastExpected { get; private set; }
    public FakePeerVerifier(bool trusted) => _trusted = trusted;

    public bool IsTrustedPeer(int pid, string expectedExeName)
    {
        LastPid = pid;
        LastExpected = expectedExeName;
        return _trusted;
    }
}

public class PeerVerifierContractTests
{
    [Fact]
    public void Trusted_verifier_returns_true_and_records_args()
    {
        var v = new FakePeerVerifier(trusted: true);
        Assert.True(v.IsTrustedPeer(1234, "Wsl.App.exe"));
        Assert.Equal(1234, v.LastPid);
        Assert.Equal("Wsl.App.exe", v.LastExpected);
    }

    [Fact]
    public void Untrusted_verifier_returns_false()
    {
        var v = new FakePeerVerifier(trusted: false);
        Assert.False(v.IsTrustedPeer(1234, "Wsl.App.exe"));
    }
}
