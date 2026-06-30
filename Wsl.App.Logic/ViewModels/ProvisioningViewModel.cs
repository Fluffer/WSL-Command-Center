using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

/// <summary>
/// Drives the Provisioning page: apply a built-in template (run setup steps inside a distro)
/// and clone an existing distro under a new name. Default-user changes reuse
/// <see cref="WslDistroService"/> elsewhere and are out of scope here.
/// </summary>
public partial class ProvisioningViewModel : ObservableObject
{
    private readonly WslProvisioningService _provision;
    private readonly WslDistroService _distros;

    public ProvisioningViewModel(WslProvisioningService provision, WslDistroService distros)
    {
        _provision = provision;
        _distros = distros;
        foreach (var t in TemplateCatalog.BuiltIn) Templates.Add(t);
    }

    public ObservableCollection<Distro> Distros { get; } = new();
    public ObservableCollection<DistroTemplate> Templates { get; } = new();
    public ObservableCollection<StepResult> StepResults { get; } = new();

    [ObservableProperty] private Distro? _selectedDistro;
    [ObservableProperty] private DistroTemplate? _selectedTemplate;

    // Clone panel
    [ObservableProperty] private Distro? _cloneSource;
    [ObservableProperty] private string _cloneNewName = "";
    [ObservableProperty] private string _cloneInstallDir = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    public async Task LoadDistrosAsync()
    {
        await Guarded(async () =>
        {
            var list = await _distros.ListAsync();
            Distros.Clear();
            foreach (var d in list) Distros.Add(d);
            StatusMessage = $"{Distros.Count} distros found.";
        });
    }

    [RelayCommand]
    public async Task ApplyTemplateAsync()
    {
        if (SelectedDistro is null) { ErrorMessage = "Select a distro to provision."; return; }
        if (SelectedTemplate is null) { ErrorMessage = "Select a template to apply."; return; }

        await Guarded(async () =>
        {
            StepResults.Clear();
            var results = await _provision.ApplyTemplateAsync(SelectedDistro.Name, SelectedTemplate);
            foreach (var r in results) StepResults.Add(r);

            var failed = results.FirstOrDefault(r => !r.Success);
            if (failed is not null)
                ErrorMessage = $"Step \"{failed.Description}\" failed: {Truncate(failed.Output)}";
            else
                StatusMessage = $"Applied \"{SelectedTemplate.DisplayName}\" to {SelectedDistro.Name} ({results.Count} steps).";
        });
    }

    [RelayCommand]
    public async Task CloneAsync()
    {
        var source = CloneSource?.Name;
        var newName = CloneNewName.Trim();
        var dir = CloneInstallDir.Trim();

        if (string.IsNullOrEmpty(source)) { ErrorMessage = "Select a distro to clone."; return; }
        if (newName.Length == 0) { ErrorMessage = "Enter a name for the clone."; return; }
        if (dir.Length == 0) { ErrorMessage = "Choose an install directory for the clone."; return; }
        if (Distros.Any(d => string.Equals(d.Name, newName, StringComparison.OrdinalIgnoreCase)))
        { ErrorMessage = $"A distro named '{newName}' is already registered. Pick another name."; return; }

        await Guarded(async () =>
        {
            await _provision.CloneAsync(source!, newName, dir);
            StatusMessage = $"Cloned {source} to {newName}.";
            var list = await _distros.ListAsync();
            Distros.Clear();
            foreach (var d in list) Distros.Add(d);
        });
    }

    private static string Truncate(string s, int max = 200)
        => s.Length <= max ? s : s[..max] + "…";

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
