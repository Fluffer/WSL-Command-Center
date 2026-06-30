using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Containers;
using Wsl.Core.Settings;
using Xunit;

namespace Wsl.Core.Tests;

public class ContainersViewModelTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"wslc-vm-test-{Guid.NewGuid():N}.json");

    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n";

    private ContainersViewModel NewVm(FakeProcessRunner runner)
        => new(new WslcService(runner), new WslDistroService(runner), new ThemeService(_settingsPath));

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch { }
    }

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
        var vm = NewVm(runner);

        await vm.SetPreviewAsync(true);

        Assert.True(vm.IsPreviewEnabled);
        Assert.NotNull(vm.Availability);
        Assert.Equal(WslcState.Available, vm.Availability!.State);

        // a fresh VM reading the same file sees it persisted
        var reread = new ThemeService(_settingsPath).Load();
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

    [Fact]
    public async Task Refresh_when_available_loads_containers_and_distros()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wslc 0.1");      // detect
        runner.Enqueue(0,                    // ps
            "CONTAINER ID   IMAGE        STATUS        NAMES\n" +
            "abc            alpine:3     Up 1 min      web\n");
        runner.Enqueue(0, ListOutput);       // distro list
        var vm = NewVm(runner);
        vm.IsPreviewEnabled = true;

        await vm.RefreshAsync();

        Assert.True(vm.Availability!.IsAvailable);
        Assert.Single(vm.Containers);
        Assert.Equal("web", vm.Containers[0].Name);
        Assert.Single(vm.Distros);
    }

    [Fact]
    public async Task Refresh_when_not_available_skips_container_list()
    {
        var runner = new FakeProcessRunner();
        // detect returns non-zero => Unreachable, so no ps call should follow
        runner.Enqueue(1, "", "broken");
        var vm = NewVm(runner);
        vm.IsPreviewEnabled = true;

        await vm.RefreshAsync();

        Assert.Equal(WslcState.Unreachable, vm.Availability!.State);
        Assert.Empty(vm.Containers);
        Assert.Single(runner.AllArgs);   // only the detect call
    }

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
