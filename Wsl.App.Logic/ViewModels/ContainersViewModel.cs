using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;
using Wsl.Core.Containers;
using Wsl.Core.Settings;

namespace Wsl.App.Logic.ViewModels;

/// <summary>
/// Drives the WSL Containers (wslc) preview page. The feature is gated behind a persisted
/// opt-in flag; even when enabled, the page degrades gracefully if wslc is absent or unreachable.
/// The raw-command runner is the primary surface; the structured container list is best-effort.
/// </summary>
public partial class ContainersViewModel : ObservableObject
{
    private readonly WslcService _wslc;
    private readonly WslDistroService _distros;
    private readonly IThemeService _settings;

    public ContainersViewModel(WslcService wslc, WslDistroService distros, IThemeService settings)
    {
        _wslc = wslc;
        _distros = distros;
        _settings = settings;
        _isPreviewEnabled = _settings.Load().EnableWslcPreview;
    }

    public ObservableCollection<WslcContainer> Containers { get; } = new();
    public ObservableCollection<Distro> Distros { get; } = new();

    [ObservableProperty] private bool _isPreviewEnabled;
    [ObservableProperty] private WslcAvailability? _availability;
    [ObservableProperty] private Distro? _selectedDistro;
    [ObservableProperty] private ContainerRuntime? _detectedRuntime;
    [ObservableProperty] private string _rawCommand = "";
    [ObservableProperty] private string _rawOutput = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>True when the typed raw command's leading verb is not on the read-only allowlist,
    /// so the UI should confirm before running it.</summary>
    public bool RawNeedsConfirm => !WslcCommand.IsReadOnly(RawCommand);

    partial void OnRawCommandChanged(string value) => OnPropertyChanged(nameof(RawNeedsConfirm));

    /// <summary>Persists the preview opt-in (without clobbering other settings) and, when enabling,
    /// probes for wslc.</summary>
    public async Task SetPreviewAsync(bool enabled)
    {
        var s = _settings.Load();
        s.EnableWslcPreview = enabled;
        _settings.Save(s);
        IsPreviewEnabled = enabled;

        if (enabled) await RefreshAsync();
        else { Availability = null; Containers.Clear(); Distros.Clear(); }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!IsPreviewEnabled) return;
        await Guarded(async () =>
        {
            Availability = await _wslc.DetectAsync();
            Containers.Clear();
            Distros.Clear();
            if (!Availability.IsAvailable)
            {
                StatusMessage = Availability.State == WslcState.NotFound
                    ? "wslc not found — install the WSL preview to use containers."
                    : "wslc is installed but did not respond.";
                return;
            }

            foreach (var c in await _wslc.ListContainersAsync()) Containers.Add(c);
            foreach (var d in await _distros.ListAsync()) Distros.Add(d);
            StatusMessage = $"wslc {Availability.Version ?? "preview"} — {Containers.Count} container(s).";
        });
    }

    [RelayCommand]
    public async Task ExecuteRawAsync()
    {
        var args = WslcCommand.Tokenize(RawCommand);
        if (args.Count == 0) { ErrorMessage = "Enter a wslc command (e.g. ps)."; return; }

        await Guarded(async () =>
        {
            var result = await _wslc.RunRawAsync(args);
            RawOutput = Compose(result);
            if (!result.Ok) ErrorMessage = $"wslc exited {result.ExitCode}.";
            else StatusMessage = $"wslc {args[0]} completed.";
        });
    }

    [RelayCommand]
    public async Task DetectRuntimeAsync()
    {
        if (SelectedDistro is null) { ErrorMessage = "Select a distro first."; return; }
        await Guarded(async () =>
        {
            DetectedRuntime = await _wslc.DetectRuntimeAsync(SelectedDistro.Name);
            StatusMessage = DetectedRuntime == ContainerRuntime.None
                ? $"No docker/podman runtime detected in {SelectedDistro.Name}."
                : $"{DetectedRuntime} detected in {SelectedDistro.Name}.";
        });
    }

    private static string Compose(RawResult r)
    {
        var parts = new[] { r.StdOut, r.StdErr }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim());
        return string.Join("\n", parts);
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
