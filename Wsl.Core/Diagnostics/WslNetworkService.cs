namespace Wsl.Core.Diagnostics;

public class WslNetworkService
{
    private readonly IProcessRunner _runner;
    public WslNetworkService(IProcessRunner runner) => _runner = runner;

    public async Task<NetworkInfo> ReadAsync(string distro, CancellationToken ct = default)
    {
        var hostnameOut = (await _runner.RunAsync("wsl.exe",
            new[] { "-d", distro, "--", "hostname", "-I" }, null, ct)).StdOut;
        var ip = (hostnameOut ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var route = await _runner.RunAsync("wsl.exe",
            new[] { "-d", distro, "--", "ip", "route", "show" }, null, ct);
        var resolv = await _runner.RunAsync("wsl.exe",
            new[] { "-d", distro, "--", "cat", "/etc/resolv.conf" }, null, ct);
        var netsh = await _runner.RunAsync("netsh.exe",
            new[] { "interface", "portproxy", "show", "all" }, null, ct);

        var dns = resolv.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith("nameserver"))
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(t => t.Length >= 2)
            .Select(t => { var s = t[1]; var h = s.IndexOf('#'); return h >= 0 ? s[..h] : s; })
            .ToList();

        return new NetworkInfo(ip, ParseGateway(route.StdOut), dns, ParsePortProxy(netsh.StdOut));
    }

    public static string ParseGateway(string ipRoute)
    {
        foreach (var line in ipRoute.Split('\n'))
        {
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var i = Array.IndexOf(t, "via");
            if (line.TrimStart().StartsWith("default") && i >= 0 && i + 1 < t.Length) return t[i + 1];
        }
        return "";
    }

    public static IReadOnlyList<PortForward> ParsePortProxy(string netshOutput)
    {
        var list = new List<PortForward>();
        foreach (var line in netshOutput.Split('\n'))
        {
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 4 && IsV4(t[0]) && IsV4(t[2])
                && int.TryParse(t[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lp)
                && int.TryParse(t[3], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var cp))
                list.Add(new PortForward(t[0], lp, t[2], cp));
        }
        return list;
    }

    private static bool IsV4(string s) =>
        System.Net.IPAddress.TryParse(s, out var ip) &&
        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
}
