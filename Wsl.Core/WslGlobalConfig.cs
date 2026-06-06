namespace Wsl.Core;

public class WslGlobalConfig
{
    private const string Section = "wsl2";

    public string? Memory { get; set; }
    public int? Processors { get; set; }
    public string? Swap { get; set; }
    public string? SwapFile { get; set; }
    public string? Networking { get; set; }
    public bool? LocalhostForwarding { get; set; }
    public bool? NestedVirtualization { get; set; }

    /// <summary>section -> key -> value, for everything not modeled above.</summary>
    public Dictionary<string, Dictionary<string, string>> Passthrough { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Modeled = new(StringComparer.OrdinalIgnoreCase)
    {
        "memory", "processors", "swap", "swapFile",
        "networkingMode", "localhostForwarding", "nestedVirtualization"
    };

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
