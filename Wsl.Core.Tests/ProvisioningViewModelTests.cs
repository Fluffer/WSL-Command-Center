using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class ProvisioningViewModelTests
{
    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n" +
        "  Debian    Running   2\r\n";

    private static ProvisioningViewModel NewVm(FakeProcessRunner runner)
    {
        var distros = new WslDistroService(runner);
        var provision = new WslProvisioningService(runner, new WslBackupService(runner));
        return new ProvisioningViewModel(provision, distros);
    }

    [Fact]
    public void Templates_are_seeded_from_catalog()
    {
        var vm = NewVm(new FakeProcessRunner());
        Assert.Equal(TemplateCatalog.BuiltIn.Count, vm.Templates.Count);
    }

    [Fact]
    public async Task LoadDistros_populates_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        var vm = NewVm(runner);

        await vm.LoadDistrosAsync();

        Assert.Equal(2, vm.Distros.Count);
        Assert.Equal("Ubuntu", vm.Distros[0].Name);
    }

    [Fact]
    public async Task ApplyTemplate_requires_distro_and_template()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);          // nothing selected
        vm.SelectedTemplate = vm.Templates[0];

        await vm.ApplyTemplateAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task ApplyTemplate_populates_step_results_on_success()
    {
        var runner = new FakeProcessRunner();
        var tmpl = vmTemplateWithTwoSteps();
        // two steps both succeed
        runner.Enqueue(0, "done1");
        runner.Enqueue(0, "done2");
        var vm = NewVm(runner);
        vm.SelectedDistro = new Distro("Ubuntu", DistroState.Stopped, 2, false);
        vm.Templates.Add(tmpl);
        vm.SelectedTemplate = tmpl;

        await vm.ApplyTemplateAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.StepResults.Count);
        Assert.All(vm.StepResults, r => Assert.True(r.Success));
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task ApplyTemplate_surfaces_failed_step()
    {
        var runner = new FakeProcessRunner();
        var tmpl = vmTemplateWithTwoSteps();
        runner.Enqueue(1, "", "boom");   // first step fails, run stops
        var vm = NewVm(runner);
        vm.SelectedDistro = new Distro("Ubuntu", DistroState.Stopped, 2, false);
        vm.Templates.Add(tmpl);
        vm.SelectedTemplate = tmpl;

        await vm.ApplyTemplateAsync();

        Assert.Single(vm.StepResults);
        Assert.False(vm.StepResults[0].Success);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Clone_requires_all_fields()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);   // no source/name/dir

        await vm.CloneAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task Clone_blocks_name_collision()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);   // LoadDistros
        var vm = NewVm(runner);
        await vm.LoadDistrosAsync();

        vm.CloneSource = vm.Distros[0];      // Ubuntu
        vm.CloneNewName = "debian";          // collides case-insensitively
        vm.CloneInstallDir = @"C:\wsl\x";

        await vm.CloneAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("debian", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(runner.AllArgs);   // only the list from LoadDistros, no export
    }

    [Fact]
    public async Task Clone_success_exports_and_imports()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);   // LoadDistros
        runner.Enqueue(0, "");           // export
        runner.Enqueue(0, "");           // import
        runner.Enqueue(0, ListOutput);   // refresh
        var vm = NewVm(runner);
        await vm.LoadDistrosAsync();

        vm.CloneSource = vm.Distros[0];      // Ubuntu
        vm.CloneNewName = "Ubuntu-clone";
        vm.CloneInstallDir = @"C:\wsl\clone";

        await vm.CloneAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("--export", runner.AllArgs[1][0]);
        Assert.Equal("--import", runner.AllArgs[2][0]);
        Assert.Equal("Ubuntu-clone", runner.AllArgs[2][1]);
        Assert.NotNull(vm.StatusMessage);
    }

    private static DistroTemplate vmTemplateWithTwoSteps() => new(
        "x", "X", "x",
        new[]
        {
            new ProvisioningStep("a", "echo a"),
            new ProvisioningStep("b", "echo b"),
        });
}
