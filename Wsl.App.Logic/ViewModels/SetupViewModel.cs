using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;

namespace Wsl.App.Logic.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    private readonly IBrokerClient _broker;
    private readonly BootstrapStateStore _state;

    public SetupViewModel(IBrokerClient broker, BootstrapStateStore state)
    {
        _broker = broker;
        _state = state;
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _rebootRequired;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private bool _includePreRelease;

    [RelayCommand]
    public async Task EnableFeaturesAsync()
    {
        await Guarded(async () =>
        {
            await _state.WriteAsync(BootstrapStep.EnableFeatures);
            var resp = await _broker.SendAsync(new EnableFeaturesRequest());
            if (!resp.Success) { ErrorMessage = resp.Error; return; }
            if (resp.RebootRequired)
            {
                RebootRequired = true;
                await _state.WriteAsync(BootstrapStep.RebootPending);
                StatusMessage = "Restart required to finish enabling WSL.";
            }
            else
            {
                await ResumeAsync();
            }
        });
    }

    /// <summary>Called on app startup (and after reboot) to continue any pending bootstrap.</summary>
    [RelayCommand]
    public async Task ResumeAsync()
    {
        var step = await _state.ReadAsync();
        if (step is BootstrapStep.Done) { IsComplete = true; return; }

        await Guarded(async () =>
        {
            // From RebootPending (or EnableFeatures completed) → install kernel.
            var kernel = await _broker.SendAsync(new InstallOrUpdateKernelRequest(IncludePreRelease));
            if (!kernel.Success) { ErrorMessage = kernel.Error; return; }
            await _state.WriteAsync(BootstrapStep.SetDefaultVersion);

            var setDefault = await _broker.SendAsync(new SetDefaultWslVersionRequest(2));
            if (!setDefault.Success) { ErrorMessage = setDefault.Error; return; }

            await _state.ClearAsync();
            IsComplete = true;
            StatusMessage = "WSL is ready.";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        finally { IsBusy = false; }
    }
}
