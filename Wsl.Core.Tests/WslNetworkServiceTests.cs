using Wsl.Core.Diagnostics;
using Xunit;

namespace Wsl.Core.Tests;

public class WslNetworkServiceTests
{
    [Fact]
    public void ParsePortProxy_ReadsV4Table()
    {
        const string netsh =
            "\nListen on ipv4:             Connect to ipv4:\n\n" +
            "Address         Port        Address         Port\n" +
            "--------------- ----------  --------------- ----------\n" +
            "0.0.0.0         8080        172.20.0.2      80\n" +
            "127.0.0.1       5432        172.20.0.3      5432\n";
        var fwds = WslNetworkService.ParsePortProxy(netsh);
        Assert.Equal(2, fwds.Count);
        Assert.Equal("0.0.0.0", fwds[0].ListenAddress);
        Assert.Equal(8080, fwds[0].ListenPort);
        Assert.Equal("172.20.0.2", fwds[0].ConnectAddress);
        Assert.Equal(80, fwds[0].ConnectPort);
        Assert.Equal("127.0.0.1", fwds[1].ListenAddress);
        Assert.Equal(5432, fwds[1].ListenPort);
    }

    [Fact]
    public void ParseGateway_ReadsDefaultRoute()
    {
        const string ipRoute = "default via 172.20.0.1 dev eth0 proto kernel\n10.0.0.0/24 dev eth0\n";
        Assert.Equal("172.20.0.1", WslNetworkService.ParseGateway(ipRoute));
    }

    [Fact]
    public void ParseGateway_ReturnsEmpty_WhenNoDefault()
    {
        Assert.Equal("", WslNetworkService.ParseGateway("10.0.0.0/24 via 192.168.1.1 dev eth0\n"));
    }

    [Fact]
    public void ParsePortProxy_SkipsIPv6Rows()
    {
        const string netsh =
            "Address         Port        Address         Port\n" +
            "::1             8080        ::2             80\n";
        var fwds = WslNetworkService.ParsePortProxy(netsh);
        Assert.Empty(fwds);
    }

    [Fact]
    public async Task ReadAsync_AggregatesAllSources()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "172.20.0.2 \n");                       // hostname -I
        runner.Enqueue(0, "default via 172.20.0.1 dev eth0\n");   // ip route
        runner.Enqueue(0, "nameserver 10.255.255.254\n");         // resolv.conf
        runner.Enqueue(0, "Address Port Address Port\n0.0.0.0 8080 172.20.0.2 80\n"); // netsh
        var info = await new WslNetworkService(runner).ReadAsync("Ubuntu");
        Assert.Equal("172.20.0.2", info.DistroIp);
        Assert.Equal("172.20.0.1", info.HostGatewayIp);
        Assert.Contains("10.255.255.254", info.DnsServers);
        Assert.Single(info.PortForwards);
        Assert.Equal(8080, info.PortForwards[0].ListenPort);
    }

    [Fact]
    public async Task ReadAsync_StripsPoundFromResolvNameserver()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "172.20.0.2\n");                        // hostname -I
        runner.Enqueue(0, "default via 172.20.0.1 dev eth0\n");   // ip route
        runner.Enqueue(0, "nameserver 1.1.1.1#x\n");              // resolv.conf with inline comment
        runner.Enqueue(0, "");                                     // netsh
        var info = await new WslNetworkService(runner).ReadAsync("Ubuntu");
        Assert.Single(info.DnsServers);
        Assert.Equal("1.1.1.1", info.DnsServers[0]);
    }
}
