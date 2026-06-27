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
    [Fact]
    public async Task RefreshAsync_LoadsNetworkForSelectedDistro()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // ListAsync
        runner.Enqueue(0, "172.20.0.2\n");                  // hostname -I
        runner.Enqueue(0, "default via 172.20.0.1 dev eth0\n");
        runner.Enqueue(0, "nameserver 10.255.255.254\n");
        runner.Enqueue(0, "Address Port Address Port\n");   // netsh empty
        var vm = new NetworkViewModel(
            new WslNetworkService(runner), new WslGpuService(runner),
            new WslDistroService(runner), new FakeBroker());

        await vm.RefreshAsync();

        Assert.Equal("Ubuntu", vm.SelectedDistro);
        Assert.Equal("172.20.0.2", vm.Network!.DistroIp);
    }

    private sealed class FakeBroker : IBrokerClient
    {
        public Task<BrokerResponse> SendAsync(BrokerRequest r, CancellationToken ct = default)
            => Task.FromResult(new BrokerResponse(true));
    }
}
