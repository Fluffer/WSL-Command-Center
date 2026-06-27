using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly WslBackupService _backup;
    private readonly WslDistroService _distros;
    private readonly WslDeployService _deploy;
    private readonly StatePreservingExport _preserving;

    public BackupViewModel(WslBackupService backup, WslDistroService distros, WslDeployService deploy, StatePreservingExport preserving)
    {
        _backup = backup;
        _distros = distros;
        _deploy = deploy;
        _preserving = preserving;
    }

    public ObservableCollection<string> Distros { get; } = new();

    /// <summary>Running distros — the page warns about these before exporting.</summary>
    public Task<IReadOnlyList<string>> RunningDistrosAsync() => _preserving.RunningAsync();

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

    // Export
    [ObservableProperty] private string _exportDistro = "";
    [ObservableProperty] private string _exportPath = "";
    [ObservableProperty] private ExportFormat _exportFormat = ExportFormat.Tar;

    // Restore
    [ObservableProperty] private string _restoreName = "";
    [ObservableProperty] private string _restoreInstallDir = "";
    [ObservableProperty] private string _restoreArchivePath = "";
    [ObservableProperty] private ExportFormat _restoreFormat = ExportFormat.Tar;
    [ObservableProperty] private int _restoreVersion = 2;

    public ExportFormat[] Formats { get; } = { ExportFormat.Tar, ExportFormat.TarGz, ExportFormat.Vhd };

    [RelayCommand]
    public async Task ExportAsync()
    {
        await Guarded(async () =>
        {
            await _preserving.RunAsync(c => _backup.ExportAsync(ExportDistro, ExportPath, ExportFormat, c));
            StatusMessage = $"Exported {ExportDistro} → {ExportPath}";
        });
    }

    [RelayCommand]
    public async Task RestoreAsync()
    {
        await Guarded(async () =>
        {
            await _backup.RestoreAsync(RestoreName, RestoreInstallDir, RestoreArchivePath,
                                       RestoreFormat, RestoreVersion);
            StatusMessage = $"Restored {RestoreName}";
        });
    }

    // Import in place (register an existing VHDX)
    [ObservableProperty] private string _inPlaceName = "";
    [ObservableProperty] private string _inPlaceVhdxPath = "";

    [RelayCommand]
    public async Task ImportInPlaceAsync()
    {
        ErrorMessage = null;
        var name = InPlaceName.Trim();
        var path = InPlaceVhdxPath.Trim();

        if (name.Length == 0)
        { ErrorMessage = "Enter a name for the distro."; return; }
        if (path.Length == 0)
        { ErrorMessage = "Choose a .vhdx file."; return; }
        if (!path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
        { ErrorMessage = "Import in place requires a .vhdx file."; return; }
        if (!File.Exists(path))
        { ErrorMessage = $"File not found: {path}"; return; }

        await Guarded(async () =>
        {
            // import-in-place silently clobbers an existing registration — refuse collisions.
            var existing = await ListExistingNamesSafeAsync();
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase))
            { ErrorMessage = $"A distro named '{name}' is already registered. Pick another name."; return; }

            await _deploy.ImportInPlaceAsync(name, path);
            StatusMessage = $"Registered {name} from {path}";
        });
    }

    /// <summary>Collision check must not block when `wsl --list` fails (e.g. no
    /// distro registered yet) — a failed list counts as "none".</summary>
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
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
