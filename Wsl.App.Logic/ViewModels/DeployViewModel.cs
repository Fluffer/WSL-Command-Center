using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class DeployViewModel : ObservableObject
{
    private readonly WslDeployService _deploy;

    public DeployViewModel(WslDeployService deploy) => _deploy = deploy;

    public ObservableCollection<CatalogEntry> Catalog { get; } = new();

    [ObservableProperty] private CatalogEntry? _selectedCatalogEntry;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Import fields
    [ObservableProperty] private string _importName = "";
    [ObservableProperty] private string _importInstallDir = "";
    [ObservableProperty] private string _importArchivePath = "";
    [ObservableProperty] private int _importVersion = 2;

    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        await Guarded(async () =>
        {
            var entries = await _deploy.ListAvailableAsync();
            Catalog.Clear();
            foreach (var e in entries) Catalog.Add(e);
            StatusMessage = $"{Catalog.Count} distros available.";
        });
    }

    [RelayCommand]
    public async Task InstallSelectedAsync()
    {
        if (SelectedCatalogEntry is null) { ErrorMessage = "Select a distro first."; return; }
        await Guarded(async () =>
        {
            await _deploy.InstallFromCatalogAsync(SelectedCatalogEntry.Name);
            StatusMessage = $"Installed {SelectedCatalogEntry.Name}.";
        });
    }

    [RelayCommand]
    public async Task ImportArchiveAsync()
    {
        await Guarded(async () =>
        {
            var isVhdx = ImportArchivePath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);
            if (isVhdx)
                await _deploy.ImportVhdxAsync(ImportName, ImportInstallDir, ImportArchivePath, ImportVersion);
            else
                await _deploy.ImportTarAsync(ImportName, ImportInstallDir, ImportArchivePath, ImportVersion);
            StatusMessage = $"Imported {ImportName}.";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
