using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly WslDistroService _distros;
    private readonly WslDiskService _disk;
    private readonly WslSystemService _system;

    public DashboardViewModel(WslDistroService distros, WslDiskService disk, WslSystemService system)
    {
        _distros = distros;
        _disk = disk;
        _system = system;
        Distros.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoDistros));
    }

    public ObservableCollection<Distro> Distros { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    /// <summary>Non-error outcome (e.g. fstrim result). Separate from <see cref="ErrorMessage"/>.</summary>
    [ObservableProperty] private string? _statusMessage;

    /// <summary>One-line WSL platform summary ("WSL 2.4.13.0 · kernel … · default: …").
    /// Null when the info could not be retrieved; the view collapses the status line then.</summary>
    [ObservableProperty] private string? _wslStatusSummary;

    /// <summary>True when there are no distros to show and we are not mid-load.
    /// Drives the empty-state placeholder (via BoolToVisibilityConverter in the view).</summary>
    public bool HasNoDistros => !IsBusy && Distros.Count == 0;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(HasNoDistros));

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
            await LoadStatusSummaryAsync();
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

    [RelayCommand]
    public Task OptimizeAsync(string name) => ActionThenRefresh(() => _disk.OptimizeAsync(name));

    /// <summary>Reclaims space via fstrim and surfaces the outcome (bytes trimmed, or why it
    /// could not run) in <see cref="StatusMessage"/>. Not an ActionThenRefresh because we keep
    /// the returned message.</summary>
    [RelayCommand]
    public async Task ReclaimAsync(string name)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try
        {
            var outcome = await _disk.TrimAsync(name);
            StatusMessage = outcome;       // set before refresh so a refresh failure can't swallow the outcome
            await RefreshAsync();          // sizes may have changed (clears ErrorMessage, not StatusMessage)
        }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Moves a distro's storage to another directory. Two parameters, so a plain method
    /// (codebehind invokes VM methods directly; [RelayCommand] supports at most one).
    /// The view must run <see cref="EvaluateMovePreflightAsync"/> and get a pass before calling this.</summary>
    public Task MoveAsync(string name, string targetDir)
        => ActionThenRefresh(() => _disk.MoveAsync(name, targetDir));

    /// <summary>Move preflight: queries the WSL version (gate ≥ <see cref="MovePreflight.MinWslVersion"/>)
    /// and combines it with filesystem facts the view supplies (DriveInfo/vhdx reads stay view-side
    /// so this stays testable). A failed version query fails the gate rather than bypassing it.</summary>
    public async Task<MovePreflightResult> EvaluateMovePreflightAsync(
        long vhdxSizeBytes, long targetFreeBytes, string targetDriveFormat)
    {
        Version wslVersion;
        try
        {
            wslVersion = (await _system.GetVersionInfoAsync()).WslVersionParsed;
        }
        catch (Exception) // WslException, or Win32Exception when wsl.exe itself is missing
        {
            wslVersion = new Version(0, 0); // unknown → fails the ≥ MinWslVersion gate
        }
        return MovePreflight.Evaluate(wslVersion, vhdxSizeBytes, targetFreeBytes, targetDriveFormat);
    }

    /// <summary>Sets the default login user for a distro. Two parameters, so a plain method
    /// (codebehind invokes VM methods directly; [RelayCommand] supports at most one).
    /// Refreshes afterwards: listing users boots the distro, so the state pill needs updating.</summary>
    public Task SetDefaultUserAsync(string name, string user)
        => ActionThenRefresh(() => _distros.SetDefaultUserAsync(name, user));

    /// <summary>Login-capable users of a distro for the set-default-user picker.
    /// Failures surface via ErrorMessage and yield an empty list.</summary>
    public async Task<IReadOnlyList<string>> ListUsersAsync(string name)
    {
        ErrorMessage = null;
        try
        {
            return await _distros.ListUsersAsync(name);
        }
        catch (WslException ex)
        {
            ErrorMessage = ex.Message;
            return Array.Empty<string>();
        }
    }

    /// <summary>Best-effort platform status line; failures must never break the distro refresh.</summary>
    private async Task LoadStatusSummaryAsync()
    {
        try
        {
            var version = await _system.GetVersionInfoAsync();
            var status = await _system.GetStatusAsync();
            WslStatusSummary = BuildSummary(version, status);
        }
        catch (Exception)
        {
            WslStatusSummary = null;
        }
    }

    private static string? BuildSummary(WslVersionInfo version, WslStatus status)
    {
        var parts = new List<string>();
        if (version.WslVersion is not null) parts.Add($"WSL {version.WslVersion}");
        if (version.KernelVersion is not null) parts.Add($"kernel {version.KernelVersion}");
        if (status.DefaultDistro is not null)
            parts.Add(status.DefaultVersion is int v
                ? $"default: {status.DefaultDistro} (v{v})"
                : $"default: {status.DefaultDistro}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

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
