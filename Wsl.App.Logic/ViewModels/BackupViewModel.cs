using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly WslBackupService _backup;

    public BackupViewModel(WslBackupService backup) => _backup = backup;

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
            await _backup.ExportAsync(ExportDistro, ExportPath, ExportFormat);
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

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
