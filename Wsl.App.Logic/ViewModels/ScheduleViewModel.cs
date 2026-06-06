using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;
using Wsl.Core.Scheduling;

namespace Wsl.App.Logic.ViewModels;

public partial class ScheduleViewModel : ObservableObject
{
    private readonly IWslScheduleService _sched;
    private readonly WslDistroService _distros;

    public ScheduleViewModel(IWslScheduleService sched, WslDistroService distros)
    {
        _sched = sched;
        _distros = distros;
    }

    public ObservableCollection<string> Distros { get; } = new();
    public ObservableCollection<string> Schedules { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private string _distro = "";
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private ExportFormat _format = ExportFormat.Tar;
    [ObservableProperty] private ScheduleFrequency _frequency = ScheduleFrequency.Daily;
    [ObservableProperty] private string _time = "02:30";
    [ObservableProperty] private int _keepCount = 7;

    public ExportFormat[] Formats { get; } = { ExportFormat.Tar, ExportFormat.TarGz, ExportFormat.Vhd };
    public ScheduleFrequency[] Frequencies { get; } = { ScheduleFrequency.Daily, ScheduleFrequency.Weekly };

    [RelayCommand]
    public async Task LoadAsync()
    {
        await Guarded(async () =>
        {
            var list = await _distros.ListAsync();
            Distros.Clear();
            foreach (var d in list) Distros.Add(d.Name);

            await RefreshSchedulesInner();
        });
    }

    [RelayCommand]
    public async Task CreateAsync()
    {
        await Guarded(async () =>
        {
            var schedule = new BackupSchedule(Distro, Folder, Format, Frequency, Time, KeepCount);
            await _sched.CreateAsync(schedule);
            StatusMessage = $"Scheduled {Frequency} backup of {Distro} at {Time}.";
            await RefreshSchedulesInner();
        });
    }

    [RelayCommand]
    public async Task DeleteAsync(string taskName)
    {
        await Guarded(async () =>
        {
            await _sched.DeleteAsync(taskName);
            StatusMessage = $"Removed scheduled task {taskName}.";
            await RefreshSchedulesInner();
        });
    }

    private async Task RefreshSchedulesInner()
    {
        var names = await _sched.ListAsync();
        Schedules.Clear();
        foreach (var n in names) Schedules.Add(n);
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
