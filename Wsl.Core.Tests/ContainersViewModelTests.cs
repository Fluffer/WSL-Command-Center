using System.IO;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Containers;
using Wsl.Core.Settings;
using Xunit;

namespace Wsl.Core.Tests;

public class ContainersViewModelTests : IDisposable
{
    private readonly string _themeSettingsPath =
        Path.Combine(Path.GetTempPath(), $"wslc-vm-test-{Guid.NewGuid():N}.json");
    private readonly string _wslcSettingsDir =
        Path.Combine(Path.GetTempPath(), $"wslc-vm-settings-{Guid.NewGuid():N}");
    private readonly string _sessionsRoot =
        Path.Combine(Path.GetTempPath(), $"wslc-vm-sessions-{Guid.NewGuid():N}");

    private string WslcSettingsPath => Path.Combine(_wslcSettingsDir, "settings.yaml");

    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n";

    // Real payload shapes, verified against wslc 2.9.3.0 — see the wslc-contract notes.
    private const string ContainerListJson = """
        [
          {
            "CreatedAt": 1785630953,
            "Id": "d60f680b1d8b3cdb417830eeb296663726583e742f29180ca10ca1ce7cf2e6d6",
            "Image": "ubuntu",
            "Name": "wcc-probe",
            "Ports": [],
            "State": 2,
            "StateChangedAt": 1785630969
          }
        ]
        """;

    private const string ImagesJson = """
        [
          {
            "Created": 1781062173,
            "Id": "sha256:5a4c6b929c57abe310fad22db2820f1423e645cdc9344bb05adaca9b50c3403f",
            "Repository": "ubuntu",
            "Size": 100154952,
            "Tag": "latest"
          }
        ]
        """;

    private const string VolumesJson = """[ { "Driver": "guest", "Name": "wcc-probe-vol" } ]""";
    private const string NetworksJson = """[ { "Driver": "bridge", "Id": "a1f055b7f891", "Name": "wcc-probe-net" } ]""";

    private const string SessionListTable =
        "[wslc] Found 1 session\n" +
        "ID   Creator PID   Display Name\n" +
        "1    6132          wslc-cli-peter\n";

    private ContainersViewModel NewVm(FakeProcessRunner runner)
        => new(
            new WslcService(runner),
            new WslcResourceService(runner),
            new WslcSettingsService(() => WslcSettingsPath),
            new WslcSessionService(runner, _sessionsRoot),
            new WslDistroService(runner),
            new ThemeService(_themeSettingsPath));

    private static WslcContainer SampleContainer() => new(
        "d60f680b1d8b3cdb417830eeb296663726583e742f29180ca10ca1ce7cf2e6d6",
        "d60f680b1d8b", "wcc-probe", "ubuntu", null, null, WslcContainerState.Running, "", "Running");

    public void Dispose()
    {
        try { if (File.Exists(_themeSettingsPath)) File.Delete(_themeSettingsPath); } catch { }
        try { if (Directory.Exists(_wslcSettingsDir)) Directory.Delete(_wslcSettingsDir, recursive: true); } catch { }
        try { if (Directory.Exists(_sessionsRoot)) Directory.Delete(_sessionsRoot, recursive: true); } catch { }
    }

    // ── Preview gate ─────────────────────────────────────────────────────────

    [Fact]
    public void Preview_defaults_off_on_fresh_settings()
    {
        var vm = NewVm(new FakeProcessRunner());
        Assert.False(vm.IsPreviewEnabled);
    }

    [Fact]
    public async Task Enabling_preview_persists_and_detects()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wslc 0.1-preview");  // detect
        runner.Enqueue(0, "[]");                 // list --all --format json
        runner.Enqueue(0, ListOutput);            // distro list
        var vm = NewVm(runner);

        await vm.SetPreviewAsync(true);

        Assert.True(vm.IsPreviewEnabled);
        Assert.NotNull(vm.Availability);
        Assert.Equal(WslcState.Available, vm.Availability!.State);

        // a fresh VM reading the same file sees it persisted
        var reread = new ThemeService(_themeSettingsPath).Load();
        Assert.True(reread.EnableWslcPreview);
    }

    [Fact]
    public async Task Disabling_preview_persists_without_detect()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);

        await vm.SetPreviewAsync(false);

        Assert.False(vm.IsPreviewEnabled);
        Assert.Empty(runner.AllArgs);   // no detect call when disabling
    }

    // ── Refresh (Containers + runtime-bridge distros) ──────────────────────

    [Fact]
    public async Task Refresh_when_available_loads_containers_and_distros()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wslc 0.1");            // detect
        runner.Enqueue(0, ContainerListJson);      // list --all --format json
        runner.Enqueue(0, ListOutput);             // distro list
        var vm = NewVm(runner);
        vm.IsPreviewEnabled = true;

        await vm.RefreshAsync();

        Assert.True(vm.Availability!.IsAvailable);
        Assert.Single(vm.Containers);
        Assert.Equal("wcc-probe", vm.Containers[0].Name);
        Assert.Equal(WslcContainerState.Running, vm.Containers[0].State);
        Assert.Single(vm.Distros);
        Assert.Equal(new[] { "list", "--all", "--format", "json" }, runner.AllArgs[1]);
    }

    [Fact]
    public async Task Refresh_when_not_available_skips_container_list()
    {
        var runner = new FakeProcessRunner();
        // detect returns non-zero => Unreachable, so no list call should follow
        runner.Enqueue(1, "", "broken");
        var vm = NewVm(runner);
        vm.IsPreviewEnabled = true;

        await vm.RefreshAsync();

        Assert.Equal(WslcState.Unreachable, vm.Availability!.State);
        Assert.Empty(vm.Containers);
        Assert.Single(runner.AllArgs);   // only the detect call
    }

    // ── Container lifecycle ──────────────────────────────────────────────────

    [Fact]
    public async Task StartContainer_sends_id_and_reloads()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wcc-probe");             // start
        runner.Enqueue(0, ContainerListJson);        // reload
        var vm = NewVm(runner);
        var c = SampleContainer();

        await vm.StartContainerAsync(c);

        Assert.Equal(new[] { "start", c.Id }, runner.AllArgs[0]);
        Assert.Single(vm.Containers);
    }

    [Fact]
    public async Task StopContainer_sends_id_and_reloads()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wcc-probe");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        var c = SampleContainer();

        await vm.StopContainerAsync(c);

        Assert.Equal(new[] { "stop", c.Id }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task RestartContainer_stops_then_starts_then_reloads()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wcc-probe"); // stop
        runner.Enqueue(0, "wcc-probe"); // start
        runner.Enqueue(0, "[]");        // reload
        var vm = NewVm(runner);
        var c = SampleContainer();

        await vm.RestartContainerAsync(c);

        Assert.Equal(new[] { "stop", c.Id }, runner.AllArgs[0]);
        Assert.Equal(new[] { "start", c.Id }, runner.AllArgs[1]);
    }

    [Fact]
    public async Task KillContainer_sends_id()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wcc-probe");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        var c = SampleContainer();

        await vm.KillContainerAsync(c);

        Assert.Equal(new[] { "kill", c.Id }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task RemoveContainer_sends_id()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wcc-probe");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        var c = SampleContainer();

        await vm.RemoveContainerAsync(c);

        Assert.Equal(new[] { "remove", c.Id }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task PruneContainers_calls_container_prune()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);

        await vm.PruneContainersAsync();

        Assert.Equal(new[] { "container", "prune" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task ShowContainerLogs_populates_output()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "line one\nline two");
        var vm = NewVm(runner);

        await vm.ShowContainerLogsAsync(SampleContainer());

        Assert.Contains("wcc-probe", vm.ContainerOutputTitle);
        Assert.Contains("line one", vm.ContainerOutput);
    }

    [Fact]
    public async Task InspectContainer_populates_output()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[{\"Id\":\"abc\"}]");
        var vm = NewVm(runner);

        await vm.InspectContainerAsync(SampleContainer());

        Assert.Contains("Inspect", vm.ContainerOutputTitle);
        Assert.Contains("abc", vm.ContainerOutput);
    }

    // ── State-gated action availability (pure, no XAML host needed) ────────

    [Theory]
    [InlineData(WslcContainerState.Created, true)]
    [InlineData(WslcContainerState.Exited, true)]
    [InlineData(WslcContainerState.Running, false)]
    [InlineData(WslcContainerState.Unknown, false)]
    public void CanStart_matches_spec(WslcContainerState state, bool expected)
        => Assert.Equal(expected, ContainersViewModel.CanStart(state));

    [Theory]
    [InlineData(WslcContainerState.Running, true)]
    [InlineData(WslcContainerState.Created, false)]
    [InlineData(WslcContainerState.Exited, false)]
    [InlineData(WslcContainerState.Unknown, false)]
    public void CanStopOrRestart_matches_spec(WslcContainerState state, bool expected)
        => Assert.Equal(expected, ContainersViewModel.CanStopOrRestart(state));

    // ── Declining confirmation ⇒ command never runs ─────────────────────────
    // The ContentDialog confirmation itself lives in the Page code-behind (a WinUI control, not
    // unit-testable here). What's testable — and asserted below — is the other half of that
    // contract at the ViewModel layer: nothing destructive ever fires as a side effect of a
    // normal refresh. A destructive RelayCommand only ever runs when the page explicitly invokes
    // it, i.e. only after a confirmation dialog has already returned Primary.

    [Fact]
    public async Task Refresh_never_invokes_a_destructive_verb_on_its_own()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wslc 0.1");
        runner.Enqueue(0, ContainerListJson);
        runner.Enqueue(0, ListOutput);
        var vm = NewVm(runner);
        vm.IsPreviewEnabled = true;

        await vm.RefreshAsync();

        Assert.DoesNotContain(runner.AllArgs, a =>
            a.Contains("remove") || a.Contains("kill") || a.Contains("prune") || a.Contains("terminate"));
    }

    // ── Images tab ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshImages_loads_from_contract_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ImagesJson);
        var vm = NewVm(runner);

        await vm.RefreshImagesAsync();

        Assert.Single(vm.Images);
        Assert.Equal("ubuntu:latest", vm.Images[0].RepoTag);
    }

    [Fact]
    public async Task PullImage_blocks_blank_name()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);

        await vm.PullImageAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task PullImage_uses_toplevel_shortcut_and_reloads()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, ImagesJson);
        var vm = NewVm(runner);
        vm.PullImageName = "ubuntu:latest";

        await vm.PullImageAsync();

        Assert.Equal(new[] { "pull", "ubuntu:latest" }, runner.AllArgs[0]);
        Assert.Single(vm.Images);
        Assert.Equal("", vm.PullImageName);
    }

    [Fact]
    public async Task RemoveImage_forces_by_repotag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        var image = new WslcImage("sha256:abc", "abc", "ubuntu", "latest", DateTimeOffset.UtcNow, 1, "1 B");

        await vm.RemoveImageAsync(image);

        Assert.Equal(new[] { "image", "remove", "--force", "ubuntu:latest" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task TagSelectedImage_requires_selection_and_target()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);

        await vm.TagSelectedImageAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task TagSelectedImage_sends_source_and_target()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        vm.SelectedImage = new WslcImage("sha256:abc", "abc", "ubuntu", "latest", DateTimeOffset.UtcNow, 1, "1 B");
        vm.TagTargetInput = "myrepo/ubuntu:v1";

        await vm.TagSelectedImageAsync();

        Assert.Equal(new[] { "tag", "ubuntu:latest", "myrepo/ubuntu:v1" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task PruneImages_passes_all_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);

        await vm.PruneImagesAsync();

        Assert.Equal(new[] { "image", "prune", "--all" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task RegistryLogin_sends_password_via_stdin_never_on_argv()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Login Succeeded");
        var vm = NewVm(runner);
        vm.RegistryServer = "registry.example.com";
        vm.RegistryUsername = "alice";
        const string secret = "s3cr3t-p@ssw0rd";

        await vm.RegistryLoginAsync(secret);

        Assert.Equal(
            new[] { "registry", "login", "--username", "alice", "--password-stdin", "registry.example.com" },
            runner.LastArgs);
        Assert.DoesNotContain(runner.LastArgs!, a => a.Contains(secret));
        Assert.Equal(secret, runner.LastStdin);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task RegistryLogout_omits_server_when_blank()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var vm = NewVm(runner);

        await vm.RegistryLogoutAsync();

        Assert.Equal(new[] { "registry", "logout" }, runner.LastArgs);
    }

    // ── Volumes tab ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshVolumes_loads_from_contract_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, VolumesJson);
        var vm = NewVm(runner);

        await vm.RefreshVolumesAsync();

        Assert.Single(vm.Volumes);
        Assert.Equal("wcc-probe-vol", vm.Volumes[0].Name);
    }

    [Fact]
    public async Task CreateVolume_blocks_blank_name()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);

        await vm.CreateVolumeAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task CreateVolume_passes_driver_and_reloads()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        vm.NewVolumeName = "wcc-probe-vol";
        vm.NewVolumeDriver = "vhd";

        await vm.CreateVolumeAsync();

        Assert.Equal(new[] { "volume", "create", "--driver", "vhd", "wcc-probe-vol" }, runner.AllArgs[0]);
        Assert.Equal("", vm.NewVolumeName);
    }

    [Fact]
    public async Task RemoveVolume_sends_name()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);

        await vm.RemoveVolumeAsync(new WslcVolume("guest", "wcc-probe-vol"));

        Assert.Equal(new[] { "volume", "remove", "wcc-probe-vol" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task PruneVolumes_passes_all_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);

        await vm.PruneVolumesAsync();

        Assert.Equal(new[] { "volume", "prune", "--all" }, runner.AllArgs[0]);
    }

    // ── Networks tab ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshNetworks_loads_from_contract_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, NetworksJson);
        var vm = NewVm(runner);

        await vm.RefreshNetworksAsync();

        Assert.Single(vm.Networks);
        Assert.Equal("wcc-probe-net", vm.Networks[0].Name);
    }

    [Fact]
    public async Task CreateNetwork_passes_driver_and_reloads()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);
        vm.NewNetworkName = "wcc-probe-net";
        vm.NewNetworkDriver = "bridge";

        await vm.CreateNetworkAsync();

        Assert.Equal(new[] { "network", "create", "--driver", "bridge", "wcc-probe-net" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task RemoveNetwork_sends_name()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);

        await vm.RemoveNetworkAsync(new WslcNetwork("bridge", "a1f055b7f891", "wcc-probe-net"));

        Assert.Equal(new[] { "network", "remove", "wcc-probe-net" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task PruneNetworks_sends_bare_prune()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[]");
        var vm = NewVm(runner);

        await vm.PruneNetworksAsync();

        Assert.Equal(new[] { "network", "prune" }, runner.AllArgs[0]);
    }

    // ── Sessions tab ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshSessions_composes_rows_with_disk_usage()
    {
        var dir = Path.Combine(_sessionsRoot, "wslc-cli-peter");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "storage.vhdx"), new byte[770_703_360]);
        File.WriteAllBytes(Path.Combine(dir, "swap.vhdx"), new byte[37_748_736]);

        var runner = new FakeProcessRunner();
        runner.Enqueue(0, SessionListTable);
        var vm = NewVm(runner);

        await vm.RefreshSessionsAsync();

        Assert.Single(vm.Sessions);
        var row = vm.Sessions[0];
        Assert.Equal("wslc-cli-peter", row.DisplayName);
        Assert.Equal(1, row.Id);
        Assert.Equal(6132, row.CreatorPid);
        Assert.NotEqual("unknown", row.TotalHuman);
    }

    [Fact]
    public async Task TerminateSession_sends_no_session_argument()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Session terminated.");
        runner.Enqueue(0, SessionListTable);
        var vm = NewVm(runner);

        await vm.TerminateSessionAsync();

        Assert.Equal(new[] { "system", "session", "terminate" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task ReclaimSession_terminates_the_default_session_first()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Session terminated.");
        runner.Enqueue(0, SessionListTable);
        var vm = NewVm(runner);
        var row = new WslcSessionRow(new WslcSession(1, 6132, "wslc-cli-peter"),
            new WslcSessionDiskUsage("wslc-cli-peter", null, null, "unknown"));

        await vm.ReclaimSessionAsync(row);

        Assert.Equal(new[] { "system", "session", "terminate" }, runner.AllArgs[0]);
    }

    // ── Configuration tab (settings.yaml) ──────────────────────────────────

    [Fact]
    public async Task LoadSettings_reads_five_keys_from_disk()
    {
        Directory.CreateDirectory(_wslcSettingsDir);
        File.WriteAllText(WslcSettingsPath,
            "session:\r\n" +
            "  cpuCount: 4\r\n" +
            "  memorySize: 2GB\r\n" +
            "  maxStorageSize: 500GB\r\n" +
            "  defaultBindingAddress: 127.0.0.1\r\n" +
            "credentialStore: wincred\r\n");
        var vm = NewVm(new FakeProcessRunner());

        await vm.LoadSettingsAsync();

        Assert.Equal("4", vm.SettingsCpuCount);
        Assert.Equal("2GB", vm.SettingsMemorySize);
        Assert.Equal("500GB", vm.SettingsMaxStorageSize);
        Assert.Equal("127.0.0.1", vm.SettingsDefaultBindingAddress);
        Assert.Equal(CredentialStoreKind.Wincred, vm.SettingsCredentialStore);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task SaveSettings_blocks_on_invalid_cpu_count_and_never_writes()
    {
        var vm = NewVm(new FakeProcessRunner());
        vm.SettingsCpuCount = "not-a-number";

        await vm.SaveSettingsAsync();

        Assert.NotNull(vm.CpuCountError);
        Assert.NotNull(vm.ErrorMessage);
        Assert.True(vm.SettingsHasErrors);
        Assert.False(File.Exists(WslcSettingsPath));
    }

    [Fact]
    public async Task SaveSettings_blocks_on_invalid_binding_address()
    {
        var vm = NewVm(new FakeProcessRunner());
        vm.SettingsDefaultBindingAddress = "not-an-ip";

        await vm.SaveSettingsAsync();

        Assert.NotNull(vm.DefaultBindingAddressError);
        Assert.False(File.Exists(WslcSettingsPath));
    }

    [Fact]
    public async Task SaveSettings_writes_valid_values_and_surfaces_session_message()
    {
        var vm = NewVm(new FakeProcessRunner());
        vm.SettingsCpuCount = "4";
        vm.SettingsMemorySize = "2GB";
        vm.SettingsMaxStorageSize = "default";
        vm.SettingsDefaultBindingAddress = "127.0.0.1";
        vm.SettingsCredentialStore = CredentialStoreKind.Wincred;

        await vm.SaveSettingsAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.True(File.Exists(WslcSettingsPath));
        Assert.Contains(WslcSettingsService.SessionChangesRequireNewSessionMessage, vm.StatusMessage);

        var reread = await new WslcSettingsService(() => WslcSettingsPath).ReadAsync();
        Assert.Equal("4", reread.Settings.CpuCount);
        Assert.Equal("2GB", reread.Settings.MemorySize);
        Assert.Equal("default", reread.Settings.MaxStorageSize);
    }

    [Fact]
    public void SettingsFilePath_is_exposed_for_the_UI()
    {
        var vm = NewVm(new FakeProcessRunner());
        Assert.Equal(WslcSettingsPath, vm.SettingsFilePath);
    }

    // ── Raw command box ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRaw_runs_and_captures_output()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "container details", "");
        var vm = NewVm(runner);
        vm.RawCommand = "inspect web";

        await vm.ExecuteRawAsync();

        Assert.Contains("container details", vm.RawOutput);
        Assert.Equal(new[] { "inspect", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task ExecuteRaw_blocks_blank_command()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.RawCommand = "   ";

        await vm.ExecuteRawAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public void RawNeedsConfirm_is_false_for_readonly_verbs_true_otherwise()
    {
        var vm = NewVm(new FakeProcessRunner());

        vm.RawCommand = "ps";
        Assert.False(vm.RawNeedsConfirm);

        vm.RawCommand = "remove web";
        Assert.True(vm.RawNeedsConfirm);
    }

    // ── Runtime bridge ───────────────────────────────────────────────────────

    [Fact]
    public async Task DetectRuntime_sets_detected_runtime()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "docker\n");
        var vm = NewVm(runner);
        vm.SelectedDistro = new Distro("Ubuntu", DistroState.Stopped, 2, false);

        await vm.DetectRuntimeAsync();

        Assert.Equal(ContainerRuntime.Docker, vm.DetectedRuntime);
    }
}
