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

    public NetworkViewModel(WslNetworkService net, WslGpuService gpu,
        WslDistroService distros, IBrokerClient broker)
    { _net = net; _gpu = gpu; _distros = distros; _broker = broker; }

    public ObservableCollection<string> Distros { get; } = new();
    public ObservableCollection<PortForward> PortForwards { get; } = new();
    [ObservableProperty] private string? _selectedDistro;
    [ObservableProperty] private NetworkInfo? _network;
    [ObservableProperty] private GpuInfo? _gpuInfo;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

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
        });
    }

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
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (System.Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
