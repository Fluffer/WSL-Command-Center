namespace Wsl.Core;

public class WslGlobalConfig
{
    private const string Section = "wsl2";
    private const string ExperimentalSection = "experimental";

    public string? Memory { get; set; }
    public int? Processors { get; set; }
    public string? Swap { get; set; }
    public string? SwapFile { get; set; }
    public string? Networking { get; set; }
    public bool? LocalhostForwarding { get; set; }
    public bool? NestedVirtualization { get; set; }

    // [wsl2] additions
    public bool? GuiApplications { get; set; }
    public int? VmIdleTimeout { get; set; }
    public string? DefaultVhdSize { get; set; }
    public bool? Firewall { get; set; }
    public bool? DnsTunneling { get; set; }
    public bool? DnsProxy { get; set; }
    public bool? AutoProxy { get; set; }
    public string? KernelCommandLine { get; set; }
    public bool? SafeMode { get; set; }
    public bool? DebugConsole { get; set; }
    public int? MaxCrashDumpCount { get; set; }
    public string? Kernel { get; set; }
    public string? KernelModules { get; set; }

    // [experimental]
    public string? AutoMemoryReclaim { get; set; }
    public bool? SparseVhd { get; set; }
    public string? IgnoredPorts { get; set; }
    public bool? HostAddressLoopback { get; set; }

    /// <summary>section -> key -> value, for everything not modeled above.</summary>
    public Dictionary<string, Dictionary<string, string>> Passthrough { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Modeled = new(StringComparer.OrdinalIgnoreCase)
    {
        "memory", "processors", "swap", "swapFile",
        "networkingMode", "localhostForwarding", "nestedVirtualization",
        "guiApplications", "vmIdleTimeout", "defaultVhdSize", "firewall",
        "dnsTunneling", "dnsProxy", "autoProxy", "kernelCommandLine",
        "safeMode", "debugConsole", "maxCrashDumpCount", "kernel", "kernelModules"
    };

    private static readonly HashSet<string> ExperimentalModeled = new(StringComparer.OrdinalIgnoreCase)
    { "autoMemoryReclaim", "sparseVhd", "ignoredPorts", "hostAddressLoopback" };

    public static WslGlobalConfig FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var cfg = new WslGlobalConfig();
        foreach (var (section, kv) in ini)
        {
            foreach (var (key, value) in kv)
            {
                if (section.Equals(Section, StringComparison.OrdinalIgnoreCase) && Modeled.Contains(key))
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "memory": cfg.Memory = value; break;
                        case "processors": cfg.Processors = int.TryParse(value, out var p) ? p : null; break;
                        case "swap": cfg.Swap = value; break;
                        case "swapfile": cfg.SwapFile = value; break;
                        case "networkingmode": cfg.Networking = value; break;
                        case "localhostforwarding": cfg.LocalhostForwarding = ParseBool(value); break;
                        case "nestedvirtualization": cfg.NestedVirtualization = ParseBool(value); break;
                        case "guiapplications": cfg.GuiApplications = ParseBool(value); break;
                        case "vmidletimeout": cfg.VmIdleTimeout = int.TryParse(value, out var vi) ? vi : null; break;
                        case "defaultvhdsize": cfg.DefaultVhdSize = value; break;
                        case "firewall": cfg.Firewall = ParseBool(value); break;
                        case "dnstunneling": cfg.DnsTunneling = ParseBool(value); break;
                        case "dnsproxy": cfg.DnsProxy = ParseBool(value); break;
                        case "autoproxy": cfg.AutoProxy = ParseBool(value); break;
                        case "kernelcommandline": cfg.KernelCommandLine = value; break;
                        case "safemode": cfg.SafeMode = ParseBool(value); break;
                        case "debugconsole": cfg.DebugConsole = ParseBool(value); break;
                        case "maxcrashdumpcount": cfg.MaxCrashDumpCount = int.TryParse(value, out var mc) ? mc : null; break;
                        case "kernel": cfg.Kernel = value; break;
                        case "kernelmodules": cfg.KernelModules = value; break;
                    }
                }
                else if (section.Equals(ExperimentalSection, StringComparison.OrdinalIgnoreCase) &&
                         ExperimentalModeled.Contains(key))
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "automemoryreclaim": cfg.AutoMemoryReclaim = value; break;
                        case "sparsevhd": cfg.SparseVhd = ParseBool(value); break;
                        case "ignoredports": cfg.IgnoredPorts = value; break;
                        case "hostaddressloopback": cfg.HostAddressLoopback = ParseBool(value); break;
                    }
                }
                else
                {
                    if (!cfg.Passthrough.TryGetValue(section, out var pk))
                        cfg.Passthrough[section] = pk = new(StringComparer.OrdinalIgnoreCase);
                    pk[key] = value;
                }
            }
        }
        return cfg;
    }

    public Dictionary<string, Dictionary<string, string>> ToIni()
    {
        // Start from passthrough so unknown keys/sections survive.
        var ini = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, kv) in Passthrough)
            ini[section] = new Dictionary<string, string>(kv, StringComparer.OrdinalIgnoreCase);

        if (!ini.TryGetValue(Section, out var wsl2))
            ini[Section] = wsl2 = new(StringComparer.OrdinalIgnoreCase);

        Set(wsl2, "memory", Memory);
        Set(wsl2, "processors", Processors?.ToString());
        Set(wsl2, "swap", Swap);
        Set(wsl2, "swapFile", SwapFile);
        Set(wsl2, "networkingMode", Networking);
        Set(wsl2, "localhostForwarding", LocalhostForwarding?.ToString().ToLowerInvariant());
        Set(wsl2, "nestedVirtualization", NestedVirtualization?.ToString().ToLowerInvariant());
        Set(wsl2, "guiApplications", GuiApplications?.ToString().ToLowerInvariant());
        Set(wsl2, "vmIdleTimeout", VmIdleTimeout?.ToString());
        Set(wsl2, "defaultVhdSize", DefaultVhdSize);
        Set(wsl2, "firewall", Firewall?.ToString().ToLowerInvariant());
        Set(wsl2, "dnsTunneling", DnsTunneling?.ToString().ToLowerInvariant());
        Set(wsl2, "dnsProxy", DnsProxy?.ToString().ToLowerInvariant());
        Set(wsl2, "autoProxy", AutoProxy?.ToString().ToLowerInvariant());
        Set(wsl2, "kernelCommandLine", KernelCommandLine);
        Set(wsl2, "safeMode", SafeMode?.ToString().ToLowerInvariant());
        Set(wsl2, "debugConsole", DebugConsole?.ToString().ToLowerInvariant());
        Set(wsl2, "maxCrashDumpCount", MaxCrashDumpCount?.ToString());
        Set(wsl2, "kernel", Kernel);
        Set(wsl2, "kernelModules", KernelModules);

        if (!ini.TryGetValue(ExperimentalSection, out var exp))
            ini[ExperimentalSection] = exp = new(StringComparer.OrdinalIgnoreCase);
        Set(exp, "autoMemoryReclaim", AutoMemoryReclaim);
        Set(exp, "sparseVhd", SparseVhd?.ToString().ToLowerInvariant());
        Set(exp, "ignoredPorts", IgnoredPorts);
        Set(exp, "hostAddressLoopback", HostAddressLoopback?.ToString().ToLowerInvariant());
        return ini;
    }

    private static void Set(Dictionary<string, string> kv, string key, string? value)
    {
        if (value is null) kv.Remove(key);
        else kv[key] = value;
    }

    private static bool? ParseBool(string v) =>
        bool.TryParse(v, out var b) ? b : v.Trim() == "1" ? true : v.Trim() == "0" ? false : null;
}
