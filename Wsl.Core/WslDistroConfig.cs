namespace Wsl.Core;

public class WslDistroConfig
{
    public string? DefaultUser { get; set; }     // [user] default
    public bool? Systemd { get; set; }            // [boot] systemd
    public bool? AutomountEnabled { get; set; }   // [automount] enabled
    public string? Hostname { get; set; }         // [network] hostname

    public Dictionary<string, Dictionary<string, string>> Passthrough { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public static WslDistroConfig FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var cfg = new WslDistroConfig();
        foreach (var (section, kv) in ini)
        {
            foreach (var (key, value) in kv)
            {
                var matched = (section.ToLowerInvariant(), key.ToLowerInvariant()) switch
                {
                    ("user", "default") => Assign(() => cfg.DefaultUser = value),
                    ("boot", "systemd") => Assign(() => cfg.Systemd = Bool(value)),
                    ("automount", "enabled") => Assign(() => cfg.AutomountEnabled = Bool(value)),
                    ("network", "hostname") => Assign(() => cfg.Hostname = value),
                    _ => false
                };
                if (!matched)
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
        var ini = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, kv) in Passthrough)
            ini[section] = new Dictionary<string, string>(kv, StringComparer.OrdinalIgnoreCase);

        Put(ini, "user", "default", DefaultUser);
        Put(ini, "boot", "systemd", Systemd?.ToString().ToLowerInvariant());
        Put(ini, "automount", "enabled", AutomountEnabled?.ToString().ToLowerInvariant());
        Put(ini, "network", "hostname", Hostname);
        return ini;
    }

    private static bool Assign(Action a) { a(); return true; }
    private static bool? Bool(string v) => bool.TryParse(v, out var b) ? b : null;

    private static void Put(Dictionary<string, Dictionary<string, string>> ini,
                            string section, string key, string? value)
    {
        if (value is null) return;
        if (!ini.TryGetValue(section, out var kv))
            ini[section] = kv = new(StringComparer.OrdinalIgnoreCase);
        kv[key] = value;
    }
}
