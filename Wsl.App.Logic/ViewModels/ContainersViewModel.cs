using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;
using Wsl.Core.Containers;
using Wsl.Core.Settings;

namespace Wsl.App.Logic.ViewModels;

/// <summary>
/// UI-facing composition of a wslc session with its resolved disk usage, so the Sessions tab can
/// bind everything about a row from one object without a second lookup at render time.
/// <see cref="WslcSessionService.GetDiskUsage"/> is synchronous (plain file-system probes), so this
/// is built eagerly whenever the session list is (re)loaded.
/// </summary>
public record WslcSessionRow(WslcSession Session, WslcSessionDiskUsage Usage)
{
    public int Id => Session.Id;
    public int CreatorPid => Session.CreatorPid;
    public string DisplayName => Session.DisplayName;
    public string StorageHuman => FormatBytes(Usage.Storage?.LogicalBytes);
    public string SwapHuman => FormatBytes(Usage.Swap?.LogicalBytes);
    public string TotalHuman => Usage.TotalHumanReadable;

    /// <summary>Mirrors <c>WslcSessionService.FormatBytes</c> (that one is internal to Wsl.Core),
    /// so per-VHD sizes can be rendered here the same way the combined total already is.</summary>
    private static string FormatBytes(long? bytes)
    {
        if (bytes is not long b) return "unknown";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = b;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// Drives the WSL Containers (wslc) preview page. The feature is gated behind a persisted
/// opt-in flag; even when enabled, the page degrades gracefully if wslc is absent or unreachable.
/// Covers containers, images, volumes, networks, sessions and the wslc settings.yaml surface, plus
/// the raw-command escape hatch and the informational runtime-detection bridge.
/// </summary>
public partial class ContainersViewModel : ObservableObject
{
    private readonly WslcService _wslc;
    private readonly WslcResourceService _resources;
    private readonly WslcSettingsService _wslcSettings;
    private readonly WslcSessionService _sessions;
    private readonly WslDistroService _distros;
    private readonly IThemeService _settings;

    public ContainersViewModel(
        WslcService wslc,
        WslcResourceService resources,
        WslcSettingsService wslcSettings,
        WslcSessionService sessions,
        WslDistroService distros,
        IThemeService settings)
    {
        _wslc = wslc;
        _resources = resources;
        _wslcSettings = wslcSettings;
        _sessions = sessions;
        _distros = distros;
        _settings = settings;
        _isPreviewEnabled = _settings.Load().EnableWslcPreview;
    }

    // ── Collections ─────────────────────────────────────────────────────────

    public ObservableCollection<WslcContainer> Containers { get; } = new();
    public ObservableCollection<WslcImage> Images { get; } = new();
    public ObservableCollection<WslcVolume> Volumes { get; } = new();
    public ObservableCollection<WslcNetwork> Networks { get; } = new();
    public ObservableCollection<WslcSessionRow> Sessions { get; } = new();
    public ObservableCollection<Distro> Distros { get; } = new();

    // ── Page-level state ────────────────────────────────────────────────────

    [ObservableProperty] private bool _isPreviewEnabled;
    [ObservableProperty] private WslcAvailability? _availability;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // ── Raw command box ─────────────────────────────────────────────────────

    [ObservableProperty] private string _rawCommand = "";
    [ObservableProperty] private string _rawOutput = "";

    /// <summary>True when the typed raw command's leading verb is not on the read-only allowlist,
    /// so the UI should confirm before running it.</summary>
    public bool RawNeedsConfirm => !WslcCommand.IsReadOnly(RawCommand);

    partial void OnRawCommandChanged(string value) => OnPropertyChanged(nameof(RawNeedsConfirm));

    // ── Runtime bridge ──────────────────────────────────────────────────────

    [ObservableProperty] private Distro? _selectedDistro;
    [ObservableProperty] private ContainerRuntime? _detectedRuntime;

    // ── Container detail output (Logs / Inspect) ───────────────────────────

    [ObservableProperty] private string? _containerOutputTitle;
    [ObservableProperty] private string? _containerOutput;

    // ── Deploy container form ──────────────────────────────────────────────

    [ObservableProperty] private string _deployImage = "";
    [ObservableProperty] private string _deployName = "";
    [ObservableProperty] private string _deployCommand = "";
    [ObservableProperty] private string _deployEnv = "";
    [ObservableProperty] private string _deployPorts = "";
    [ObservableProperty] private string _deployVolumes = "";
    [ObservableProperty] private string _deployMemory = "";
    [ObservableProperty] private string _deployCpus = "";
    [ObservableProperty] private string _deployNetwork = "";
    [ObservableProperty] private bool _deployRemoveOnExit;

    // ── Images tab ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _pullImageName = "";
    [ObservableProperty] private WslcImage? _selectedImage;
    [ObservableProperty] private string _tagTargetInput = "";
    [ObservableProperty] private string _registryServer = "";
    [ObservableProperty] private string _registryUsername = "";

    // ── Volumes tab ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _newVolumeName = "";
    [ObservableProperty] private string _newVolumeDriver = "guest";
    public IReadOnlyList<string> VolumeDriverOptions { get; } = new[] { "guest", "vhd" };

    // ── Networks tab ────────────────────────────────────────────────────────

    [ObservableProperty] private string _newNetworkName = "";
    [ObservableProperty] private string _newNetworkDriver = "bridge";

    // ── Configuration tab (settings.yaml) ──────────────────────────────────

    [ObservableProperty] private string? _settingsCpuCount;
    [ObservableProperty] private string? _settingsMemorySize;
    [ObservableProperty] private string? _settingsMaxStorageSize;
    [ObservableProperty] private string? _settingsDefaultBindingAddress;
    [ObservableProperty] private CredentialStoreKind _settingsCredentialStore;

    [ObservableProperty] private string? _cpuCountError;
    [ObservableProperty] private string? _memorySizeError;
    [ObservableProperty] private string? _maxStorageSizeError;
    [ObservableProperty] private string? _defaultBindingAddressError;

    public string SettingsFilePath => _wslcSettings.SettingsFilePath;
    public string SessionChangesMessage => WslcSettingsService.SessionChangesRequireNewSessionMessage;
    public IReadOnlyList<CredentialStoreKind> CredentialStoreOptions { get; } = Enum.GetValues<CredentialStoreKind>();

    public bool SettingsHasErrors =>
        CpuCountError is not null || MemorySizeError is not null ||
        MaxStorageSizeError is not null || DefaultBindingAddressError is not null;

    partial void OnSettingsCpuCountChanged(string? value)
    {
        CpuCountError = ValidateOrNull(value, WslcSettingsService.ValidateCpuCount);
        OnPropertyChanged(nameof(SettingsHasErrors));
    }

    partial void OnSettingsMemorySizeChanged(string? value)
    {
        MemorySizeError = ValidateOrNull(value, WslcSettingsService.ValidateMemorySize);
        OnPropertyChanged(nameof(SettingsHasErrors));
    }

    partial void OnSettingsMaxStorageSizeChanged(string? value)
    {
        MaxStorageSizeError = ValidateOrNull(value, WslcSettingsService.ValidateMaxStorageSize);
        OnPropertyChanged(nameof(SettingsHasErrors));
    }

    partial void OnSettingsDefaultBindingAddressChanged(string? value)
    {
        DefaultBindingAddressError = ValidateOrNull(value, WslcSettingsService.ValidateDefaultBindingAddress);
        OnPropertyChanged(nameof(SettingsHasErrors));
    }

    /// <summary>A blank field means "leave unset" (the key stays commented out) — only a non-blank
    /// value is run through the Validate* helper.</summary>
    private static string? ValidateOrNull(string? value, Func<string, string?> validate)
        => string.IsNullOrWhiteSpace(value) ? null : validate(value.Trim());

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Preview opt-in ──────────────────────────────────────────────────────

    /// <summary>Persists the preview opt-in (without clobbering other settings) and, when enabling,
    /// probes for wslc.</summary>
    public async Task SetPreviewAsync(bool enabled)
    {
        var s = _settings.Load();
        s.EnableWslcPreview = enabled;
        _settings.Save(s);
        IsPreviewEnabled = enabled;

        if (enabled) await RefreshAsync();
        else
        {
            Availability = null;
            Containers.Clear();
            Images.Clear();
            Volumes.Clear();
            Networks.Clear();
            Sessions.Clear();
            Distros.Clear();
        }
    }

    // ── Master refresh (Containers + runtime-bridge distros) ──────────────
    // Images/Volumes/Networks/Sessions/Configuration are lazy-loaded the first time their tab is
    // selected (each has its own Refresh command below) so opening the page doesn't fan out into
    // a burst of wslc invocations before the user asks for them.

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

            await LoadContainersAsync();
            await LoadDistrosAsync();
            StatusMessage = $"wslc {Availability.Version ?? "preview"} — {Containers.Count} container(s).";
        });
    }

    // ── Containers tab ──────────────────────────────────────────────────────

    [RelayCommand]
    public Task RefreshContainersAsync() => Guarded(LoadContainersAsync);

    [RelayCommand]
    public Task StartContainerAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.StartAsync(c.Id);
        if (!r.Ok) { ErrorMessage = ErrText(r); return; }
        StatusMessage = $"Started {c.Name}.";
        await LoadContainersAsync();
    });

    [RelayCommand]
    public Task StopContainerAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.StopAsync(c.Id);
        if (!r.Ok) { ErrorMessage = ErrText(r); return; }
        StatusMessage = $"Stopped {c.Name}.";
        await LoadContainersAsync();
    });

    [RelayCommand]
    public Task RestartContainerAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.RestartAsync(c.Id);
        if (!r.Ok) { ErrorMessage = ErrText(r); return; }
        StatusMessage = $"Restarted {c.Name}.";
        await LoadContainersAsync();
    });

    /// <summary>Caller is expected to confirm first — killing forcibly stops without a graceful
    /// shutdown.</summary>
    [RelayCommand]
    public Task KillContainerAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.KillAsync(c.Id);
        if (!r.Ok) { ErrorMessage = ErrText(r); return; }
        StatusMessage = $"Killed {c.Name}.";
        await LoadContainersAsync();
    });

    /// <summary>
    /// Caller is expected to confirm first — removes the container instance. A running container
    /// needs <c>--force</c>: wslc otherwise refuses with WSLC_E_CONTAINER_IS_RUNNING, which left
    /// the app unable to remove anything it had just deployed. <see cref="NeedsForceRemove"/>
    /// decides, and the confirmation dialog says so before this runs. State is Unknown only on
    /// columnar-fallback rows, where running-ness isn't knowable — force is not assumed there, and
    /// wslc's own error tells the user to stop it first.
    /// </summary>
    [RelayCommand]
    public Task RemoveContainerAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.RemoveAsync(c.Id, force: NeedsForceRemove(c.State));
        if (!r.Ok) { ErrorMessage = ErrText(r); return; }
        StatusMessage = $"Removed {c.Name}.";
        await LoadContainersAsync();
    });

    /// <summary>True when removing this container requires <c>--force</c> (it is running).</summary>
    public static bool NeedsForceRemove(WslcContainerState state)
        => state == WslcContainerState.Running;

    /// <summary>Caller is expected to confirm first — removes ALL stopped containers.</summary>
    [RelayCommand]
    public Task PruneContainersAsync() => Guarded(async () =>
    {
        var r = await _wslc.PruneContainersAsync();
        if (!r.Ok) { ErrorMessage = ErrText(r); return; }
        StatusMessage = "Pruned stopped containers.";
        await LoadContainersAsync();
    });

    [RelayCommand]
    public Task ShowContainerLogsAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.GetLogsAsync(c.Id, tailLines: 200, timestamps: true);
        ContainerOutputTitle = $"Logs — {c.Name}";
        ContainerOutput = Compose(r);
        if (!r.Ok) ErrorMessage = ErrText(r);
    });

    [RelayCommand]
    public Task InspectContainerAsync(WslcContainer c) => Guarded(async () =>
    {
        var r = await _wslc.InspectAsync(c.Id);
        ContainerOutputTitle = $"Inspect — {c.Name}";
        ContainerOutput = Compose(r);
        if (!r.Ok) ErrorMessage = ErrText(r);
    });

    private async Task LoadContainersAsync()
    {
        Containers.Clear();
        foreach (var c in await _wslc.ListContainersAsync()) Containers.Add(c);
    }

    /// <summary>True when Start should be offered for a container in this state — a container
    /// that has never run, or one that has already exited. Pure and static so the page's
    /// per-row action gating is unit-testable without a XAML host.</summary>
    public static bool CanStart(WslcContainerState state)
        => state is WslcContainerState.Created or WslcContainerState.Exited;

    /// <summary>True when Stop/Restart should be offered — only while the container is running.</summary>
    public static bool CanStopOrRestart(WslcContainerState state)
        => state == WslcContainerState.Running;

    // ── Deploy container form ──────────────────────────────────────────────
    // Two distinct verbs, deliberately not collapsed into one command: Deploy runs the container
    // immediately (detached), Create only stages it (appears as Created, started later by the user).

    /// <summary>`wslc run --detach` — creates AND starts the container, detached.</summary>
    [RelayCommand]
    public Task DeployContainerAsync() => RunDeployFormAsync(_wslc.RunDetachedAsync, "Deployed");

    /// <summary>`wslc create` — stages the container without starting it.</summary>
    [RelayCommand]
    public Task CreateContainerOnlyAsync() => RunDeployFormAsync(_wslc.CreateAsync, "Created");

    private Task RunDeployFormAsync(
        Func<WslcRunOptions, CancellationToken, Task<RawResult>> invoke, string verb)
        => Guarded(async () =>
        {
            var options = BuildDeployOptions();
            if (options is null) return; // validation error already set as ErrorMessage

            var r = await invoke(options, default);
            if (!r.Ok) { ErrorMessage = ErrText(r); return; } // form retained for correction

            var id = (r.StdOut ?? "").Trim();
            StatusMessage = string.IsNullOrEmpty(id) ? $"{verb} container." : $"{verb} {id}.";
            ClearDeployForm();
            await LoadContainersAsync();
        });

    /// <summary>Validates and maps the Deploy form fields onto <see cref="WslcRunOptions"/>. Returns
    /// null and sets <see cref="ErrorMessage"/> on the first validation failure, so the caller never
    /// invokes wslc with bad input.</summary>
    private WslcRunOptions? BuildDeployOptions()
    {
        var image = DeployImage.Trim();
        if (string.IsNullOrWhiteSpace(image)) { ErrorMessage = "Enter an image to deploy."; return null; }

        var env = ParseEnvLines(DeployEnv, out var envError);
        if (envError is not null) { ErrorMessage = envError; return null; }

        string? cpus = null;
        if (!string.IsNullOrWhiteSpace(DeployCpus))
        {
            cpus = DeployCpus.Trim();
            if (!double.TryParse(cpus, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                ErrorMessage = $"CPUs must be a number, e.g. 0.5 or 2 (got \"{cpus}\").";
                return null;
            }
        }

        var command = WslcCommand.Tokenize(DeployCommand);
        var ports = SplitList(DeployPorts);
        var volumes = SplitList(DeployVolumes);

        return new WslcRunOptions(
            Image: image,
            Name: string.IsNullOrWhiteSpace(DeployName) ? null : DeployName.Trim(),
            Command: command.Count > 0 ? command : null,
            Env: env,
            PublishedPorts: ports.Count > 0 ? ports : null,
            Volumes: volumes.Count > 0 ? volumes : null,
            Memory: string.IsNullOrWhiteSpace(DeployMemory) ? null : DeployMemory.Trim(),
            Cpus: cpus,
            Network: string.IsNullOrWhiteSpace(DeployNetwork) ? null : DeployNetwork.Trim(),
            Remove: DeployRemoveOnExit);
    }

    /// <summary>Parses `KEY=VALUE` lines (blank lines ignored). A non-blank line with no `=` is a
    /// validation error, not a silently dropped row — <paramref name="error"/> is set and the
    /// returned dictionary should be discarded in that case.</summary>
    private static IReadOnlyDictionary<string, string>? ParseEnvLines(string input, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(input)) return null;

        var env = new Dictionary<string, string>();
        foreach (var rawLine in input.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var idx = line.IndexOf('=');
            if (idx <= 0)
            {
                error = $"Invalid environment line (expected KEY=VALUE): \"{line}\".";
                return null;
            }
            env[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return env.Count > 0 ? env : null;
    }

    /// <summary>Splits comma- or newline-separated free text (published ports, volumes) into a
    /// trimmed, non-empty list.</summary>
    private static List<string> SplitList(string input)
        => string.IsNullOrWhiteSpace(input)
            ? new List<string>()
            : input.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    private void ClearDeployForm()
    {
        DeployImage = "";
        DeployName = "";
        DeployCommand = "";
        DeployEnv = "";
        DeployPorts = "";
        DeployVolumes = "";
        DeployMemory = "";
        DeployCpus = "";
        DeployNetwork = "";
        DeployRemoveOnExit = false;
    }

    // ── Images tab ──────────────────────────────────────────────────────────

    [RelayCommand]
    public Task RefreshImagesAsync() => Guarded(LoadImagesAsync);

    [RelayCommand]
    public Task PullImageAsync() => Guarded(async () =>
    {
        if (string.IsNullOrWhiteSpace(PullImageName)) { ErrorMessage = "Enter an image to pull, e.g. ubuntu:latest."; return; }
        var name = PullImageName.Trim();
        var r = await _resources.PullImageAsync(name);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Pulled {name}.";
        PullImageName = "";
        await LoadImagesAsync();
    });

    /// <summary>Caller is expected to confirm first — removes the image.</summary>
    [RelayCommand]
    public Task RemoveImageAsync(WslcImage image) => Guarded(async () =>
    {
        var r = await _resources.RemoveImageAsync(image.RepoTag, force: true);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Removed {image.RepoTag}.";
        await LoadImagesAsync();
    });

    [RelayCommand]
    public Task TagSelectedImageAsync() => Guarded(async () =>
    {
        if (SelectedImage is null) { ErrorMessage = "Select an image to tag."; return; }
        if (string.IsNullOrWhiteSpace(TagTargetInput)) { ErrorMessage = "Enter a target tag, e.g. myrepo/name:tag."; return; }
        var target = TagTargetInput.Trim();
        var r = await _resources.TagImageAsync(SelectedImage.RepoTag, target);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Tagged {SelectedImage.RepoTag} as {target}.";
        TagTargetInput = "";
        await LoadImagesAsync();
    });

    /// <summary>Caller is expected to confirm first — removes ALL images not referenced by any
    /// container.</summary>
    [RelayCommand]
    public Task PruneImagesAsync() => Guarded(async () =>
    {
        var r = await _resources.PruneImagesAsync(all: true);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = "Pruned unused images.";
        await LoadImagesAsync();
    });

    /// <summary>Login pipes <paramref name="password"/> to wslc over stdin and never retains it —
    /// there is no field or property backing it; the caller (code-behind) clears its PasswordBox
    /// immediately after this call returns, on either outcome.</summary>
    [RelayCommand]
    public Task RegistryLoginAsync(string password) => Guarded(async () =>
    {
        if (string.IsNullOrWhiteSpace(RegistryUsername) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "Username and password are required.";
            return;
        }
        var server = string.IsNullOrWhiteSpace(RegistryServer) ? null : RegistryServer.Trim();
        var r = await _resources.LoginAsync(server, RegistryUsername.Trim(), password);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = server is null ? "Registry login succeeded." : $"Logged in to {server}.";
    });

    [RelayCommand]
    public Task RegistryLogoutAsync() => Guarded(async () =>
    {
        var server = string.IsNullOrWhiteSpace(RegistryServer) ? null : RegistryServer.Trim();
        var r = await _resources.LogoutAsync(server);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = server is null ? "Registry logout succeeded." : $"Logged out of {server}.";
    });

    private async Task LoadImagesAsync()
    {
        Images.Clear();
        foreach (var i in await _resources.ListImagesAsync()) Images.Add(i);
    }

    // ── Volumes tab ─────────────────────────────────────────────────────────

    [RelayCommand]
    public Task RefreshVolumesAsync() => Guarded(LoadVolumesAsync);

    [RelayCommand]
    public Task CreateVolumeAsync() => Guarded(async () =>
    {
        if (string.IsNullOrWhiteSpace(NewVolumeName)) { ErrorMessage = "Enter a volume name."; return; }
        var name = NewVolumeName.Trim();
        var driver = string.IsNullOrWhiteSpace(NewVolumeDriver) ? null : NewVolumeDriver.Trim();
        var r = await _resources.CreateVolumeAsync(name, driver);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Created volume {name}.";
        NewVolumeName = "";
        await LoadVolumesAsync();
    });

    /// <summary>Caller is expected to confirm first — removes the volume and its data.</summary>
    [RelayCommand]
    public Task RemoveVolumeAsync(WslcVolume v) => Guarded(async () =>
    {
        var r = await _resources.RemoveVolumeAsync(v.Name);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Removed volume {v.Name}.";
        await LoadVolumesAsync();
    });

    /// <summary>Caller is expected to confirm first — removes ALL unused volumes and their data.</summary>
    [RelayCommand]
    public Task PruneVolumesAsync() => Guarded(async () =>
    {
        var r = await _resources.PruneVolumesAsync(all: true);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = "Pruned unused volumes.";
        await LoadVolumesAsync();
    });

    private async Task LoadVolumesAsync()
    {
        Volumes.Clear();
        foreach (var v in await _resources.ListVolumesAsync()) Volumes.Add(v);
    }

    // ── Networks tab ────────────────────────────────────────────────────────

    [RelayCommand]
    public Task RefreshNetworksAsync() => Guarded(LoadNetworksAsync);

    [RelayCommand]
    public Task CreateNetworkAsync() => Guarded(async () =>
    {
        if (string.IsNullOrWhiteSpace(NewNetworkName)) { ErrorMessage = "Enter a network name."; return; }
        var name = NewNetworkName.Trim();
        var driver = string.IsNullOrWhiteSpace(NewNetworkDriver) ? null : NewNetworkDriver.Trim();
        var r = await _resources.CreateNetworkAsync(name, driver);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Created network {name}.";
        NewNetworkName = "";
        await LoadNetworksAsync();
    });

    /// <summary>Caller is expected to confirm first — removes the network.</summary>
    [RelayCommand]
    public Task RemoveNetworkAsync(WslcNetwork n) => Guarded(async () =>
    {
        var r = await _resources.RemoveNetworkAsync(n.Name);
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = $"Removed network {n.Name}.";
        await LoadNetworksAsync();
    });

    /// <summary>Caller is expected to confirm first — removes ALL unused networks.</summary>
    [RelayCommand]
    public Task PruneNetworksAsync() => Guarded(async () =>
    {
        var r = await _resources.PruneNetworksAsync();
        if (!r.Ok) { ErrorMessage = ActionErrText(r); return; }
        StatusMessage = "Pruned unused networks.";
        await LoadNetworksAsync();
    });

    private async Task LoadNetworksAsync()
    {
        Networks.Clear();
        foreach (var n in await _resources.ListNetworksAsync()) Networks.Add(n);
    }

    // ── Sessions tab ────────────────────────────────────────────────────────

    [RelayCommand]
    public Task RefreshSessionsAsync() => Guarded(LoadSessionsAsync);

    /// <summary>Caller is expected to confirm first — terminates the DEFAULT wslc session, which
    /// stops every container running in it, not just one.</summary>
    [RelayCommand]
    public Task TerminateSessionAsync() => Guarded(async () =>
    {
        var r = await _sessions.TerminateDefaultSessionAsync();
        if (!r.Success) { ErrorMessage = r.Message; return; }
        StatusMessage = r.Message;
        await LoadSessionsAsync();
    });

    /// <summary>Caller is expected to confirm first — reclaim terminates the default session
    /// (stopping every container in it) before marking its VHDs sparse.</summary>
    [RelayCommand]
    public Task ReclaimSessionAsync(WslcSessionRow row) => Guarded(async () =>
    {
        var r = await _sessions.ReclaimAsync(row.DisplayName);
        StatusMessage = r.Message;
        if (!r.Success) ErrorMessage = r.Message;
        await LoadSessionsAsync();
    });

    private async Task LoadSessionsAsync()
    {
        Sessions.Clear();
        foreach (var s in await _sessions.ListSessionsAsync())
            Sessions.Add(new WslcSessionRow(s, _sessions.GetDiskUsage(s.DisplayName)));
    }

    // ── Configuration tab (settings.yaml) ──────────────────────────────────

    [RelayCommand]
    public Task LoadSettingsAsync() => Guarded(async () =>
    {
        var result = await _wslcSettings.ReadAsync();
        SettingsCpuCount = result.Settings.CpuCount;
        SettingsMemorySize = result.Settings.MemorySize;
        SettingsMaxStorageSize = result.Settings.MaxStorageSize;
        SettingsDefaultBindingAddress = result.Settings.DefaultBindingAddress;
        SettingsCredentialStore = result.Settings.CredentialStore;

        if (result.ErrorMessage is not null) ErrorMessage = result.ErrorMessage;
        else StatusMessage = result.FileExists ? $"Loaded {SettingsFilePath}." : "No settings.yaml yet — showing defaults.";
    });

    [RelayCommand]
    public Task SaveSettingsAsync() => Guarded(async () =>
    {
        // Final validation pass — field-level errors already track live typing, but re-run them
        // here too so a save can never slip through with a stale/cleared error state.
        CpuCountError = ValidateOrNull(SettingsCpuCount, WslcSettingsService.ValidateCpuCount);
        MemorySizeError = ValidateOrNull(SettingsMemorySize, WslcSettingsService.ValidateMemorySize);
        MaxStorageSizeError = ValidateOrNull(SettingsMaxStorageSize, WslcSettingsService.ValidateMaxStorageSize);
        DefaultBindingAddressError = ValidateOrNull(SettingsDefaultBindingAddress, WslcSettingsService.ValidateDefaultBindingAddress);
        OnPropertyChanged(nameof(SettingsHasErrors));
        if (SettingsHasErrors)
        {
            ErrorMessage = "Fix the highlighted settings before saving.";
            return;
        }

        var settings = new WslcSettings
        {
            CpuCount = Normalize(SettingsCpuCount),
            MemorySize = Normalize(SettingsMemorySize),
            MaxStorageSize = Normalize(SettingsMaxStorageSize),
            DefaultBindingAddress = Normalize(SettingsDefaultBindingAddress),
            CredentialStore = SettingsCredentialStore,
        };
        var result = await _wslcSettings.WriteAsync(settings);
        if (!result.Success) { ErrorMessage = result.ErrorMessage ?? "Failed to save wslc settings."; return; }
        StatusMessage = $"Saved {SettingsFilePath}. {SessionChangesMessage}";
    });

    // ── Runtime bridge ──────────────────────────────────────────────────────

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

    private async Task LoadDistrosAsync()
    {
        Distros.Clear();
        foreach (var d in await _distros.ListAsync()) Distros.Add(d);
    }

    // ── Raw command box ─────────────────────────────────────────────────────

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

    // ── Shared plumbing ─────────────────────────────────────────────────────

    private static string Compose(RawResult r)
    {
        var parts = new[] { r.StdOut, r.StdErr }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim());
        return string.Join("\n", parts);
    }

    private static string ErrText(RawResult r) => string.IsNullOrWhiteSpace(r.StdErr) ? $"wslc exited {r.ExitCode}." : r.StdErr.Trim();
    private static string ActionErrText(WslcActionResult r) => string.IsNullOrWhiteSpace(r.StdErr) ? $"wslc exited {r.ExitCode}." : r.StdErr.Trim();

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
