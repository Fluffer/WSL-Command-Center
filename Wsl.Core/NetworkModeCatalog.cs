namespace Wsl.Core;

/// <summary>
/// The .wslconfig [wsl2] networkingMode values exposed by the switcher.
/// Per MS docs the file accepts none/nat/bridged(deprecated)/mirrored/virtioproxy.
/// "Consomme" is the display rename of the VirtioProxy backend; the config token stays "virtioproxy".
/// "bridged" is deprecated (WSL 2.4.5+) so it is not offered as a choice.
/// </summary>
public enum WslNetworkMode { Nat, Mirrored, VirtioProxy, None }

public sealed record NetworkModeOption(
    WslNetworkMode Mode,
    string ConfigValue,
    string DisplayName,
    string Description,
    bool RequiresWin11_22H2);

public static class NetworkModeCatalog
{
    public static IReadOnlyList<NetworkModeOption> All { get; } = Array.AsReadOnly(new[]
    {
        new NetworkModeOption(WslNetworkMode.Nat, "nat", "NAT (default)",
            "WSL's default NAT-based networking. Use localhostForwarding + port-proxy rules to reach Linux services.",
            RequiresWin11_22H2: false),
        new NetworkModeOption(WslNetworkMode.Mirrored, "mirrored", "Mirrored",
            "Mirrors the Windows network interfaces into Linux for better compatibility (IPv6, LAN access, localhost both ways).",
            RequiresWin11_22H2: true),
        new NetworkModeOption(WslNetworkMode.VirtioProxy, "virtioproxy", "VirtioProxy (Consomme)",
            "Virtualized proxy networking (display name 'Consomme'). WSL falls back to this automatically when NAT fails (since WSL 2.3.25).",
            RequiresWin11_22H2: false),
        new NetworkModeOption(WslNetworkMode.None, "none", "None (disconnected)",
            "Disables WSL networking entirely. The VM has no network access.",
            RequiresWin11_22H2: false),
    });

    private static readonly IReadOnlyDictionary<WslNetworkMode, string> ConfigValues =
        All.ToDictionary(o => o.Mode, o => o.ConfigValue);

    /// <summary>Maps a raw .wslconfig value to a mode. Null/blank/unknown => NAT (matches WSL's own fallback).</summary>
    public static WslNetworkMode Parse(string? configValue)
    {
        if (string.IsNullOrWhiteSpace(configValue)) return WslNetworkMode.Nat;
        return configValue.Trim().ToLowerInvariant() switch
        {
            "mirrored" => WslNetworkMode.Mirrored,
            "virtioproxy" => WslNetworkMode.VirtioProxy,
            "none" => WslNetworkMode.None,
            // "nat", "bridged" (deprecated), and any unknown token all resolve to NAT.
            _ => WslNetworkMode.Nat,
        };
    }

    public static string ConfigValue(WslNetworkMode mode) =>
        ConfigValues.TryGetValue(mode, out var v) ? v : "nat";

    /// <summary>
    /// Static, non-fragile consequence warnings shown in the confirm dialog before applying a mode.
    /// Deliberately not a conflict analyzer — just the documented gotchas for the target mode.
    /// </summary>
    public static IReadOnlyList<string> WarningsFor(WslNetworkMode mode, bool anyDistroRunning, bool hasPortForwards)
    {
        var w = new List<string>();

        if (mode == WslNetworkMode.Mirrored)
        {
            w.Add("Requires Windows 11 22H2 or later. On older builds WSL falls back to NAT.");
            w.Add("localhostForwarding is ignored in mirrored mode — localhost is bridged both ways automatically.");
            if (hasPortForwards)
                w.Add("Existing netsh port-proxy rules become redundant in mirrored mode and may shadow Linux binds; review them after switching.");
        }
        else if (mode == WslNetworkMode.None)
        {
            w.Add("Networking will be fully disabled for every WSL 2 distro until you switch back.");
            if (hasPortForwards)
                w.Add("Existing port-proxy rules will stop working — the Linux side has no network to forward to.");
        }
        else if (mode == WslNetworkMode.VirtioProxy)
        {
            w.Add("VirtioProxy (Consomme) is primarily NAT's automatic fallback; pin it only if you specifically need it.");
        }

        if (anyDistroRunning)
            w.Add("Applying requires `wsl --shutdown`, which will stop all running distros. Save work first.");
        else
            w.Add("Change applies on next WSL launch (a `wsl --shutdown` is run to be sure).");

        return w;
    }
}
