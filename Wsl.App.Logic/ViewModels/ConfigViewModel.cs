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

    // Global fields (string for direct TextBox binding)
    [ObservableProperty] private string? _memory;
    [ObservableProperty] private string? _processors;
    [ObservableProperty] private string? _networking;
    [ObservableProperty] private bool _localhostForwarding;

    // Per-distro
    [ObservableProperty] private string? _selectedDistro;
    [ObservableProperty] private string? _defaultUser;
    [ObservableProperty] private bool _systemd;
    [ObservableProperty] private string? _hostname;

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
        });
    }

    [RelayCommand]
    public async Task SaveGlobalAsync()
    {
        await Guarded(async () =>
        {
            _global.Memory = string.IsNullOrWhiteSpace(Memory) ? null : Memory;
            _global.Processors = int.TryParse(Processors, out var p) ? p : null;
            _global.Networking = string.IsNullOrWhiteSpace(Networking) ? null : Networking;
            _global.LocalhostForwarding = LocalhostForwarding;
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
        });
    }

    [RelayCommand]
    public async Task SaveDistroAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDistro)) { ErrorMessage = "Pick a distro."; return; }
        await Guarded(async () =>
        {
            _distro.DefaultUser = string.IsNullOrWhiteSpace(DefaultUser) ? null : DefaultUser;
            _distro.Systemd = Systemd;
            _distro.Hostname = string.IsNullOrWhiteSpace(Hostname) ? null : Hostname;
            await _config.WriteDistroAsync(SelectedDistro!, _distro);
            StatusMessage = $"Saved wsl.conf for {SelectedDistro}. Run `wsl --shutdown` to apply.";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        catch (IOException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
