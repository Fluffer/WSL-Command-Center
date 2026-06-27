using Wsl.Broker;
using Wsl.Contracts;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class PortProxyBrokerTests
{
    [Fact]
    public async Task DeletePortProxy_BuildsNetshDeleteArgs()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = new PrivilegedOperations(runner);
        var resp = await ops.HandleAsync(new DeletePortProxyRequest("0.0.0.0", 8080));
        Assert.True(resp.Success);
        Assert.Equal("netsh.exe", runner.LastExe);
        Assert.Contains("delete", runner.LastArgs!);
        Assert.Contains("v4tov4", runner.LastArgs!);
        Assert.Contains("listenport=8080", runner.LastArgs!);
        Assert.Contains("listenaddress=0.0.0.0", runner.LastArgs!);
    }
}
