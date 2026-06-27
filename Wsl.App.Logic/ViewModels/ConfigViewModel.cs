using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly WslConfigService _config;
    private readonly WslDistroService _distros;
    private WslGlobalConfig _global = new();
    private WslDistroConfig _distro = new();

    public ConfigViewModel(WslConfigService config, WslDistroService distros)
    {
        _config = config;
        _distros = distros;
    }

    public ObservableCollection<string> Distros { get; } = new();

    /// <summary>Valid [wsl2] networkingMode values; editable combo still allows a custom one.</summary>
    public string[] NetworkingModes { get; } = { "NAT", "mirrored" };

    /// <summary>Valid [experimental] autoMemoryReclaim values.</summary>
    public string[] AutoMemoryReclaimModes { get; } = { "disabled", "gradual", "dropCache" };

    // Hints showing what WSL2 falls back to when these fields are left empty,
    // computed from the host's actual hardware.
    public string MemoryDefaultHint { get; } = BuildMemoryHint();
    public string ProcessorsDefaultHint { get; } =
        $"Empty = WSL2 uses all {SystemInfo.LogicalProcessors} logical processors.";
    public string SwapDefaultHint { get; } =
        "Empty = WSL2 swap defaults to 25% of its memory limit.";

    private static string BuildMemoryHint()
    {
        var total = SystemInfo.TotalPhysicalGiB();
        if (total <= 0)
            return "Empty = WSL2 reserves 50% of total RAM. Set a value (e.g. 8GB) to cap it.";
        var half = Math.Round(total / 2, 1);
        return $"Empty = WSL2 reserves 50% of RAM ≈ {half} GB of {total} GB. Set a value (e.g. 8GB) to cap it.";
    }

    [RelayCommand]
    public async Task LoadDistrosAsync()
    {
        await Guarded(async () =>
        {
            var list = await _distros.ListAsync();
            Distros.Clear();
            foreach (var d in list)
                Distros.Add(d.Name);
        });
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // ── Global fields ── [wsl2] ──────────────────────────────────────────────
    [ObservableProperty] private string? _memory;
    [ObservableProperty] private string? _processors;
    [ObservableProperty] private string? _networking;
    [ObservableProperty] private bool _localhostForwarding;

    // Tier 1 global ([wsl2])
    [ObservableProperty] private bool? _guiApplications;
    [ObservableProperty] private string? _vmIdleTimeout;
    [ObservableProperty] private string? _defaultVhdSize;
    [ObservableProperty] private bool? _firewall;
    [ObservableProperty] private bool? _dnsTunneling;
    [ObservableProperty] private bool? _dnsProxy;
    [ObservableProperty] private bool? _autoProxy;
    [ObservableProperty] private string? _kernelCommandLine;
    [ObservableProperty] private bool? _safeMode;
    [ObservableProperty] private bool? _debugConsole;
    [ObservableProperty] private string? _maxCrashDumpCount;
    [ObservableProperty] private string? _kernel;
    [ObservableProperty] private string? _kernelModules;

    // Tier 1 experimental ([experimental])
    [ObservableProperty] private string? _autoMemoryReclaim;
    [ObservableProperty] private bool? _sparseVhd;
    [ObservableProperty] private string? _ignoredPorts;
    [ObservableProperty] private bool? _hostAddressLoopback;

    // ── Per-distro fields ────────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedDistro;
    [ObservableProperty] private string? _defaultUser;
    [ObservableProperty] private bool _systemd;
    [ObservableProperty] private string? _hostname;

    // Tier 2 distro
    [ObservableProperty] private bool? _mountFsTab;
    [ObservableProperty] private string? _automountRoot;
    [ObservableProperty] private string? _automountOptions;
    [ObservableProperty] private bool? _interopEnabled;
    [ObservableProperty] private bool? _appendWindowsPath;
    [ObservableProperty] private bool? _generateHosts;
    [ObservableProperty] private bool? _generateResolvConf;
    [ObservableProperty] private string? _dns;
    [ObservableProperty] private string? _bootCommand;
    [ObservableProperty] private bool? _protectBinfmt;
    [ObservableProperty] private bool? _gpuEnabled;
    [ObservableProperty] private bool? _useWindowsTimezone;

    [RelayCommand]
    public async Task LoadGlobalAsync()
    {
        await Guarded(async () =>
        {
            _global = await _config.ReadGlobalAsync();
            Memory = _global.Memory;
            Processors = _global.Processors?.ToString();
            Networking = _global.Networking;
            LocalhostForwarding = _global.LocalhostForwarding ?? false;

            GuiApplications = _global.GuiApplications;
            VmIdleTimeout = _global.VmIdleTimeout?.ToString();
            DefaultVhdSize = _global.DefaultVhdSize;
            Firewall = _global.Firewall;
            DnsTunneling = _global.DnsTunneling;
            DnsProxy = _global.DnsProxy;
            AutoProxy = _global.AutoProxy;
            KernelCommandLine = _global.KernelCommandLine;
            SafeMode = _global.SafeMode;
            DebugConsole = _global.DebugConsole;
            MaxCrashDumpCount = _global.MaxCrashDumpCount?.ToString();
            Kernel = _global.Kernel;
            KernelModules = _global.KernelModules;
            AutoMemoryReclaim = _global.AutoMemoryReclaim;
            SparseVhd = _global.SparseVhd;
            IgnoredPorts = _global.IgnoredPorts;
            HostAddressLoopback = _global.HostAddressLoopback;
        });
    }

    [RelayCommand]
    public async Task SaveGlobalAsync()
    {
        await Guarded(async () =>
        {
            _global.Memory = NullIfBlank(Memory);
            _global.Processors = int.TryParse(Processors, out var p) ? p : null;
            _global.Networking = NullIfBlank(Networking);
            _global.LocalhostForwarding = LocalhostForwarding;

            _global.GuiApplications = GuiApplications;
            _global.VmIdleTimeout = int.TryParse(VmIdleTimeout, out var vit) ? vit : null;
            _global.DefaultVhdSize = NullIfBlank(DefaultVhdSize);
            _global.Firewall = Firewall;
            _global.DnsTunneling = DnsTunneling;
            _global.DnsProxy = DnsProxy;
            _global.AutoProxy = AutoProxy;
            _global.KernelCommandLine = NullIfBlank(KernelCommandLine);
            _global.SafeMode = SafeMode;
            _global.DebugConsole = DebugConsole;
            _global.MaxCrashDumpCount = int.TryParse(MaxCrashDumpCount, out var mcd) ? mcd : null;
            _global.Kernel = NullIfBlank(Kernel);
            _global.KernelModules = NullIfBlank(KernelModules);
            _global.AutoMemoryReclaim = NullIfBlank(AutoMemoryReclaim);
            _global.SparseVhd = SparseVhd;
            _global.IgnoredPorts = NullIfBlank(IgnoredPorts);
            _global.HostAddressLoopback = HostAddressLoopback;

            await _config.WriteGlobalAsync(_global);
            StatusMessage = "Saved .wslconfig. Run `wsl --shutdown` to apply.";
        });
    }

    [RelayCommand]
    public async Task LoadDistroAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDistro)) { ErrorMessage = "Pick a distro."; return; }
        await Guarded(async () =>
        {
            _distro = await _config.ReadDistroAsync(SelectedDistro!);
            DefaultUser = _distro.DefaultUser;
            Systemd = _distro.Systemd ?? false;
            Hostname = _distro.Hostname;

            MountFsTab = _distro.MountFsTab;
            AutomountRoot = _distro.AutomountRoot;
            AutomountOptions = _distro.AutomountOptions;
            InteropEnabled = _distro.InteropEnabled;
            AppendWindowsPath = _distro.AppendWindowsPath;
            GenerateHosts = _distro.GenerateHosts;
            GenerateResolvConf = _distro.GenerateResolvConf;
            Dns = _distro.Dns;
            BootCommand = _distro.BootCommand;
            ProtectBinfmt = _distro.ProtectBinfmt;
            GpuEnabled = _distro.GpuEnabled;
            UseWindowsTimezone = _distro.UseWindowsTimezone;
        });
    }

    [RelayCommand]
    public async Task SaveDistroAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDistro)) { ErrorMessage = "Pick a distro."; return; }
        await Guarded(async () =>
        {
            _distro.DefaultUser = NullIfBlank(DefaultUser);
            _distro.Systemd = Systemd;
            _distro.Hostname = NullIfBlank(Hostname);

            _distro.MountFsTab = MountFsTab;
            _distro.AutomountRoot = NullIfBlank(AutomountRoot);
            _distro.AutomountOptions = NullIfBlank(AutomountOptions);
            _distro.InteropEnabled = InteropEnabled;
            _distro.AppendWindowsPath = AppendWindowsPath;
            _distro.GenerateHosts = GenerateHosts;
            _distro.GenerateResolvConf = GenerateResolvConf;
            _distro.Dns = NullIfBlank(Dns);
            _distro.BootCommand = NullIfBlank(BootCommand);
            _distro.ProtectBinfmt = ProtectBinfmt;
            _distro.GpuEnabled = GpuEnabled;
            _distro.UseWindowsTimezone = UseWindowsTimezone;

            await _config.WriteDistroAsync(SelectedDistro!, _distro);
            StatusMessage = $"Saved wsl.conf for {SelectedDistro}. Run `wsl --shutdown` to apply.";
        });
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        catch (IOException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
