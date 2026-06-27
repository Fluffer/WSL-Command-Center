using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;
using Wsl.Core.Snapshots;

namespace Wsl.App.Logic.ViewModels;

public partial class SnapshotViewModel : ObservableObject
{
    private readonly WslSnapshotService _snaps;
    private readonly WslDistroService _distros;

    public SnapshotViewModel(WslSnapshotService snaps, WslDistroService distros)
    { _snaps = snaps; _distros = distros; }

    public ObservableCollection<string> Distros { get; } = new();
    public ObservableCollection<Snapshot> Snapshots { get; } = new();
    [ObservableProperty] private string? _selectedDistro;
    [ObservableProperty] private string? _newLabel;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    public async Task LoadAsync() => await Guarded(async () =>
    {
        var list = await _distros.ListAsync();
        Distros.Clear();
        foreach (var d in list) Distros.Add(d.Name);
        SelectedDistro ??= Distros.FirstOrDefault();
        ReloadSnapshots();
    });

    [RelayCommand]
    public Task CreateAsync() => SelectedDistro is null ? Task.CompletedTask : Guarded(async () =>
    {
        await _snaps.CreateAsync(SelectedDistro, NewLabel ?? "", wslVersion: 2);
        NewLabel = null;
        ReloadSnapshots();
    });

    [RelayCommand]
    public Task DeleteAsync(Snapshot snap) => Guarded(() =>
    {
        _snaps.Delete(snap);
        ReloadSnapshots();
        return Task.CompletedTask;
    });

    [RelayCommand]
    public Task RestoreCloneAsync((Snapshot snap, string newName, string installDir) a)
        => Guarded(() => _snaps.RestoreCloneAsync(a.snap, a.newName, a.installDir));

    [RelayCommand]
    public Task RestoreOverwriteAsync((Snapshot snap, string installDir) a)
        => Guarded(() => _snaps.RestoreOverwriteAsync(a.snap, a.installDir));

    private void ReloadSnapshots()
    {
        Snapshots.Clear();
        foreach (var s in _snaps.List(SelectedDistro)) Snapshots.Add(s);
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (System.Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
