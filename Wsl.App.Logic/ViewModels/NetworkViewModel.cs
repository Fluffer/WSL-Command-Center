using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Diagnostics;
using Wsl.Core.Ipc;

namespace Wsl.App.Logic.ViewModels;

public partial class NetworkViewModel : ObservableObject
{
    private readonly WslNetworkService _net;
    private readonly WslGpuService _gpu;
    private readonly WslDistroService _distros;
    private readonly IBrokerClient _broker;
    private readonly WslConfigService _config;

    public NetworkViewModel(WslNetworkService net, WslGpuService gpu,
        WslDistroService distros, IBrokerClient broker, WslConfigService config)
    { _net = net; _gpu = gpu; _distros = distros; _broker = broker; _config = config; }

    public ObservableCollection<string> Distros { get; } = new();
    public ObservableCollection<PortForward> PortForwards { get; } = new();
    [ObservableProperty] private string? _selectedDistro;
    [ObservableProperty] private NetworkInfo? _network;
    [ObservableProperty] private GpuInfo? _gpuInfo;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    /// <summary>Non-error status (e.g. "mode applied"). Kept separate from <see cref="ErrorMessage"/>
    /// so a success message never lights up the error InfoBar.</summary>
    [ObservableProperty] private string? _statusMessage;

    // F1: networking-mode switcher.
    public ObservableCollection<NetworkModeOption> NetworkModes { get; } = new(NetworkModeCatalog.All);
    [ObservableProperty] private NetworkModeOption? _selectedMode;
    /// <summary>Raw networkingMode token from .wslconfig, surfaced verbatim so deprecated/unknown
    /// values (e.g. "bridged") stay visible instead of being silently shown as NAT.</summary>
    [ObservableProperty] private string? _currentModeLabel;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await Guarded(async () =>
        {
            var running = (await _distros.ListAsync())
                .Where(d => d.State == DistroState.Running).Select(d => d.Name).ToList();
            Distros.Clear();
            foreach (var d in running) Distros.Add(d);
            SelectedDistro ??= running.FirstOrDefault();
            if (SelectedDistro is null) { Network = null; return; }

            Network = await _net.ReadAsync(SelectedDistro);
            PortForwards.Clear();
            foreach (var f in Network.PortForwards) PortForwards.Add(f);
            GpuInfo = await _gpu.ProbeAsync(SelectedDistro);
            await LoadNetworkModeAsync();
        });
    }

    /// <summary>Reads the current networkingMode from .wslconfig (file read, no wsl.exe call).</summary>
    public async Task LoadNetworkModeAsync(CancellationToken ct = default)
    {
        var cfg = await _config.ReadGlobalAsync(ct);
        var raw = cfg.Networking;
        CurrentModeLabel = string.IsNullOrWhiteSpace(raw) ? "nat (default — unset)" : raw;
        var mode = NetworkModeCatalog.Parse(raw);
        SelectedMode = NetworkModes.FirstOrDefault(o => o.Mode == mode);
    }

    /// <summary>Confirm-dialog consequences for switching to <paramref name="mode"/>, using current state.</summary>
    public IReadOnlyList<string> WarningsForMode(WslNetworkMode mode)
        => NetworkModeCatalog.WarningsFor(mode, anyDistroRunning: Distros.Count > 0, hasPortForwards: PortForwards.Count > 0);

    /// <summary>Writes the new mode to .wslconfig then `wsl --shutdown` so the next launch picks it up.</summary>
    [RelayCommand]
    public Task ApplyNetworkModeAsync(WslNetworkMode mode) => Guarded(async () =>
    {
        var token = NetworkModeCatalog.ConfigValue(mode);
        var cfg = await _config.ReadGlobalAsync();
        cfg.Networking = token;
        await _config.WriteGlobalAsync(cfg);   // durable part: the mode is now persisted to .wslconfig
        try
        {
            await _distros.ShutdownAsync();    // …then shut the VM so the new mode applies on next start
        }
        catch (System.Exception ex)
        {
            // Config is already written; the switch will apply on the next manual shutdown.
            await LoadNetworkModeAsync();
            StatusMessage = $"Networking mode set to '{token}', but `wsl --shutdown` failed ({ex.Message}). Run it manually to apply.";
            return;
        }
        await LoadNetworkModeAsync();
        Distros.Clear();                       // all distros were just stopped; require an explicit Refresh
        StatusMessage = $"Networking mode set to '{token}'. WSL was shut down — press Refresh to reconnect.";
    });

    [RelayCommand]
    public Task ProbeGpuAsync() => SelectedDistro is null ? Task.CompletedTask
        : Guarded(async () => GpuInfo = await _gpu.ProbeAsync(SelectedDistro));

    [RelayCommand]
    public Task RestartNetworkingAsync() => Guarded(() => _distros.ShutdownAsync());

    [RelayCommand]
    public Task DeletePortForwardAsync(PortForward f) => Guarded(async () =>
    {
        var resp = await _broker.SendAsync(new DeletePortProxyRequest(f.ListenAddress, f.ListenPort));
        if (!resp.Success) { ErrorMessage = resp.Error; return; }
        PortForwards.Remove(f);
    });

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (System.Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
