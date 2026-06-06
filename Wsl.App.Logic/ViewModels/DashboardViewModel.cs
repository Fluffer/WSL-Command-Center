using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly WslDistroService _distros;

    public DashboardViewModel(WslDistroService distros) => _distros = distros;

    public ObservableCollection<Distro> Distros { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var list = await _distros.ListAsync();
            Distros.Clear();
            foreach (var d in list) Distros.Add(d);
        }
        catch (WslException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public Task StartAsync(string name) => ActionThenRefresh(() => _distros.StartAsync(name));

    [RelayCommand]
    public Task TerminateAsync(string name) => ActionThenRefresh(() => _distros.TerminateAsync(name));

    [RelayCommand]
    public Task SetDefaultAsync(string name) => ActionThenRefresh(() => _distros.SetDefaultAsync(name));

    [RelayCommand]
    public Task UnregisterAsync(string name) => ActionThenRefresh(() => _distros.UnregisterAsync(name));

    private async Task ActionThenRefresh(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
            await RefreshAsync();
        }
        catch (WslException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
