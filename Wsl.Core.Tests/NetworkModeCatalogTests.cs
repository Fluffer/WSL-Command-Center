using System.Linq;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class NetworkModeCatalogTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]          // doc: unknown value => NAT
    [InlineData("bridged")]        // deprecated; not offered, treat as unknown => NAT bucket
    public void Parse_NullEmptyOrUnknown_ReturnsNat(string? value)
        => Assert.Equal(WslNetworkMode.Nat, NetworkModeCatalog.Parse(value));

    [Theory]
    [InlineData("nat", WslNetworkMode.Nat)]
    [InlineData("NAT", WslNetworkMode.Nat)]
    [InlineData("Mirrored", WslNetworkMode.Mirrored)]
    [InlineData("mirrored", WslNetworkMode.Mirrored)]
    [InlineData("virtioproxy", WslNetworkMode.VirtioProxy)]
    [InlineData("VirtioProxy", WslNetworkMode.VirtioProxy)]
    [InlineData("none", WslNetworkMode.None)]
    public void Parse_KnownValues_CaseInsensitive(string value, WslNetworkMode expected)
        => Assert.Equal(expected, NetworkModeCatalog.Parse(value));

    [Theory]
    [InlineData(WslNetworkMode.Nat, "nat")]
    [InlineData(WslNetworkMode.Mirrored, "mirrored")]
    [InlineData(WslNetworkMode.VirtioProxy, "virtioproxy")]
    [InlineData(WslNetworkMode.None, "none")]
    public void ConfigValue_MatchesWslconfigToken(WslNetworkMode mode, string expected)
        => Assert.Equal(expected, NetworkModeCatalog.ConfigValue(mode));

    [Fact]
    public void ConfigValue_RoundTripsThroughParse()
    {
        foreach (var opt in NetworkModeCatalog.All)
            Assert.Equal(opt.Mode, NetworkModeCatalog.Parse(NetworkModeCatalog.ConfigValue(opt.Mode)));
    }

    [Fact]
    public void All_OffersNatMirroredVirtioProxyNone_NotDeprecatedBridged()
    {
        var modes = NetworkModeCatalog.All.Select(o => o.Mode).ToList();
        Assert.Contains(WslNetworkMode.Nat, modes);
        Assert.Contains(WslNetworkMode.Mirrored, modes);
        Assert.Contains(WslNetworkMode.VirtioProxy, modes);
        Assert.Contains(WslNetworkMode.None, modes);
    }

    [Fact]
    public void VirtioProxy_DisplayName_MentionsConsomme()
    {
        var opt = NetworkModeCatalog.All.Single(o => o.Mode == WslNetworkMode.VirtioProxy);
        Assert.Contains("Consomme", opt.DisplayName, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(WslNetworkMode.Mirrored, true)]
    [InlineData(WslNetworkMode.Nat, false)]
    [InlineData(WslNetworkMode.None, false)]
    public void Win11Gating_OnlyMirroredRequires22H2(WslNetworkMode mode, bool requires)
        => Assert.Equal(requires, NetworkModeCatalog.All.Single(o => o.Mode == mode).RequiresWin11_22H2);

    [Fact]
    public void WarningsFor_Mirrored_FlagsLocalhostForwardingPortProxyAndWin11()
    {
        var w = NetworkModeCatalog.WarningsFor(WslNetworkMode.Mirrored, anyDistroRunning: false, hasPortForwards: true);
        var joined = string.Join(" | ", w).ToLowerInvariant();
        Assert.Contains("localhostforwarding", joined);
        Assert.Contains("port", joined);            // port-proxy rules ignored
        Assert.Contains("22h2", joined);            // Win11 22H2 requirement
    }

    [Fact]
    public void WarningsFor_Mirrored_NoPortForwards_OmitsPortProxyWarning()
    {
        var w = NetworkModeCatalog.WarningsFor(WslNetworkMode.Mirrored, anyDistroRunning: false, hasPortForwards: false);
        Assert.DoesNotContain(w, x => x.ToLowerInvariant().Contains("port-proxy"));
    }

    [Fact]
    public void WarningsFor_None_WithPortForwards_WarnsRulesStopWorking()
    {
        var w = NetworkModeCatalog.WarningsFor(WslNetworkMode.None, anyDistroRunning: false, hasPortForwards: true);
        var joined = string.Join(" | ", w).ToLowerInvariant();
        Assert.Contains("disabled", joined);
        Assert.Contains("port-proxy", joined);
    }

    [Fact]
    public void ConfigValue_UnmappedEnum_FallsBackToNat()
        => Assert.Equal("nat", NetworkModeCatalog.ConfigValue((WslNetworkMode)99));

    [Fact]
    public void WarningsFor_AnyMode_WhenDistroRunning_WarnsShutdownStopsDistros()
    {
        var w = NetworkModeCatalog.WarningsFor(WslNetworkMode.Nat, anyDistroRunning: true, hasPortForwards: false);
        Assert.Contains(w, x => x.ToLowerInvariant().Contains("shut down")
                             || x.ToLowerInvariant().Contains("shutdown")
                             || x.ToLowerInvariant().Contains("stop"));
    }

    [Fact]
    public void WarningsFor_NotRunning_NoShutdownWarning()
    {
        var w = NetworkModeCatalog.WarningsFor(WslNetworkMode.Nat, anyDistroRunning: false, hasPortForwards: false);
        Assert.DoesNotContain(w, x => x.ToLowerInvariant().Contains("running distros"));
    }
}
