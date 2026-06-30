using System.IO;
using System.Linq;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Diagnostics;
using Wsl.Core.Ipc;
using Wsl.Contracts;
using Xunit;

namespace Wsl.Core.Tests;

public class NetworkViewModelTests
{
    private static string TempCfgPath() =>
        Path.Combine(Path.GetTempPath(), "wslcfg_" + System.Guid.NewGuid().ToString("N") + ".wslconfig");

    [Fact]
    public async Task RefreshAsync_LoadsNetworkForSelectedDistro()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // ListAsync
        runner.Enqueue(0, "172.20.0.2\n");                  // hostname -I
        runner.Enqueue(0, "default via 172.20.0.1 dev eth0\n");
        runner.Enqueue(0, "nameserver 10.255.255.254\n");
        runner.Enqueue(0, "Address Port Address Port\n");   // netsh empty
        var cfgPath = TempCfgPath();
        var vm = new NetworkViewModel(
            new WslNetworkService(runner), new WslGpuService(runner),
            new WslDistroService(runner), new FakeBroker(),
            new WslConfigService(runner, () => cfgPath));

        await vm.RefreshAsync();

        Assert.Equal("Ubuntu", vm.SelectedDistro);
        Assert.Equal("172.20.0.2", vm.Network!.DistroIp);
        Assert.Equal(WslNetworkMode.Nat, vm.SelectedMode!.Mode);   // absent .wslconfig => NAT
    }

    [Fact]
    public async Task LoadNetworkModeAsync_ReadsExistingTokenAndShowsRawLabel()
    {
        var cfgPath = TempCfgPath();
        File.WriteAllText(cfgPath, "[wsl2]\nnetworkingMode=virtioproxy\n");
        try
        {
            var runner = new FakeProcessRunner();
            var vm = NewVm(runner, cfgPath);

            await vm.LoadNetworkModeAsync();

            Assert.Equal(WslNetworkMode.VirtioProxy, vm.SelectedMode!.Mode);
            Assert.Equal("virtioproxy", vm.CurrentModeLabel);
        }
        finally { File.Delete(cfgPath); }
    }

    [Fact]
    public async Task LoadNetworkModeAsync_DeprecatedBridged_ShownRaw_SelectedNat()
    {
        var cfgPath = TempCfgPath();
        File.WriteAllText(cfgPath, "[wsl2]\nnetworkingMode=bridged\n");
        try
        {
            var vm = NewVm(new FakeProcessRunner(), cfgPath);
            await vm.LoadNetworkModeAsync();
            Assert.Equal("bridged", vm.CurrentModeLabel);          // raw token preserved in UI
            Assert.Equal(WslNetworkMode.Nat, vm.SelectedMode!.Mode); // parse buckets it to NAT
        }
        finally { File.Delete(cfgPath); }
    }

    [Fact]
    public async Task ApplyNetworkModeAsync_WritesTokenThenShutsDown()
    {
        var cfgPath = TempCfgPath();
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(0, "");   // wsl --shutdown
            var vm = NewVm(runner, cfgPath);

            await vm.ApplyNetworkModeAsync(WslNetworkMode.Mirrored);

            var written = File.ReadAllText(cfgPath);
            Assert.Contains("networkingMode", written);
            Assert.Contains("mirrored", written);
            Assert.Contains("--shutdown", runner.AllArgs.Last());
            Assert.Equal(WslNetworkMode.Mirrored, vm.SelectedMode!.Mode);
            Assert.Null(vm.ErrorMessage);                       // success must not light the error bar
            Assert.Contains("mirrored", vm.StatusMessage!);     // success surfaced via StatusMessage
        }
        finally { if (File.Exists(cfgPath)) File.Delete(cfgPath); }
    }

    [Fact]
    public void WarningsForMode_UsesCurrentStateForRunningAndPortForwards()
    {
        var vm = NewVm(new FakeProcessRunner(), TempCfgPath());
        vm.Distros.Add("Ubuntu");
        vm.PortForwards.Add(new PortForward("0.0.0.0", 8080, "172.20.0.2", 80));

        var w = vm.WarningsForMode(WslNetworkMode.Mirrored);
        var joined = string.Join(" | ", w).ToLowerInvariant();

        Assert.Contains("running distros", joined);  // anyDistroRunning path
        Assert.Contains("port-proxy", joined);       // hasPortForwards path
    }

    private static NetworkViewModel NewVm(FakeProcessRunner runner, string cfgPath) =>
        new(new WslNetworkService(runner), new WslGpuService(runner),
            new WslDistroService(runner), new FakeBroker(),
            new WslConfigService(runner, () => cfgPath));

    private sealed class FakeBroker : IBrokerClient
    {
        public Task<BrokerResponse> SendAsync(BrokerRequest r, CancellationToken ct = default)
            => Task.FromResult(new BrokerResponse(true));
    }
}
