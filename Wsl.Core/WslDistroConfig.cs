namespace Wsl.Core;

public class WslDistroConfig
{
    public string? DefaultUser { get; set; }     // [user] default
    public bool? Systemd { get; set; }            // [boot] systemd
    public bool? AutomountEnabled { get; set; }   // [automount] enabled
    public string? Hostname { get; set; }         // [network] hostname
    public bool? MountFsTab { get; set; }          // [automount] mountFsTab
    public string? AutomountRoot { get; set; }     // [automount] root
    public string? AutomountOptions { get; set; }  // [automount] options
    public bool? InteropEnabled { get; set; }      // [interop] enabled
    public bool? AppendWindowsPath { get; set; }   // [interop] appendWindowsPath
    public bool? GenerateHosts { get; set; }       // [network] generateHosts
    public bool? GenerateResolvConf { get; set; }  // [network] generateResolvConf
    public string? Dns { get; set; }               // [network] dns
    public string? BootCommand { get; set; }       // [boot] command
    public bool? ProtectBinfmt { get; set; }       // [boot] protectBinfmt
    public bool? GpuEnabled { get; set; }          // [gpu] enabled
    public bool? UseWindowsTimezone { get; set; }  // [time] useWindowsTimezone

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
                    ("automount", "mountfstab") => Assign(() => cfg.MountFsTab = Bool(value)),
                    ("automount", "root") => Assign(() => cfg.AutomountRoot = value),
                    ("automount", "options") => Assign(() => cfg.AutomountOptions = value),
                    ("interop", "enabled") => Assign(() => cfg.InteropEnabled = Bool(value)),
                    ("interop", "appendwindowspath") => Assign(() => cfg.AppendWindowsPath = Bool(value)),
                    ("network", "generatehosts") => Assign(() => cfg.GenerateHosts = Bool(value)),
                    ("network", "generateresolvconf") => Assign(() => cfg.GenerateResolvConf = Bool(value)),
                    ("network", "dns") => Assign(() => cfg.Dns = value),
                    ("boot", "command") => Assign(() => cfg.BootCommand = value),
                    ("boot", "protectbinfmt") => Assign(() => cfg.ProtectBinfmt = Bool(value)),
                    ("gpu", "enabled") => Assign(() => cfg.GpuEnabled = Bool(value)),
                    ("time", "usewindowstimezone") => Assign(() => cfg.UseWindowsTimezone = Bool(value)),
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
        Put(ini, "automount", "mountFsTab", MountFsTab?.ToString().ToLowerInvariant());
        Put(ini, "automount", "root", AutomountRoot);
        Put(ini, "automount", "options", AutomountOptions);
        Put(ini, "interop", "enabled", InteropEnabled?.ToString().ToLowerInvariant());
        Put(ini, "interop", "appendWindowsPath", AppendWindowsPath?.ToString().ToLowerInvariant());
        Put(ini, "network", "generateHosts", GenerateHosts?.ToString().ToLowerInvariant());
        Put(ini, "network", "generateResolvConf", GenerateResolvConf?.ToString().ToLowerInvariant());
        Put(ini, "network", "dns", Dns);
        Put(ini, "boot", "command", BootCommand);
        Put(ini, "boot", "protectBinfmt", ProtectBinfmt?.ToString().ToLowerInvariant());
        Put(ini, "gpu", "enabled", GpuEnabled?.ToString().ToLowerInvariant());
        Put(ini, "time", "useWindowsTimezone", UseWindowsTimezone?.ToString().ToLowerInvariant());
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
