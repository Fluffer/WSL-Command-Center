using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class DeployViewModel : ObservableObject
{
    private readonly WslDeployService _deploy;
    private readonly WslDistroService _distros;

    public DeployViewModel(WslDeployService deploy, WslDistroService distros)
    {
        _deploy = deploy;
        _distros = distros;
    }

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

    // Advanced install fields — catalog distro and local file are mutually exclusive.
    [ObservableProperty] private CatalogEntry? _advancedCatalogEntry;
    [ObservableProperty] private string _advancedFromFile = "";
    [ObservableProperty] private string _advancedName = "";
    [ObservableProperty] private string _advancedLocation = "";
    [ObservableProperty] private int _advancedVersionIndex = 1; // 0 = WSL 1, 1 = WSL 2
    [ObservableProperty] private bool _advancedWebDownload;

    public bool IsAdvancedCatalogEnabled => string.IsNullOrWhiteSpace(AdvancedFromFile);
    public bool IsAdvancedFileEnabled => AdvancedCatalogEntry is null;

    partial void OnAdvancedFromFileChanged(string value)
        => OnPropertyChanged(nameof(IsAdvancedCatalogEnabled));

    partial void OnAdvancedCatalogEntryChanged(CatalogEntry? value)
        => OnPropertyChanged(nameof(IsAdvancedFileEnabled));

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

    [RelayCommand]
    public async Task InstallAdvancedAsync()
    {
        ErrorMessage = null;
        var distro = AdvancedCatalogEntry?.Name;
        var fromFile = string.IsNullOrWhiteSpace(AdvancedFromFile) ? null : AdvancedFromFile.Trim();
        var name = string.IsNullOrWhiteSpace(AdvancedName) ? null : AdvancedName.Trim();

        if (distro is null && fromFile is null)
        { ErrorMessage = "Select a catalog distro or choose a local image file."; return; }
        if (distro is not null && fromFile is not null)
        { ErrorMessage = "Pick either a catalog distro or a local image file, not both."; return; }
        if (fromFile is not null && name is null)
        { ErrorMessage = "A name is required when installing from a file."; return; }

        await Guarded(async () =>
        {
            var effectiveName = name ?? distro!;
            var existing = await ListExistingNamesSafeAsync();
            if (existing.Contains(effectiveName, StringComparer.OrdinalIgnoreCase))
            { ErrorMessage = $"A distro named '{effectiveName}' is already registered. Pick another name."; return; }

            await _deploy.InstallCustomAsync(new CustomInstallOptions
            {
                Distro = distro,
                FromFile = fromFile,
                Name = name,
                Location = string.IsNullOrWhiteSpace(AdvancedLocation) ? null : AdvancedLocation.Trim(),
                Version = AdvancedVersionIndex == 0 ? 1 : 2,
                WebDownload = AdvancedWebDownload,
            });
            StatusMessage = $"Installed {effectiveName}.";
        });
    }

    /// <summary>Collision check must not block the very first install — `wsl --list`
    /// fails when no distro is registered yet, so a failed list counts as "none".</summary>
    private async Task<IReadOnlyList<string>> ListExistingNamesSafeAsync()
    {
        try
        {
            var distros = await _distros.ListAsync();
            return distros.Select(d => d.Name).ToList();
        }
        catch (WslException) { return Array.Empty<string>(); }
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
