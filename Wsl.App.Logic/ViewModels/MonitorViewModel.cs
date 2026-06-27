using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;
using Wsl.Core.Monitoring;

namespace Wsl.App.Logic.ViewModels;

public partial class MonitorViewModel : ObservableObject
{
    private readonly WslMonitorService _monitor;
    private readonly WslDistroService _distros;

    public MonitorViewModel(WslMonitorService monitor, WslDistroService distros)
    { _monitor = monitor; _distros = distros; }

    public ObservableCollection<DistroMetrics> Rows { get; } = new();
    [ObservableProperty] private VmMetrics? _vm;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        await Guarded(async () =>
        {
            var all = await _distros.ListAsync();
            var running = all.Where(d => d.State == DistroState.Running).Select(d => d.Name).ToList();
            var snap = await _monitor.SampleAsync(running);
            Vm = snap.Vm;
            Rows.Clear();
            foreach (var r in snap.Distros) Rows.Add(r);
        });
    }

    [RelayCommand]
    public Task TerminateAsync(string name) => Guarded(() => _distros.TerminateAsync(name));

    [RelayCommand]
    public Task RestartVmAsync() => Guarded(async () =>
    {
        await _distros.ShutdownAsync();
    });

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (System.Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
