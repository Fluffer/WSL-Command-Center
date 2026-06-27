namespace Wsl.Core.Diagnostics;

public record PortForward(string ListenAddress, int ListenPort, string ConnectAddress, int ConnectPort);
public record NetworkInfo(string DistroIp, string HostGatewayIp,
    IReadOnlyList<string> DnsServers, IReadOnlyList<PortForward> PortForwards);
