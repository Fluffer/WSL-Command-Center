using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Contracts;
using Wsl.Core.Ipc;

namespace Wsl.App.Logic.ViewModels;

/// <summary>Display wrapper around the broker's <see cref="DiskInfo"/> payload.</summary>
public sealed record DiskRow(DiskInfo Info)
{
    public string DeviceId => Info.DeviceId;
    public string Model => Info.Model.Length > 0 ? Info.Model : "Unknown disk";
    public bool IsSystem => Info.IsSystem;
    public bool CanMount => !Info.IsSystem;

    /// <summary>Short device id, e.g. <c>PHYSICALDRIVE2</c> — also the typed-confirm token.</summary>
    public string ShortName => Info.DeviceId[(Info.DeviceId.LastIndexOf('\\') + 1)..].ToUpperInvariant();

    public string SizeText =>
        string.Create(CultureInfo.CurrentCulture, $"{Info.SizeBytes / 1_073_741_824.0:F1} GB");

    public string Description
    {
        get
        {
            var serial = Info.SerialNumber.Length > 0 ? $" · S/N {Info.SerialNumber}" : "";
            var system = Info.IsSystem ? " · System disk — cannot be mounted" : "";
            return $"{Info.DeviceId} · {SizeText}{serial}{system}";
        }
    }
}

/// <summary>Mount/unmount physical disks and VHDs into WSL2. All operations require elevation
/// and go through the broker; <see cref="IBrokerClient.SendAsync"/> handles launch + elevation.</summary>
public partial class DisksViewModel : ObservableObject
{
    private readonly IBrokerClient _broker;

    public DisksViewModel(IBrokerClient broker) => _broker = broker;

    public ObservableCollection<DiskRow> Disks { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _vhdPath = "";

    [RelayCommand]
    public async Task LoadDisksAsync()
    {
        await Guarded(async () =>
        {
            var resp = await _broker.SendAsync(new ListDisksRequest());
            if (!resp.Success) { ErrorMessage = resp.Error; return; }
            Disks.Clear();
            foreach (var d in resp.Disks ?? Array.Empty<DiskInfo>())
                Disks.Add(new DiskRow(d));
        });
    }

    /// <summary>Called from the page's mount dialog (codebehind dialog idiom, like Dashboard).</summary>
    public async Task MountAsync(string disk, bool vhd, bool bare,
        int? partition, string? type, string? options, string? name)
    {
        await Guarded(async () =>
        {
            var resp = await _broker.SendAsync(new MountDiskRequest(
                disk, vhd, bare, partition,
                Blank(type), Blank(options), Blank(name)));
            if (!resp.Success) { ErrorMessage = resp.Error; return; }
            StatusMessage = bare
                ? $"Attached {disk} to WSL2 (bare — mount it from inside a distro)."
                : $"Mounted {disk}. Distros see it under /mnt/wsl/.";
        });
    }

    [RelayCommand]
    public async Task UnmountAsync(string disk)
    {
        await Guarded(async () =>
        {
            var resp = await _broker.SendAsync(new UnmountDiskRequest(disk));
            if (!resp.Success) { ErrorMessage = resp.Error; return; }
            StatusMessage = $"Unmounted {disk}.";
        });
    }

    [RelayCommand]
    public async Task UnmountAllAsync()
    {
        await Guarded(async () =>
        {
            var resp = await _broker.SendAsync(new UnmountDiskRequest(null));
            if (!resp.Success) { ErrorMessage = resp.Error; return; }
            StatusMessage = "Unmounted all disks from WSL2.";
        });
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (Exception ex) { ErrorMessage = ex.Message; } // broker launch/elevation failures
        finally { IsBusy = false; }
    }
}
